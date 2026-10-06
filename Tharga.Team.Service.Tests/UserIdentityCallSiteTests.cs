using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

public class UserIdentityCallSiteTests
{
    private const string Sub = "pairwise-sub";
    private const string Oid = "tenant-oid";

    private static readonly UserIdentityResolver OidResolver = new([DirectoryClaimTypes.ObjectId]);

    private static ClaimsPrincipal EntraPrincipal()
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Sub),
            new Claim(DirectoryClaimTypes.ObjectIdentifier, Oid),
            new Claim(ClaimTypes.Email, "ada@example.com")
        ], "Cookies"));

    private sealed record TestUser : IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
    }

    private sealed class RecordingUserService(ITeamCache cache) : UserServiceBase(null, cache: cache)
    {
        public List<string> LookedUp { get; } = [];

        protected override TimeSpan? LastSeenStampInterval => null;

        protected override Task<IUser> GetUserAsync(ClaimsPrincipal claimsPrincipal)
        {
            var identity = ResolveUserIdentity(claimsPrincipal);
            LookedUp.Add(identity);
            return Task.FromResult<IUser>(new TestUser { Key = "u-1", Identity = identity, EMail = "ada@example.com" });
        }

        protected override async IAsyncEnumerable<IUser> GetAllAsync() { yield break; }
    }

    private static (RecordingUserService Sut, ITeamCache Cache) UserService(UserIdentityResolver resolver)
    {
        var cache = Substitute.For<ITeamCache>();
        cache.GetUserAsync(Arg.Any<string>()).Returns(Task.FromResult(CachedValue<IUser>.Miss));
        var sut = new RecordingUserService(cache);
        if (resolver != null) sut.IdentityResolver = resolver;
        return (sut, cache);
    }

    [Fact]
    public async Task CurrentUser_Default_IsKeyedOnSub()
    {
        var (sut, cache) = UserService(null);

        var user = await sut.GetCurrentUserAsync(EntraPrincipal());

        Assert.Equal(Sub, user.Identity);
        Assert.Equal([Sub], sut.LookedUp);
        await cache.Received(1).GetUserAsync(Sub);
    }

    [Fact]
    public async Task CurrentUser_ConfiguredOid_IsKeyedOnOid_InTheStoreAndTheCache()
    {
        var (sut, cache) = UserService(OidResolver);

        var user = await sut.GetCurrentUserAsync(EntraPrincipal());

        Assert.Equal(Oid, user.Identity);
        Assert.Equal([Oid], sut.LookedUp);
        await cache.Received(1).GetUserAsync(Oid);
        await cache.Received(1).SetUserAsync(Oid, Arg.Any<IUser>());
        await cache.DidNotReceive().GetUserAsync(Sub);
    }

    [Fact]
    public async Task CurrentUser_ConfiguredOid_WithoutOidClaim_ResolvesNoUser()
    {
        var (sut, _) = UserService(OidResolver);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Sub)], "Cookies"));

        var user = await sut.GetCurrentUserAsync(principal);

        Assert.Null(user);
        Assert.Empty(sut.LookedUp);
    }

    private static TeamAuthorizer Authorizer(UserIdentityResolver resolver)
    {
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(EntraPrincipal()));
        return resolver == null ? new TeamAuthorizer(accessor) : new TeamAuthorizer(accessor, resolver);
    }

    [Fact]
    public async Task AuthorizerSubject_Default_IsNameIdentifier()
    {
        Assert.Equal(Sub, await Authorizer(null).GetSubjectAsync());
    }

    [Fact]
    public async Task AuthorizerSubject_ConfiguredOid_IsOid()
    {
        Assert.Equal(Oid, await Authorizer(OidResolver).GetSubjectAsync());
    }

    [Fact]
    public void SignInAudit_Default_RecordsNameIdentifier()
    {
        Assert.Equal(Sub, AuthAuditEntries.SignIn(EntraPrincipal()).CallerUserIdentity);
    }

    [Fact]
    public void SignInAudit_ConfiguredOid_RecordsOid()
    {
        var entry = AuthAuditEntries.SignIn(EntraPrincipal(), OidResolver);

        Assert.Equal(Oid, entry.CallerUserIdentity);
        Assert.Equal("ada@example.com", entry.CallerIdentity);
    }

    [Fact]
    public void UserCreatedAudit_ConfiguredOid_RecordsOid()
    {
        var user = new TestUser { Key = "u-1", Identity = Oid, EMail = "ada@example.com" };

        Assert.Equal(Oid, AuthAuditEntries.UserCreated(user, EntraPrincipal(), OidResolver).CallerUserIdentity);
        Assert.Equal(Sub, AuthAuditEntries.UserCreated(user, EntraPrincipal()).CallerUserIdentity);
    }

    [Fact]
    public void SignInAudit_NullResolver_FallsBackToDefault()
    {
        Assert.Equal(Sub, AuthAuditEntries.SignIn(EntraPrincipal(), null).CallerUserIdentity);
    }

    private static IHttpContextAccessor Request(UserIdentityResolver registered)
    {
        var services = new ServiceCollection();
        if (registered != null) services.AddSingleton(registered);

        var context = new DefaultHttpContext
        {
            User = EntraPrincipal(),
            RequestServices = services.BuildServiceProvider()
        };

        return new HttpContextAccessor { HttpContext = context };
    }

    [Fact]
    public void ServiceCallAudit_NoResolverRegistered_RecordsNameIdentifier()
    {
        var entry = AuditHelper.BuildEntry(Request(null), "team", "rename", "RenameAsync", 0, true);

        Assert.Equal(Sub, entry.CallerUserIdentity);
    }

    [Fact]
    public void ServiceCallAudit_ConfiguredOid_RecordsOid()
    {
        var entry = AuditHelper.BuildEntry(Request(OidResolver), "team", "rename", "RenameAsync", 0, true);

        Assert.Equal(Oid, entry.CallerUserIdentity);
    }
}
