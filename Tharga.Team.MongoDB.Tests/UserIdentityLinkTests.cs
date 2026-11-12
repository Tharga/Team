using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using NSubstitute;
using Tharga.MongoDB;
using Tharga.Team;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// A person who signs in through a new identity provider is found by it: either an administrator moves the identity onto
/// their existing user, or, when the host opts in, a sign-in whose email matches exactly one user takes that user over.
/// </summary>
public class UserIdentityLinkTests
{
    public record TestUserEntity : EntityBase, IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
    }

    private sealed class TestUserService(IUserRepository<TestUserEntity> repo, bool link)
        : UserServiceRepositoryBase<TestUserEntity>(Substitute.For<AuthenticationStateProvider>(), repo)
    {
        protected override bool ShouldLinkToExistingUserByEmail(ClaimsPrincipal claimsPrincipal) => link;

        protected override Task<TestUserEntity> CreateUserEntityAsync(ClaimsPrincipal claimsPrincipal, string identity)
            => Task.FromResult(new TestUserEntity { Identity = identity, Key = "created-key", EMail = "anna@example.com" });
    }

    private readonly IUserRepository<TestUserEntity> _repo = Substitute.For<IUserRepository<TestUserEntity>>();

    // The resolved-user cache is shared by the whole run, so each test signs in as an identity of its own.
    private string NewId { get; } = $"new-{Guid.NewGuid():N}";

    private static ClaimsPrincipal SignedIn(string identity, string email = "anna@example.com")
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, identity), new Claim("email", email)], "test"));

    [Fact]
    public async Task SetUserIdentityAsync_WritesIt_AndDropsBothIdentitiesFromTheCache()
    {
        _repo.GetByKeyAsync("u-1").Returns(new TestUserEntity { Key = "u-1", Identity = "old-id" });

        await new TestUserService(_repo, false).SetUserIdentityAsync("u-1", NewId);

        await _repo.Received(1).SetIdentityAsync("u-1", NewId);
    }

    [Fact]
    public async Task SetUserIdentityAsync_UnknownUser_WritesNothing()
    {
        _repo.GetByKeyAsync("u-gone").Returns((TestUserEntity)null);

        await new TestUserService(_repo, false).SetUserIdentityAsync("u-gone", NewId);

        await _repo.DidNotReceiveWithAnyArgs().SetIdentityAsync(default, default);
    }

    [Fact]
    public async Task SetUserIdentityAsync_BlankIdentity_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new TestUserService(_repo, false).SetUserIdentityAsync("u-1", " "));
        await _repo.DidNotReceiveWithAnyArgs().SetIdentityAsync(default, default);
    }

    [Fact]
    public async Task ByDefault_ANewIdentityWithAKnownEmail_GetsANewUser()
    {
        _repo.GetAsync(NewId).Returns((TestUserEntity)null);
        _repo.TryAddAsync(Arg.Any<TestUserEntity>()).Returns(true);
        _repo.GetByEMailAsync("anna@example.com").Returns([new TestUserEntity { Key = "old", Identity = "old-id", EMail = "anna@example.com" }]);

        var user = await new TestUserService(_repo, false).GetCurrentUserAsync(SignedIn(NewId));

        Assert.Equal("created-key", user.Key);
        await _repo.DidNotReceiveWithAnyArgs().SetIdentityAsync(default, default);
    }

    [Fact]
    public async Task WhenTheHostOptsIn_ANewIdentityWithExactlyOneMatchingEmail_TakesOverThatUser()
    {
        var existing = new TestUserEntity { Key = "old", Identity = "old-id", EMail = "anna@example.com" };
        _repo.GetAsync(NewId).Returns((TestUserEntity)null, existing with { Identity = NewId });
        _repo.GetByEMailAsync("anna@example.com").Returns([existing]);

        var user = await new TestUserService(_repo, true).GetCurrentUserAsync(SignedIn(NewId));

        Assert.Equal("old", user.Key);
        Assert.Equal(NewId, user.Identity);
        await _repo.Received(1).SetIdentityAsync("old", NewId);
        await _repo.DidNotReceiveWithAnyArgs().TryAddAsync(default);
    }

    [Fact]
    public async Task WhenTheHostOptsIn_TwoUsersWithTheEmail_LinkNothing_AndTheSignInCreatesAUser()
    {
        _repo.GetAsync(NewId).Returns((TestUserEntity)null);
        _repo.TryAddAsync(Arg.Any<TestUserEntity>()).Returns(true);
        _repo.GetByEMailAsync("anna@example.com").Returns([
            new TestUserEntity { Key = "a", Identity = "a-id", EMail = "anna@example.com" },
            new TestUserEntity { Key = "b", Identity = "b-id", EMail = "anna@example.com" }]);

        var user = await new TestUserService(_repo, true).GetCurrentUserAsync(SignedIn(NewId));

        Assert.Equal("created-key", user.Key);
        await _repo.DidNotReceiveWithAnyArgs().SetIdentityAsync(default, default);
    }

    [Fact]
    public async Task WhenTheHostOptsIn_NoUserWithTheEmail_TheSignInCreatesAUser()
    {
        _repo.GetAsync(NewId).Returns((TestUserEntity)null);
        _repo.TryAddAsync(Arg.Any<TestUserEntity>()).Returns(true);
        _repo.GetByEMailAsync("anna@example.com").Returns([]);

        Assert.Equal("created-key", (await new TestUserService(_repo, true).GetCurrentUserAsync(SignedIn(NewId))).Key);
    }

    [Fact]
    public async Task AKnownIdentity_IsNeverLookedUpByEmail()
    {
        _repo.GetAsync("old-id").Returns(new TestUserEntity { Key = "old", Identity = "old-id", EMail = "anna@example.com" });

        var user = await new TestUserService(_repo, true).GetCurrentUserAsync(SignedIn("old-id"));

        Assert.Equal("old", user.Key);
        await _repo.DidNotReceiveWithAnyArgs().GetByEMailAsync(default);
    }

    [Fact]
    public async Task WhenLinkingFails_TheSignInStillCreatesAUser()
    {
        _repo.GetAsync(NewId).Returns((TestUserEntity)null);
        _repo.TryAddAsync(Arg.Any<TestUserEntity>()).Returns(true);
        _repo.GetByEMailAsync("anna@example.com").Returns([new TestUserEntity { Key = "old", Identity = "old-id", EMail = "anna@example.com" }]);
        _repo.SetIdentityAsync("old", NewId).Returns<Task>(_ => throw new NotSupportedException("no"));

        Assert.Equal("created-key", (await new TestUserService(_repo, true).GetCurrentUserAsync(SignedIn(NewId))).Key);
    }

    [Fact]
    public async Task ASignInWithNoEmail_IsNotLinked()
    {
        _repo.GetAsync(NewId).Returns((TestUserEntity)null);
        _repo.TryAddAsync(Arg.Any<TestUserEntity>()).Returns(true);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, NewId)], "test"));

        await new TestUserService(_repo, true).GetCurrentUserAsync(principal);

        await _repo.DidNotReceiveWithAnyArgs().GetByEMailAsync(default);
    }
}
