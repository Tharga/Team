using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests.Audit;

/// <summary>
/// An audit entry records what was done to as well as who did it, so "who deleted this user?" is a query rather than
/// a scan of the metadata (Tharga/Team#404).
/// </summary>
public class AuditTargetTests
{
    private readonly FakeAuditBackend _backend;
    private readonly CompositeAuditLogger _logger;

    public AuditTargetTests()
    {
        (_logger, _backend) = FakeAuditLoggerFactory.Create();
    }

    // --- the user decorator ---

    [Fact]
    public async Task DeletingAUser_RecordsThatUserAsTheTarget()
    {
        var inner = Substitute.For<IUserManagementService>();
        inner.DeleteUserAsync("u-1", Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new UserDeleteResult());
        var sut = new AuditingUserManagementServiceDecorator(inner, _logger, Substitute.For<IHttpContextAccessor>());

        await sut.DeleteUserAsync("u-1");

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal(AuditTargetKinds.User, entry.TargetKind);
        Assert.Equal("u-1", entry.TargetKey);
    }

    /// <summary>A failed attempt is the one an operator looks for, so the target is recorded there too.</summary>
    [Fact]
    public async Task AFailedUserOperation_StillRecordsTheTarget()
    {
        var inner = Substitute.For<IUserManagementService>();
        inner.DeleteUserAsync("u-1", Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns<Task<UserDeleteResult>>(_ => throw new InvalidOperationException("boom"));
        var sut = new AuditingUserManagementServiceDecorator(inner, _logger, Substitute.For<IHttpContextAccessor>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DeleteUserAsync("u-1"));

        var entry = Assert.Single(_backend.Entries);
        Assert.False(entry.Success);
        Assert.Equal("u-1", entry.TargetKey);
    }

    [Fact]
    public async Task LinkingTwoUsers_TargetsTheOneThatIsKept()
    {
        var inner = Substitute.For<IUserManagementService>();
        inner.LinkIdentityAsync("keep", "dupe", Arg.Any<CancellationToken>()).Returns(new UserLinkResult("id", null));
        var sut = new AuditingUserManagementServiceDecorator(inner, _logger, Substitute.For<IHttpContextAccessor>());

        await sut.LinkIdentityAsync("keep", "dupe");

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("keep", entry.TargetKey);
        Assert.Equal("dupe", entry.Metadata[AuditMetadataKeys.LinkedUserKey]);
    }

    [Fact]
    public async Task AnOperationOnNoParticularUser_HasNoTarget()
    {
        var inner = Substitute.For<IUserManagementService>();
        inner.VerifyAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<UserVerificationResult>().ToAsyncEnumerable());
        var sut = new AuditingUserManagementServiceDecorator(inner, _logger, Substitute.For<IHttpContextAccessor>());

        await sut.VerifyAllAsync().ToListAsync();

        var entry = Assert.Single(_backend.Entries);
        Assert.Null(entry.TargetKind);
        Assert.Null(entry.TargetKey);
    }

    // --- the team decorator ---

    private (AuditingTeamServiceDecorator Sut, TestTeamService Inner) Teams()
    {
        var userService = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns("user-1");
        userService.GetCurrentUserAsync().Returns(user);

        var inner = new TestTeamService(userService);
        inner.AddTeam("team-1", "Test Team",
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.User, State = MembershipState.Member });

        return (new AuditingTeamServiceDecorator(inner, _logger, Substitute.For<IHttpContextAccessor>()), inner);
    }

    [Fact]
    public async Task ChangingAMembersAccess_TargetsTheMember_NotTheTeam()
    {
        var (sut, _) = Teams();

        await sut.SetMemberRoleAsync("team-1", "user-2", AccessLevel.Administrator);

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal(AuditTargetKinds.User, entry.TargetKind);
        Assert.Equal("user-2", entry.TargetKey);
        Assert.Equal("team-1", entry.TeamKey);
    }

    [Fact]
    public async Task RemovingAMember_TargetsTheMember()
    {
        var (sut, _) = Teams();

        await sut.RemoveMemberAsync("team-1", "user-2");

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal(AuditTargetKinds.User, entry.TargetKind);
        Assert.Equal("user-2", entry.TargetKey);
    }

    [Fact]
    public async Task RenamingATeam_TargetsTheTeam()
    {
        var (sut, _) = Teams();

        await sut.RenameTeamAsync<TestMember>("team-1", "Renamed");

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal(AuditTargetKinds.Team, entry.TargetKind);
        Assert.Equal("team-1", entry.TargetKey);
    }

    /// <summary>An address that is not yet a user has no key to name, so the entry has no target; the address stays in the metadata.</summary>
    [Fact]
    public async Task InvitingAnAddress_HasNoTarget()
    {
        var (sut, _) = Teams();

        await sut.AddMemberAsync("team-1", new InviteUserModel { Email = "new@test.com", AccessLevel = AccessLevel.User });

        var entry = Assert.Single(_backend.Entries);
        Assert.Null(entry.TargetKind);
        Assert.Null(entry.TargetKey);
    }

    // --- the API key decorator ---

    [Fact]
    public async Task LockingATeamKey_TargetsTheKey()
    {
        var sut = new AuditingApiKeyServiceDecorator(Substitute.For<IApiKeyAdministrationService>(), _logger, Substitute.For<IHttpContextAccessor>());

        await sut.LockKeyAsync("team-1", "key-1");

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal(AuditTargetKinds.ApiKey, entry.TargetKind);
        Assert.Equal("key-1", entry.TargetKey);
    }

    [Fact]
    public async Task ASystemKeyOperation_TargetsTheKey()
    {
        var sut = new AuditingApiKeyServiceDecorator(Substitute.For<IApiKeyAdministrationService>(), _logger, Substitute.For<IHttpContextAccessor>());

        await sut.DeleteSystemKeyAsync("sys-1");
        await sut.SetSystemKeyScopesAsync("sys-2", ["a:b"]);

        Assert.Collection(_backend.Entries,
            first => Assert.Equal(("sys-1", AuditTargetKinds.ApiKey), (first.TargetKey, first.TargetKind)),
            second => Assert.Equal(("sys-2", AuditTargetKinds.ApiKey), (second.TargetKey, second.TargetKind)));
    }

    // --- the entry and the query ---

    /// <summary>A kind with no key, or a key with no kind, names nothing a filter could match, so neither is kept.</summary>
    [Theory]
    [InlineData("user", null)]
    [InlineData(null, "u-1")]
    [InlineData("user", " ")]
    public void AKindWithoutAKey_OrTheReverse_IsNotRecorded(string kind, string key)
    {
        var entry = AuditHelper.BuildEntry(null, "f", "a", "m", 0, true, targetKind: kind, targetKey: key);

        Assert.Null(entry.TargetKind);
        Assert.Null(entry.TargetKey);
    }

    [Fact]
    public void TheTargetFilter_IsAnExactMatchOnKindAndKey()
    {
        var rendered = Render(new AuditQuery { TargetKind = AuditTargetKinds.User, TargetKey = "u-1" });

        Assert.Equal("user", rendered["TargetKind"].AsString);
        Assert.Equal("u-1", rendered["TargetKey"].AsString);
    }

    /// <summary>Entries written before targets were recorded have none, and a filter on a target leaves them out.</summary>
    [Fact]
    public void WithNoTargetFilter_NothingIsAddedToTheQuery()
    {
        Assert.Equal(new BsonDocument(), Render(new AuditQuery()));
    }

    /// <summary>
    /// Stored sparsely, so the great majority of entries, which have no target, cost nothing, and a filter on a target can
    /// never match one. Read from the class map rather than by serializing a whole entity, which needs a database's Guid setting.
    /// </summary>
    [Theory]
    [InlineData(nameof(AuditEntryEntity.TargetKind))]
    [InlineData(nameof(AuditEntryEntity.TargetKey))]
    public void TheTargetIsStoredUnderItsOwnName_AndOmittedWhenThereIsNone(string member)
    {
        var map = BsonClassMap.LookupClassMap(typeof(AuditEntryEntity)).GetMemberMap(member);

        Assert.Equal(member, map.ElementName);
        Assert.True(map.IgnoreIfNull);
    }

    private static BsonDocument Render(AuditQuery query)
    {
        var registry = BsonSerializer.SerializerRegistry;
        return MongoDbAuditLogger.BuildFilter(query)
            .Render(new RenderArgs<AuditEntryEntity>(registry.GetSerializer<AuditEntryEntity>(), registry));
    }
}
