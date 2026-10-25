using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The eviction half of disabling a user: a signed-in caller is signed out, not merely refused next time.
/// </summary>
/// <remarks>
/// An API key is checked on every request, so disabling one takes effect immediately. A signed-in user
/// holds a Blazor circuit with claims already issued and keeps working until something re-evaluates —
/// which is what <c>ClaimRevalidationOptions.Interval</c> exists for. Returning <c>false</c> from
/// <c>ValidateAuthenticationStateAsync</c> is what actually ends the session; the team-claim refresh
/// beside it never does, because a membership change is not a reason to sign anybody out.
/// </remarks>
public class UserEvictionTests
{
    private sealed record FakeUser(string Key, string Identity, string EMail, DateTime? DisabledAt) : IUser;

    private sealed class FakeUserService : IUserService
    {
        private readonly IUser _user;
        private readonly Exception _failure;

        public FakeUserService(IUser user = null, Exception failure = null)
        {
            _user = user;
            _failure = failure;
        }

        public ClaimsPrincipal ReceivedPrincipal { get; private set; }
        public int CallCount { get; private set; }

        public Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal = null)
        {
            CallCount++;
            ReceivedPrincipal = claimsPrincipal;
            if (_failure != null) throw _failure;
            return Task.FromResult(_user);
        }

        public IAsyncEnumerable<IUser> GetAsync() => throw new NotSupportedException();
        public Task SetUserNameAsync(string userKey, string name) => Task.CompletedTask;
        public Task SeedUserNameAsync(string userKey, string name) => Task.CompletedTask;
    }

    private static IServiceProvider Services(IUserService userService)
    {
        var services = new ServiceCollection();
        if (userService != null) services.AddSingleton(userService);
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Principal()
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "Test"));

    [Fact]
    public async Task ADisabledUser_IsEvicted()
    {
        var userService = new FakeUserService(new FakeUser("u1", "alice", "alice@example.com", DateTime.UtcNow));

        Assert.True(await TeamRevalidatingAuthenticationStateProvider.IsDisabledAsync(Services(userService), Principal()));
    }

    [Fact]
    public async Task AnEnabledUser_KeepsTheirSession()
    {
        var userService = new FakeUserService(new FakeUser("u1", "alice", "alice@example.com", null));

        Assert.False(await TeamRevalidatingAuthenticationStateProvider.IsDisabledAsync(Services(userService), Principal()));
    }

    /// <summary>
    /// <b>Fail-open, and deliberately so.</b> Treating a store failure as "disabled" would sign out every
    /// signed-in user at once — a database blip would become an outage. The loop runs again next interval,
    /// so a genuinely disabled user is still evicted, one interval later.
    /// </summary>
    [Fact]
    public async Task AStoreThatThrows_DoesNotSignAnybodyOut()
    {
        var userService = new FakeUserService(failure: new InvalidOperationException("Database unreachable."));

        Assert.False(await TeamRevalidatingAuthenticationStateProvider.IsDisabledAsync(Services(userService), Principal()));
    }

    /// <summary>A host that registered no user store is not signing everyone out on every interval.</summary>
    [Fact]
    public async Task NoUserStoreRegistered_DoesNotSignAnybodyOut()
    {
        Assert.False(await TeamRevalidatingAuthenticationStateProvider.IsDisabledAsync(Services(null), Principal()));
    }

    /// <summary>
    /// An unresolvable caller is not an evictable one. The check runs on a background loop with no
    /// <c>HttpContext</c>, so a null here means "nobody was resolved", not "this person is disabled".
    /// </summary>
    [Fact]
    public async Task AnUnresolvableCaller_DoesNotSignAnybodyOut()
    {
        Assert.False(await TeamRevalidatingAuthenticationStateProvider.IsDisabledAsync(Services(new FakeUserService()), Principal()));
    }

    /// <summary>
    /// The principal must be passed through. The background loop has no ambient caller, so a check that
    /// let the argument default would resolve nobody and evict no one — silently, and only in production.
    /// </summary>
    [Fact]
    public async Task ThePrincipalIsPassedThrough()
    {
        var principal = Principal();
        var userService = new FakeUserService(new FakeUser("u1", "alice", "alice@example.com", null));

        await TeamRevalidatingAuthenticationStateProvider.IsDisabledAsync(Services(userService), principal);

        Assert.Equal(1, userService.CallCount);
        Assert.Same(principal, userService.ReceivedPrincipal);
    }

    /// <summary>
    /// The check runs every interval on an idle tab too, which is not the user doing anything, so it must not record
    /// them as active. (<see cref="FakeUserService"/> implements only the one-argument resolve, as a host's own service
    /// may, and the tests above show the check still reaches it.)
    /// </summary>
    [Fact]
    public async Task TheCheck_DoesNotCountAsTheUserBeingActive()
    {
        var userService = new ActivityAwareUserService(new FakeUser("u1", "alice", "alice@example.com", null));

        await TeamRevalidatingAuthenticationStateProvider.IsDisabledAsync(Services(userService), Principal());

        Assert.Equal([false], userService.RecordActivity);
    }

    private sealed class ActivityAwareUserService(IUser user) : IUserService
    {
        public List<bool> RecordActivity { get; } = [];

        public Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal = null) => GetCurrentUserAsync(claimsPrincipal, recordActivity: true);

        public Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal, bool recordActivity)
        {
            RecordActivity.Add(recordActivity);
            return Task.FromResult(user);
        }

        public IAsyncEnumerable<IUser> GetAsync() => throw new NotSupportedException();
        public Task SetUserNameAsync(string userKey, string name) => Task.CompletedTask;
        public Task SeedUserNameAsync(string userKey, string name) => Task.CompletedTask;
    }
}
