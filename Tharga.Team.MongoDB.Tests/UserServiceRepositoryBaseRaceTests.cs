using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Verifies the catch-DuplicateKey recovery path in <see cref="UserServiceRepositoryBase{TUserEntity}.GetUserAsync"/>.
/// When two first-time logins for the same Identity race, the unique <c>Identity</c> index on
/// <see cref="UserRepositoryCollection{TUserEntity}"/> guarantees one wins. The losing thread catches
/// the <see cref="MongoWriteException"/> with <see cref="ServerErrorCategory.DuplicateKey"/> and re-reads
/// the winning row instead of throwing (issue Tharga/Team#65).
/// </summary>
public class UserServiceRepositoryBaseRaceTests
{
    [Fact]
    public async Task GetUserAsync_NoExistingUser_AddsAndReturnsCandidate()
    {
        var repo = Substitute.For<IUserRepository<TestUserEntity>>();
        repo.GetAsync("alice@example.com").Returns((TestUserEntity)null);
        repo.TryAddAsync(Arg.Any<TestUserEntity>()).Returns(true);

        var sut = new TestUserService(repo, new TestUserEntity { Identity = "alice@example.com", Key = "u-alice" });
        var result = await sut.InvokeGetUserAsync(BuildClaims("alice@example.com"));

        Assert.NotNull(result);
        Assert.Equal("u-alice", result.Key);
        await repo.Received(1).TryAddAsync(Arg.Any<TestUserEntity>());
    }

    [Fact]
    public async Task GetUserAsync_ExistingUser_DoesNotAdd()
    {
        var existing = new TestUserEntity { Identity = "bob@example.com", Key = "u-bob" };
        var repo = Substitute.For<IUserRepository<TestUserEntity>>();
        repo.GetAsync("bob@example.com").Returns(existing);

        var sut = new TestUserService(repo, new TestUserEntity { Identity = "bob@example.com", Key = "should-not-be-used" });
        var result = await sut.InvokeGetUserAsync(BuildClaims("bob@example.com"));

        Assert.Same(existing, result);
        await repo.DidNotReceive().TryAddAsync(Arg.Any<TestUserEntity>());
    }

    [Fact]
    public async Task GetUserAsync_TheInsertIsLost_ReReadsAndReturnsWinner()
    {
        // Race: first GetAsync returns null, candidate is created, and the add answers false because the
        // unique-Identity index already holds it. Recovery re-reads by Identity and returns the winner.
        var winner = new TestUserEntity { Identity = "carol@example.com", Key = "u-winner" };
        var repo = Substitute.For<IUserRepository<TestUserEntity>>();
        repo.GetAsync("carol@example.com").Returns((TestUserEntity)null, winner);
        repo.TryAddAsync(Arg.Any<TestUserEntity>()).Returns(false);

        var sut = new TestUserService(repo, new TestUserEntity { Identity = "carol@example.com", Key = "u-loser" });
        var result = await sut.InvokeGetUserAsync(BuildClaims("carol@example.com"));

        Assert.Same(winner, result);
        await repo.Received(2).GetAsync("carol@example.com");
        await repo.Received(1).TryAddAsync(Arg.Any<TestUserEntity>());
    }

    [Fact]
    public async Task GetUserAsync_TheInsertFailsAndNobodyElseAddedTheUser_Throws()
    {
        // The collection answers false for any write error, so a missing winner means the write really failed,
        // and signing in must not carry on as if it had worked.
        var repo = Substitute.For<IUserRepository<TestUserEntity>>();
        repo.GetAsync("dave@example.com").Returns((TestUserEntity)null);
        repo.TryAddAsync(Arg.Any<TestUserEntity>()).Returns(false);

        var sut = new TestUserService(repo, new TestUserEntity { Identity = "dave@example.com", Key = "u-dave" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InvokeGetUserAsync(BuildClaims("dave@example.com")));
    }

    [Fact]
    public async Task AHostRepository_WithOnlyAddAsync_StillAnswersFalseForADuplicate_AndThrowsForAnythingElse()
    {
        // TryAddAsync is a default interface method, so a host's own repository keeps compiling: it wraps AddAsync.
        IUserRepository<TestUserEntity> duplicate = new HostRepository(BuildDuplicateKeyException());
        IUserRepository<TestUserEntity> broken = new HostRepository(new TimeoutException("The database is down."));
        IUserRepository<TestUserEntity> fine = new HostRepository(null);

        Assert.False(await duplicate.TryAddAsync(new TestUserEntity { Identity = "x" }));
        Assert.True(await fine.TryAddAsync(new TestUserEntity { Identity = "x" }));
        await Assert.ThrowsAsync<TimeoutException>(() => broken.TryAddAsync(new TestUserEntity { Identity = "x" }));
    }

    private sealed class HostRepository(Exception onAdd) : IUserRepository<TestUserEntity>
    {
        public IAsyncEnumerable<TestUserEntity> GetAsync() => AsyncEnumerable.Empty<TestUserEntity>();
        public Task<TestUserEntity> GetAsync(string identity) => Task.FromResult<TestUserEntity>(null);
        public Task<TestUserEntity> GetByKeyAsync(string userKey) => Task.FromResult<TestUserEntity>(null);
        public Task AddAsync(TestUserEntity user) => onAdd == null ? Task.CompletedTask : Task.FromException(onAdd);
        public Task SetNameAsync(string userKey, string name) => Task.CompletedTask;
    }

    private static ClaimsPrincipal BuildClaims(string identity)
    {
        // Tharga.Toolkit's GetIdentity().Identity reads ClaimTypes.NameIdentifier off the principal.
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, identity) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    /// <summary>
    /// Construct a <see cref="MongoWriteException"/> carrying a <see cref="WriteError"/> with
    /// <see cref="ServerErrorCategory.DuplicateKey"/> via reflection. Both types have only
    /// <c>internal</c> constructors in MongoDB.Driver 3.x; we bypass them via
    /// <see cref="RuntimeHelpers.GetUninitializedObject"/> and set backing fields directly.
    /// Fragile across driver upgrades but sufficient for a single test fixture.
    /// </summary>
    private static MongoWriteException BuildDuplicateKeyException()
    {
        var writeError = (WriteError)RuntimeHelpers.GetUninitializedObject(typeof(WriteError));
        SetField(writeError, "_category", ServerErrorCategory.DuplicateKey);
        SetField(writeError, "_code", 11000);
        SetField(writeError, "_message", "E11000 duplicate key error");
        SetField(writeError, "_details", new BsonDocument());

        var exception = (MongoWriteException)RuntimeHelpers.GetUninitializedObject(typeof(MongoWriteException));
        SetField(exception, "_writeError", writeError);
        return exception;
    }

    private static void SetField(object instance, string fieldName, object value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null) throw new InvalidOperationException($"Field '{fieldName}' not found on {instance.GetType().FullName}. The MongoDB.Driver internal layout may have changed.");
        field.SetValue(instance, value);
    }

    public record TestUserEntity : EntityBase, IUser
    {
        public string Identity { get; init; }
        public string Key { get; init; }
        public string EMail { get; init; }
        public string Name { get; init; }
    }

    private sealed class TestUserService : UserServiceRepositoryBase<TestUserEntity>
    {
        private readonly TestUserEntity _candidate;

        public TestUserService(IUserRepository<TestUserEntity> repo, TestUserEntity candidate)
            : base(Substitute.For<AuthenticationStateProvider>(), repo)
        {
            _candidate = candidate;
        }

        protected override Task<TestUserEntity> CreateUserEntityAsync(ClaimsPrincipal claimsPrincipal, string identity)
        {
            return Task.FromResult(_candidate);
        }

        // Expose the protected method for direct invocation in tests.
        public Task<IUser> InvokeGetUserAsync(ClaimsPrincipal principal) => GetUserAsync(principal);
    }
}
