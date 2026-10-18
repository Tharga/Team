using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Every member write sends one update of only what changes, never the member list read and written back
/// (Tharga/Team#338). A write that carried the rest of the roster could put back a member another administrator
/// had just removed, or undo a suspension, so overlapping writes are only safe if none of them does.
/// </summary>
public class TeamRepositoryMemberWriteTests
{
    public record TestMember : TeamMemberBase;
    public record TestTeamEntity : TeamEntityBase<TestMember>;

    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private const string MemberFilter = """{ "Key" : "T1", "Members" : { "$elemMatch" : { "Key" : "u1" } } }""";
    private const string InvitationFilter = """{ "Key" : "T1", "Members" : { "$elemMatch" : { "Invitation" : { "$ne" : null }, "Invitation.InviteKey" : "inv1" } } }""";

    private static readonly Dictionary<string, Func<TeamRepository<TestTeamEntity, TestMember>, Task>> AllWrites = new()
    {
        ["RemoveMember"] = r => r.RemoveMemberAsync("T1", "u1"),
        ["RemoveMemberFromAllTeams"] = r => r.RemoveMemberFromAllTeamsAsync("u1"),
        ["SetMemberRole"] = r => r.SetMemberRoleAsync("T1", "u1", AccessLevel.Administrator),
        ["SetMemberSuspended"] = r => r.SetMemberSuspendedAsync("T1", "u1", Now, "admin"),
        ["SetMemberTenantRoles"] = r => r.SetMemberTenantRolesAsync("T1", "u1", ["Auditor"]),
        ["SetMemberScopeOverrides"] = r => r.SetMemberScopeOverridesAsync("T1", "u1", ["case:read"]),
        ["SetMemberName"] = r => r.SetMemberNameAsync("T1", "u1", "Ann"),
        ["SetInvitationExpiry"] = r => r.SetInvitationExpiryAsync("T1", "inv1", Now),
        ["SetInvitationResponse"] = r => r.SetInvitationResponseAsync("T1", "u1", "inv1", true),
        ["SetLastSeen"] = r => r.SetLastSeenAsync("T1", "u1", Now),
    };

    public static TheoryData<string> Writes() => new(AllWrites.Keys);

    private static RenderArgs<TestTeamEntity> Args
        => new(BsonSerializer.LookupSerializer<TestTeamEntity>(), BsonSerializer.SerializerRegistry);

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task AWrite_NeverReadsTheTeamOrWritesTheMemberList(string write)
    {
        var capture = await RunAsync(AllWrites[write]);

        Assert.NotEmpty(capture.Updates);
        Assert.All(capture.Updates, update =>
            Assert.False(update.TryGetValue("$set", out var set) && set.AsBsonDocument.Contains("Members"),
                $"{write} wrote the whole member list: {update}"));
        await capture.Collection.DidNotReceiveWithAnyArgs().GetOneAsync(default(Expression<Func<TestTeamEntity, bool>>));
        await capture.Collection.DidNotReceiveWithAnyArgs().GetOneAsync(default(FilterDefinition<TestTeamEntity>));
        await capture.Collection.DidNotReceiveWithAnyArgs().ReplaceOneAsync(default);
    }

    /// <summary>The mode that sends the filter with the update; see <c>TeamRepositoryLastSeenTests</c>.</summary>
    [Theory]
    [MemberData(nameof(Writes))]
    public async Task AWrite_IsOneFindAndModify(string write)
    {
        var capture = await RunAsync(AllWrites[write]);

        Assert.NotEmpty(capture.Options);
        Assert.All(capture.Options, options => Assert.Equal(EMode.FirstOrDefault, options?.Mode));
    }

    [Fact]
    public async Task ARoleChange_SetsOnlyThatMembersLevel()
    {
        var capture = await RunAsync(r => r.SetMemberRoleAsync("T1", "u1", AccessLevel.Administrator));

        AssertSingle(capture, MemberFilter, """{ "$set" : { "Members.$.AccessLevel" : "Administrator" } }""");
    }

    [Fact]
    public async Task ASuspension_SetsWhenAndBy()
    {
        var capture = await RunAsync(r => r.SetMemberSuspendedAsync("T1", "u1", Now, "admin"));

        var expected = new BsonDocument("$set", new BsonDocument
        {
            { "Members.$.SuspendedAt", Now },
            { "Members.$.SuspendedBy", "admin" }
        });
        AssertSingle(capture, MemberFilter, expected);
    }

    /// <summary>Removed, not stored as null: the fields are ignored when null, so a whole-member write left them absent.</summary>
    [Fact]
    public async Task LiftingASuspension_RemovesBothFields()
    {
        var capture = await RunAsync(r => r.SetMemberSuspendedAsync("T1", "u1", null, null));

        AssertSingle(capture, MemberFilter, """{ "$unset" : { "Members.$.SuspendedAt" : 1, "Members.$.SuspendedBy" : 1 } }""");
    }

    [Fact]
    public async Task TenantRoles_AreSet()
    {
        var capture = await RunAsync(r => r.SetMemberTenantRolesAsync("T1", "u1", ["Auditor"]));

        AssertSingle(capture, MemberFilter, """{ "$set" : { "Members.$.TenantRoles" : ["Auditor"] } }""");
    }

    [Fact]
    public async Task ClearedTenantRoles_AreRemoved()
    {
        var capture = await RunAsync(r => r.SetMemberTenantRolesAsync("T1", "u1", null));

        AssertSingle(capture, MemberFilter, """{ "$unset" : { "Members.$.TenantRoles" : 1 } }""");
    }

    [Fact]
    public async Task ScopeOverrides_AreSet()
    {
        var capture = await RunAsync(r => r.SetMemberScopeOverridesAsync("T1", "u1", ["case:read"]));

        AssertSingle(capture, MemberFilter, """{ "$set" : { "Members.$.ScopeOverrides" : ["case:read"] } }""");
    }

    [Fact]
    public async Task ClearedScopeOverrides_AreRemoved()
    {
        var capture = await RunAsync(r => r.SetMemberScopeOverridesAsync("T1", "u1", null));

        AssertSingle(capture, MemberFilter, """{ "$unset" : { "Members.$.ScopeOverrides" : 1 } }""");
    }

    [Fact]
    public async Task AName_IsTrimmed()
    {
        var capture = await RunAsync(r => r.SetMemberNameAsync("T1", "u1", "  Ann  "));

        AssertSingle(capture, MemberFilter, """{ "$set" : { "Members.$.Name" : "Ann" } }""");
    }

    [Fact]
    public async Task ABlankName_ClearsIt()
    {
        var capture = await RunAsync(r => r.SetMemberNameAsync("T1", "u1", "  "));

        AssertSingle(capture, MemberFilter, """{ "$set" : { "Members.$.Name" : null } }""");
    }

    /// <summary>Only the expiry: the code, and every link already mailed with it, is untouched.</summary>
    [Fact]
    public async Task AnInvitationExtension_SetsOnlyTheExpiry()
    {
        var capture = await RunAsync(r => r.SetInvitationExpiryAsync("T1", "inv1", Now));

        var expected = new BsonDocument("$set", new BsonDocument("Members.$.Invitation.ExpiresAt", Now));
        AssertSingle(capture, InvitationFilter, expected);
    }

    [Fact]
    public async Task AnAcceptedInvitation_MakesTheRowAMember()
    {
        var capture = await RunAsync(r => r.SetInvitationResponseAsync("T1", "u1", "inv1", true));

        var set = AssertInvitationResponse(capture);
        Assert.Equal("u1", set["Members.$.Key"]);
        Assert.Equal("Member", set["Members.$.State"]);
        Assert.Equal(BsonNull.Value, set["Members.$.Name"]);
        Assert.Equal(BsonNull.Value, set["Members.$.Invitation"]);
        Assert.Equal(5, set.ElementCount);
    }

    [Fact]
    public async Task ADeclinedInvitation_KeepsTheInvitation()
    {
        var capture = await RunAsync(r => r.SetInvitationResponseAsync("T1", "u1", "inv1", false));

        var set = AssertInvitationResponse(capture);
        Assert.Equal("u1", set["Members.$.Key"]);
        Assert.Equal("Rejected", set["Members.$.State"]);
        Assert.Equal(3, set.ElementCount);
    }

    [Fact]
    public async Task AnInvitationResponse_WithNoSuchInvitation_ReturnsNull()
    {
        var capture = await RunAsync(r => r.SetInvitationResponseAsync("T1", "u1", "inv1", true));

        Assert.Null(capture.Result);
    }

    /// <summary>Every row with the key, as the read-and-filter version dropped them all.</summary>
    [Fact]
    public async Task ARemoval_PullsThatMember()
    {
        var capture = await RunAsync(r => r.RemoveMemberAsync("T1", "u1"));

        AssertSingle(capture, """{ "Key" : "T1" }""", """{ "$pull" : { "Members" : { "Key" : "u1" } } }""");
    }

    [Fact]
    public async Task RemovingFromAllTeams_PullsThatMemberFromEachTeam()
    {
        var capture = await RunAsync(r => r.RemoveMemberFromAllTeamsAsync("u1"), "T1", "T2");

        Assert.Equal([BsonDocument.Parse("""{ "Key" : "T1" }"""), BsonDocument.Parse("""{ "Key" : "T2" }""")], capture.Filters);
        Assert.All(capture.Updates, update => Assert.Equal(BsonDocument.Parse("""{ "$pull" : { "Members" : { "Key" : "u1" } } }"""), update));
    }

    private static BsonDocument AssertInvitationResponse(Capture capture)
    {
        Assert.Equal(BsonDocument.Parse(InvitationFilter), Assert.Single(capture.Filters));
        var update = Assert.Single(capture.Updates);
        Assert.Equal(["$set"], update.Names);
        var set = update["$set"].AsBsonDocument;
        Assert.IsType<BsonDateTime>(set["Members.$.LastSeen"]);
        return set;
    }

    private static void AssertSingle(Capture capture, string filter, string update)
        => AssertSingle(capture, filter, BsonDocument.Parse(update));

    private static void AssertSingle(Capture capture, string filter, BsonDocument update)
    {
        Assert.Equal(BsonDocument.Parse(filter), Assert.Single(capture.Filters));
        Assert.Equal(update, Assert.Single(capture.Updates));
    }

    private sealed record Capture(
        ITeamRepositoryCollection<TestTeamEntity, TestMember> Collection,
        List<BsonDocument> Filters,
        List<BsonDocument> Updates,
        List<OneOption<TestTeamEntity>> Options)
    {
        public object Result { get; set; }
    }

    /// <param name="teamsWithTheMember">What the lookup in <c>RemoveMemberFromAllTeamsAsync</c> finds.</param>
    private static async Task<Capture> RunAsync(Func<TeamRepository<TestTeamEntity, TestMember>, Task> write, params string[] teamsWithTheMember)
    {
        var collection = Substitute.For<ITeamRepositoryCollection<TestTeamEntity, TestMember>>();
        var capture = new Capture(collection, [], [], []);
        _ = collection.UpdateOneAsync(
            Arg.Do<FilterDefinition<TestTeamEntity>>(f => capture.Filters.Add(f.Render(Args))),
            Arg.Do<UpdateDefinition<TestTeamEntity>>(u => capture.Updates.Add(u.Render(Args).AsBsonDocument)),
            Arg.Do<OneOption<TestTeamEntity>>(o => capture.Options.Add(o)),
            Arg.Any<IClientSessionHandle>());

        var teams = (teamsWithTheMember.Length == 0 ? ["T1"] : teamsWithTheMember)
            .Select(key => new TestTeamEntity { Key = key, Name = key, Members = [new TestMember { Key = "u1", State = MembershipState.Member }] })
            .ToArray();
        collection.GetAsync(Arg.Any<Expression<Func<TestTeamEntity, bool>>>()).Returns(teams.ToAsyncEnumerable());

        var repository = new TeamRepository<TestTeamEntity, TestMember>(collection);
        var task = write(repository);
        await task;
        if (task is Task<ITeam> withResult) capture.Result = await withResult;
        return capture;
    }
}
