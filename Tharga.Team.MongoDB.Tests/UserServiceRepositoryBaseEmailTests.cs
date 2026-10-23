using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// An administrator setting a user's address on the built-in store: written through the repository, the cached
/// user dropped behind it so the old address is not served, and nothing done for a user who is gone.
/// </summary>
public class UserServiceRepositoryBaseEmailTests
{
    private readonly IUserRepository<TestUserEntity> _repo = Substitute.For<IUserRepository<TestUserEntity>>();
    private readonly ITeamCache _cache = Substitute.For<ITeamCache>();
    private readonly TestUserService _sut;

    public UserServiceRepositoryBaseEmailTests()
    {
        _sut = new TestUserService(_repo, _cache);
    }

    [Fact]
    public async Task SetUserEmailAsync_UserFound_WritesAndDropsTheCachedUser()
    {
        _repo.GetByKeyAsync("u-1").Returns(new TestUserEntity { Key = "u-1", Identity = "id-1", EMail = "old@example.com" });

        await _sut.SetUserEmailAsync("u-1", "new@example.com");

        await _repo.Received(1).SetEMailAsync("u-1", "new@example.com");
        await _cache.Received().RemoveUserAsync("id-1");
    }

    [Fact]
    public async Task SetUserEmailAsync_UserMissing_NoWrite()
    {
        _repo.GetByKeyAsync("u-gone").Returns((TestUserEntity)null);

        await _sut.SetUserEmailAsync("u-gone", "new@example.com");

        await _repo.DidNotReceiveWithAnyArgs().SetEMailAsync(default, default);
    }

    [Fact]
    public async Task SetUserEmailAsync_EmptyKey_NoOp()
    {
        await _sut.SetUserEmailAsync(null, "new@example.com");

        await _repo.DidNotReceiveWithAnyArgs().GetByKeyAsync(default);
        await _repo.DidNotReceiveWithAnyArgs().SetEMailAsync(default, default);
    }

    public record TestUserEntity : EntityBase, IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
    }

    private sealed class TestUserService : UserServiceRepositoryBase<TestUserEntity>
    {
        public TestUserService(IUserRepository<TestUserEntity> repo, ITeamCache cache)
            : base(Substitute.For<AuthenticationStateProvider>(), repo, cache: cache)
        {
        }

        protected override Task<TestUserEntity> CreateUserEntityAsync(ClaimsPrincipal claimsPrincipal, string identity)
            => Task.FromResult(new TestUserEntity { Identity = identity, Key = Guid.NewGuid().ToString() });
    }
}
