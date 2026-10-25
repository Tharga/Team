using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Verifies the throttled LastSeen stamping and one-shot DirectoryId backfill that
/// <see cref="UserServiceBase.GetCurrentUserAsync"/> performs on every resolve. The throttle and
/// backfill guards are static (per process), so every test uses unique identities and user keys.
/// </summary>
public class UserServiceBaseActivityStampingTests
{
    [Fact]
    public async Task GetCurrentUserAsync_StampsLastSeenOnResolve()
    {
        var (sut, repo, principal, key) = Build();

        await sut.GetCurrentUserAsync(principal);

        await repo.Received(1).SetLastSeenAsync(key, Arg.Any<DateTime>());
    }

    [Fact]
    public async Task GetCurrentUserAsync_SecondResolveWithinInterval_DoesNotStampAgain()
    {
        var (sut, repo, principal, key) = Build();

        await sut.GetCurrentUserAsync(principal);
        await sut.GetCurrentUserAsync(principal);

        await repo.Received(1).SetLastSeenAsync(key, Arg.Any<DateTime>());
    }

    [Fact]
    public async Task GetCurrentUserAsync_ZeroInterval_StampsEveryResolve()
    {
        var (sut, repo, principal, key) = Build(intervalMinutes: 0);

        await sut.GetCurrentUserAsync(principal);
        await sut.GetCurrentUserAsync(principal);

        await repo.Received(2).SetLastSeenAsync(key, Arg.Any<DateTime>());
    }

    [Fact]
    public async Task GetCurrentUserAsync_NullInterval_NeverStamps()
    {
        var (sut, repo, principal, _) = Build(intervalMinutes: null);

        await sut.GetCurrentUserAsync(principal);

        await repo.DidNotReceiveWithAnyArgs().SetLastSeenAsync(default, default);
    }

    [Fact]
    public async Task GetCurrentUserAsync_StampFails_StillReturnsUser()
    {
        var (sut, repo, principal, key) = Build();
        repo.SetLastSeenAsync(key, Arg.Any<DateTime>()).Returns<Task>(_ => throw new InvalidOperationException("store down"));

        var user = await sut.GetCurrentUserAsync(principal);

        Assert.NotNull(user);
        Assert.Equal(key, user.Key);
    }

    /// <summary>A resolve that is not the user doing anything, such as claim revalidation on an idle tab, is not last seen.</summary>
    [Fact]
    public async Task GetCurrentUserAsync_NotRecordingActivity_DoesNotStampLastSeen()
    {
        var (sut, repo, principal, key) = Build();

        var user = await sut.GetCurrentUserAsync(principal, recordActivity: false);

        Assert.Equal(key, user.Key);
        await repo.DidNotReceiveWithAnyArgs().SetLastSeenAsync(default, default);
    }

    /// <summary>A read that records nothing must not use up the throttle either, or the next real request would go unrecorded.</summary>
    [Fact]
    public async Task GetCurrentUserAsync_NotRecordingActivity_LeavesTheNextResolveToStamp()
    {
        var (sut, repo, principal, key) = Build();

        await sut.GetCurrentUserAsync(principal, recordActivity: false);
        await sut.GetCurrentUserAsync(principal);

        await repo.Received(1).SetLastSeenAsync(key, Arg.Any<DateTime>());
    }

    [Fact]
    public async Task GetCurrentUserAsync_RecordingActivity_StampsAsTheOrdinaryResolveDoes()
    {
        var (sut, repo, principal, key) = Build();

        await sut.GetCurrentUserAsync(principal, recordActivity: true);

        await repo.Received(1).SetLastSeenAsync(key, Arg.Any<DateTime>());
    }

    /// <summary>Filling in the directory id keeps the record right; it is not activity, so it still happens.</summary>
    [Fact]
    public async Task GetCurrentUserAsync_NotRecordingActivity_StillBackfillsTheDirectoryId()
    {
        var (sut, repo, principal, key) = Build(oid: "oid-quiet");

        await sut.GetCurrentUserAsync(principal, recordActivity: false);

        await repo.Received(1).SetDirectoryIdAsync(key, "oid-quiet");
    }

    [Fact]
    public async Task GetCurrentUserAsync_NoDirectoryId_BackfillsFromOidClaimOnce()
    {
        var (sut, repo, principal, key) = Build(oid: "oid-123");

        await sut.GetCurrentUserAsync(principal);
        await sut.GetCurrentUserAsync(principal);

        await repo.Received(1).SetDirectoryIdAsync(key, "oid-123");
    }

    [Fact]
    public async Task GetCurrentUserAsync_NoOidClaim_DoesNotBackfill()
    {
        var (sut, repo, principal, _) = Build();

        await sut.GetCurrentUserAsync(principal);

        await repo.DidNotReceiveWithAnyArgs().SetDirectoryIdAsync(default, default);
    }

    [Fact]
    public async Task GetCurrentUserAsync_RawOidClaim_BackfillsToo()
    {
        var (sut, repo, principal, key) = Build(oid: "oid-raw", useMappedOidClaim: false);

        await sut.GetCurrentUserAsync(principal);

        await repo.Received(1).SetDirectoryIdAsync(key, "oid-raw");
    }

    private static (TestUserService Sut, IUserRepository<TestUserEntity> Repo, ClaimsPrincipal Principal, string Key) Build(
        double? intervalMinutes = 15, string oid = null, bool useMappedOidClaim = true)
    {
        var identity = $"id-{Guid.NewGuid():N}";
        var key = $"u-{Guid.NewGuid():N}";
        var entity = new TestUserEntity { Identity = identity, Key = key };

        var repo = Substitute.For<IUserRepository<TestUserEntity>>();
        repo.GetAsync(identity).Returns(entity);
        repo.GetByKeyAsync(key).Returns(entity);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, identity) };
        if (oid != null)
        {
            claims.Add(new Claim(useMappedOidClaim ? DirectoryClaimTypes.ObjectIdentifier : DirectoryClaimTypes.ObjectId, oid));
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var sut = new TestUserService(repo)
        {
            Interval = intervalMinutes == null ? null : TimeSpan.FromMinutes(intervalMinutes.Value)
        };

        return (sut, repo, principal, key);
    }

    public record TestUserEntity : EntityBase, IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
    }

    private sealed class TestUserService : UserServiceRepositoryBase<TestUserEntity>
    {
        public TestUserService(IUserRepository<TestUserEntity> repo)
            : base(Substitute.For<AuthenticationStateProvider>(), repo)
        {
        }

        public TimeSpan? Interval { get; set; } = TimeSpan.FromMinutes(15);

        protected override TimeSpan? LastSeenStampInterval => Interval;

        protected override Task<TestUserEntity> CreateUserEntityAsync(ClaimsPrincipal claimsPrincipal, string identity)
            => Task.FromResult(new TestUserEntity { Identity = identity, Key = Guid.NewGuid().ToString() });
    }
}
