using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// <see cref="UserServiceRepositoryBase{TUserEntity}"/> writes a refreshed profile through the repository, touching
/// only the values that changed.
/// </summary>
public class UserServiceRepositoryBaseProfileRefreshTests
{
    private const string StoredName = "Anna Andersson";
    private const string StoredEMail = "anna@example.com";

    [Fact]
    public async Task AChangedName_IsWrittenThroughSetNameAsyncOnly()
    {
        var (sut, repo, key, identity) = Build();

        await sut.GetCurrentUserAsync(Principal(identity, "Anna Svensson", StoredEMail));

        await repo.Received(1).SetNameAsync(key, "Anna Svensson");
        await repo.DidNotReceiveWithAnyArgs().SetEMailAsync(default, default);
    }

    [Fact]
    public async Task AChangedEMail_IsWrittenThroughSetEMailAsyncOnly()
    {
        var (sut, repo, key, identity) = Build();

        await sut.GetCurrentUserAsync(Principal(identity, StoredName, "anna.svensson@example.com"));

        await repo.Received(1).SetEMailAsync(key, "anna.svensson@example.com");
        await repo.DidNotReceiveWithAnyArgs().SetNameAsync(default, default);
    }

    [Fact]
    public async Task BothChanged_AreBothWritten()
    {
        var (sut, repo, key, identity) = Build();

        await sut.GetCurrentUserAsync(Principal(identity, "Anna Svensson", "anna.svensson@example.com"));

        await repo.Received(1).SetNameAsync(key, "Anna Svensson");
        await repo.Received(1).SetEMailAsync(key, "anna.svensson@example.com");
    }

    private static (TestUserService Sut, IUserRepository<TestUserEntity> Repo, string Key, string Identity) Build()
    {
        var identity = $"id-{Guid.NewGuid():N}";
        var key = $"u-{Guid.NewGuid():N}";
        var entity = new TestUserEntity { Identity = identity, Key = key, Name = StoredName, EMail = StoredEMail };

        var repo = Substitute.For<IUserRepository<TestUserEntity>>();
        repo.GetAsync(identity).Returns(entity);
        repo.GetByKeyAsync(key).Returns(entity);

        return (new TestUserService(repo), repo, key, identity);
    }

    private static ClaimsPrincipal Principal(string identity, string name, string email)
        => new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, identity), new Claim("name", name), new Claim("email", email)],
            "test"));

    public record TestUserEntity : EntityBase, IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
        public string Name { get; init; }
    }

    private sealed class TestUserService : UserServiceRepositoryBase<TestUserEntity>
    {
        public TestUserService(IUserRepository<TestUserEntity> repo)
            : base(Substitute.For<AuthenticationStateProvider>(), repo, cache: Substitute.For<ITeamCache>())
        {
        }

        protected override bool ShouldRefreshProfile(ClaimsPrincipal claimsPrincipal) => true;

        protected override TimeSpan? LastSeenStampInterval => null;

        protected override Task<TestUserEntity> CreateUserEntityAsync(ClaimsPrincipal claimsPrincipal, string identity)
            => Task.FromResult(new TestUserEntity { Identity = identity, Key = Guid.NewGuid().ToString() });
    }
}
