using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using MongoDB.Driver;
using NSubstitute;
using Tharga.MongoDB;
using Tharga.Team;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// <c>UserServiceBase.UserCreatedEvent</c> — raised when a user record is created because someone signed in
/// for the first time, so the creation can be audited and routed to Slack (Tharga/Team#142).
/// </summary>
/// <remarks>
/// <b>The race case is the reason this file exists.</b> <c>GetUserAsync</c> catches a duplicate key and
/// re-reads the winner — the fix for Tharga/Team#65, where two concurrent first sign-ins for one identity
/// both tried to insert. The loser of that race did not create anything, so it must not announce a creation:
/// otherwise one user yields two audit entries and two Slack messages, and the audit log says a person was
/// created twice.
/// <para>
/// That guarantee previously rested on where the call was placed and a comment saying why. Placement is not
/// enforcement — a later edit moving the raise below the try block would be silent.
/// </para>
/// </remarks>
public class UserCreatedEventTests
{
    public record TestUserEntity : EntityBase, IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
    }

    private sealed class TestUserService(IUserRepository<TestUserEntity> repo)
        : UserServiceRepositoryBase<TestUserEntity>(Substitute.For<AuthenticationStateProvider>(), repo)
    {
        protected override Task<TestUserEntity> CreateUserEntityAsync(ClaimsPrincipal claimsPrincipal, string identity)
            => Task.FromResult(new TestUserEntity { Identity = identity, Key = "created-key", EMail = "new@test.com" });
    }

    private static (TestUserService Sut, IUserRepository<TestUserEntity> Repo, ClaimsPrincipal Principal, string Identity) Build()
    {
        var identity = $"id-{Guid.NewGuid():N}";
        var repo = Substitute.For<IUserRepository<TestUserEntity>>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, identity)], "test"));

        return (new TestUserService(repo), repo, principal, identity);
    }

    /// <summary>
    /// A <see cref="MongoWriteException"/> the production filter will actually match. It guards on
    /// <c>WriteError?.Category == ServerErrorCategory.DuplicateKey</c>, so an exception without a populated
    /// write error propagates instead of being caught — which would test the wrong path entirely.
    /// The type has no public constructor, so the field is set directly.
    /// </summary>
    private static MongoWriteException DuplicateKey()
    {
        var ex = (MongoWriteException)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(MongoWriteException));

        var writeError = (WriteError)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(WriteError));

        typeof(WriteError)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(ServerErrorCategory))
            .SetValue(writeError, ServerErrorCategory.DuplicateKey);

        var field = typeof(MongoWriteException)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(WriteError));

        field.SetValue(ex, writeError);
        return ex;
    }

    [Fact]
    public async Task AFirstSignIn_RaisesTheEvent()
    {
        var (sut, repo, principal, identity) = Build();
        repo.GetAsync(identity).Returns((TestUserEntity)null);

        var raised = new List<UserCreatedEventArgs>();
        sut.UserCreatedEvent += (_, e) => raised.Add(e);

        await sut.GetCurrentUserAsync(principal);

        var e = Assert.Single(raised);
        Assert.Equal("created-key", e.User.Key);
        Assert.Equal("new@test.com", e.User.EMail);
        Assert.Same(principal, e.Principal);
    }

    /// <summary>An existing user is not a creation — signing in again must announce nothing.</summary>
    [Fact]
    public async Task AReturningUser_RaisesNothing()
    {
        var (sut, repo, principal, identity) = Build();
        repo.GetAsync(identity).Returns(new TestUserEntity { Identity = identity, Key = "existing" });

        var raised = 0;
        sut.UserCreatedEvent += (_, _) => raised++;

        await sut.GetCurrentUserAsync(principal);

        Assert.Equal(0, raised);
    }

    /// <summary>
    /// The point of the file: losing the insert race means someone else created the record. The loser re-reads
    /// the winner and must announce nothing, or one user produces two creation entries.
    /// </summary>
    [Fact]
    public async Task LosingTheInsertRace_RaisesNothing()
    {
        var (sut, repo, principal, identity) = Build();

        var winner = new TestUserEntity { Identity = identity, Key = "winner" };
        repo.GetAsync(identity).Returns(_ => null, _ => winner);
        repo.AddAsync(Arg.Any<TestUserEntity>()).Returns<Task>(_ => throw DuplicateKey());

        var raised = 0;
        sut.UserCreatedEvent += (_, _) => raised++;

        var user = await sut.GetCurrentUserAsync(principal);

        Assert.Equal(0, raised);
        Assert.Equal("winner", user.Key);
    }

    /// <summary>
    /// No subscriber is the default — nothing registers one unless auditing is configured — so the creation
    /// path must be unaffected by there being nobody listening.
    /// </summary>
    [Fact]
    public async Task WithNoSubscriber_TheUserIsStillCreated()
    {
        var (sut, repo, principal, identity) = Build();
        repo.GetAsync(identity).Returns((TestUserEntity)null);

        var user = await sut.GetCurrentUserAsync(principal);

        Assert.Equal("created-key", user.Key);
    }
}
