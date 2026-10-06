using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

public class UserServiceRepositoryBaseIdentityClaimTests
{
    private const string Sub = "pairwise-sub";
    private const string Oid = "tenant-oid";

    private static ClaimsPrincipal EntraPrincipal()
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Sub),
            new Claim(DirectoryClaimTypes.ObjectIdentifier, Oid)
        ], "test"));

    [Fact]
    public async Task Default_FindsTheUserBySub()
    {
        var existing = new TestUserEntity { Identity = Sub, Key = "u-sub" };
        var repo = Substitute.For<IUserRepository<TestUserEntity>>();
        repo.GetAsync(Sub).Returns(existing);
        var sut = new TestUserService(repo);

        var result = await sut.InvokeGetUserAsync(EntraPrincipal());

        Assert.Same(existing, result);
    }

    [Fact]
    public async Task ConfiguredOid_FindsTheOidKeyedUser_NotTheSubKeyedOne()
    {
        var oidUser = new TestUserEntity { Identity = Oid, Key = "u-oid" };
        var repo = Substitute.For<IUserRepository<TestUserEntity>>();
        repo.GetAsync(Oid).Returns(oidUser);
        repo.GetAsync(Sub).Returns(new TestUserEntity { Identity = Sub, Key = "u-sub" });
        var sut = new TestUserService(repo) { IdentityResolver = new UserIdentityResolver([DirectoryClaimTypes.ObjectId]) };

        var result = await sut.InvokeGetUserAsync(EntraPrincipal());

        Assert.Same(oidUser, result);
        await repo.DidNotReceive().GetAsync(Sub);
    }

    [Fact]
    public async Task ConfiguredOid_CreatesTheNewUserUnderOid()
    {
        var repo = Substitute.For<IUserRepository<TestUserEntity>>();
        repo.GetAsync(Arg.Any<string>()).Returns((TestUserEntity)null);
        var sut = new TestUserService(repo) { IdentityResolver = new UserIdentityResolver([DirectoryClaimTypes.ObjectId]) };

        var result = await sut.InvokeGetUserAsync(EntraPrincipal());

        Assert.Equal(Oid, sut.CreatedWithIdentity);
        Assert.Equal(Oid, result.Identity);
        await repo.Received(1).AddAsync(Arg.Is<TestUserEntity>(x => x.Identity == Oid));
    }

    public record TestUserEntity : EntityBase, IUser
    {
        public string Identity { get; init; }
        public string Key { get; init; }
        public string EMail { get; init; }
        public string Name { get; init; }
    }

    private sealed class TestUserService(IUserRepository<TestUserEntity> repo)
        : UserServiceRepositoryBase<TestUserEntity>(Substitute.For<AuthenticationStateProvider>(), repo)
    {
        public string CreatedWithIdentity { get; private set; }

        protected override Task<TestUserEntity> CreateUserEntityAsync(ClaimsPrincipal claimsPrincipal, string identity)
        {
            CreatedWithIdentity = identity;
            return Task.FromResult(new TestUserEntity { Identity = identity, Key = "u-new" });
        }

        public Task<IUser> InvokeGetUserAsync(ClaimsPrincipal principal) => GetUserAsync(principal);
    }
}
