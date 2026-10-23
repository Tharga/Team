using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The owner rules are part of the write (Tharga/Team#342): the filter that goes to the server carries the rule, so
/// a change landing between the caller's read and the write makes the write match nothing instead of producing the
/// state the rule forbids.
/// </summary>
public class TeamRepositoryOwnerGuardTests
{
    public record TestMember : TeamMemberBase;
    public record TestTeamEntity : TeamEntityBase<TestMember>;

    private static RenderArgs<TestTeamEntity> Args
        => new(BsonSerializer.LookupSerializer<TestTeamEntity>(), BsonSerializer.SerializerRegistry);

    private const string NotOwner = """{ "Key" : "T1", "Members" : { "$elemMatch" : { "Key" : "u1", "AccessLevel" : { "$ne" : "Owner" } } } }""";

    [Fact]
    public async Task ADemotion_OnlyMatchesAMemberWhoIsNotTheOwner()
    {
        var (filters, updates, options) = await RunAsync(r => r.SetMemberRoleUnlessOwnerAsync("T1", "u1", AccessLevel.User));

        Assert.Equal(BsonDocument.Parse(NotOwner), Assert.Single(filters));
        Assert.Equal(BsonDocument.Parse("""{ "$set" : { "Members.$.AccessLevel" : "User" } }"""), Assert.Single(updates));
        Assert.Equal(EMode.FirstOrDefault, Assert.Single(options)?.Mode);
    }

    [Fact]
    public async Task ARemoval_OnlyMatchesAMemberWhoIsNotTheOwner()
    {
        var (filters, updates, options) = await RunAsync(r => r.RemoveMemberGuardedAsync("T1", "u1", false));

        Assert.Equal(BsonDocument.Parse(NotOwner), Assert.Single(filters));
        Assert.Equal(BsonDocument.Parse("""{ "$pull" : { "Members" : { "Key" : "u1" } } }"""), Assert.Single(updates));
        Assert.Equal(EMode.FirstOrDefault, Assert.Single(options)?.Mode);
    }

    /// <summary>
    /// The same members <c>TeamServiceBase</c> counts: accepted, not suspended, Owner or Administrator. Listed rather
    /// than compared, because the level is stored by name and <c>$lte</c> would order the names alphabetically.
    /// </summary>
    [Fact]
    public async Task ALastAdministratorsRemoval_AlsoRequiresAnotherActiveAdministratorOrOwner()
    {
        var (filters, _, _) = await RunAsync(r => r.RemoveMemberGuardedAsync("T1", "u1", true));

        var expected = BsonDocument.Parse("""
            { "$and" : [
                { "Key" : "T1" },
                { "Members" : { "$elemMatch" : { "Key" : "u1", "AccessLevel" : { "$ne" : "Owner" } } } },
                { "Members" : { "$elemMatch" : { "Key" : { "$ne" : "u1" }, "State" : "Member", "SuspendedAt" : null, "AccessLevel" : { "$in" : ["Owner", "Administrator"] } } } }
            ] }
            """);
        var rendered = Assert.Single(filters);
        Assert.Equal(expected.ToJson(), rendered.ToJson());
    }

    [Fact]
    public async Task AGuardedWrite_ThatMatchesNothing_ReportsRefused()
    {
        var demoted = await RunAsync(r => r.SetMemberRoleUnlessOwnerAsync("T1", "u1", AccessLevel.User));
        var removed = await RunAsync(r => r.RemoveMemberGuardedAsync("T1", "u1", true));

        Assert.False(demoted.Result);
        Assert.False(removed.Result);
    }

    [Fact]
    public void ATransfer_MatchesOnlyWhileTheCurrentOwnerIsStillTheOwner_AndTheNewOwnerIsAMember()
    {
        var filter = TeamRepositoryCollection<TestTeamEntity, TestMember>.TransferFilter("T1", "u1", "u2").Render(Args);

        var expected = BsonDocument.Parse("""
            { "$and" : [
                { "Key" : "T1" },
                { "Members" : { "$elemMatch" : { "Key" : "u1", "AccessLevel" : "Owner" } } },
                { "Members" : { "$elemMatch" : { "Key" : "u2" } } }
            ] }
            """);
        Assert.Equal(expected.ToJson(), filter.AsBsonDocument.ToJson());
    }

    /// <summary>One update, so the team is never seen with two owners or with none.</summary>
    [Fact]
    public void ATransfer_PromotesAndDemotesInTheSameUpdate_EachOnItsOwnMember()
    {
        var update = TeamRepositoryCollection<TestTeamEntity, TestMember>.TransferUpdate().Render(Args).AsBsonDocument;
        var filters = TeamRepositoryCollection<TestTeamEntity, TestMember>.TransferArrayFilters("u1", "u2")
            .Select(f => f.Render(BsonSerializer.LookupSerializer<BsonDocument>(), BsonSerializer.SerializerRegistry))
            .ToArray();

        Assert.Equal(
            BsonDocument.Parse("""{ "$set" : { "Members.$[outgoing].AccessLevel" : "Administrator", "Members.$[incoming].AccessLevel" : "Owner" } }"""),
            update);
        Assert.Equal(BsonDocument.Parse("""{ "outgoing.Key" : "u1" }"""), filters[0]);
        Assert.Equal(BsonDocument.Parse("""{ "incoming.Key" : "u2" }"""), filters[1]);
    }

    private sealed record Run(List<BsonDocument> Filters, List<BsonDocument> Updates, List<OneOption<TestTeamEntity>> Options, bool Result)
    {
        public void Deconstruct(out List<BsonDocument> filters, out List<BsonDocument> updates, out List<OneOption<TestTeamEntity>> options)
            => (filters, updates, options) = (Filters, Updates, Options);
    }

    private static async Task<Run> RunAsync(Func<TeamRepository<TestTeamEntity, TestMember>, Task<bool>> write)
    {
        var collection = Substitute.For<ITeamRepositoryCollection<TestTeamEntity, TestMember>>();
        var filters = new List<BsonDocument>();
        var updates = new List<BsonDocument>();
        var options = new List<OneOption<TestTeamEntity>>();
        _ = collection.UpdateOneAsync(
            Arg.Do<FilterDefinition<TestTeamEntity>>(f => filters.Add(f.Render(Args))),
            Arg.Do<UpdateDefinition<TestTeamEntity>>(u => updates.Add(u.Render(Args).AsBsonDocument)),
            Arg.Do<OneOption<TestTeamEntity>>(o => options.Add(o)),
            Arg.Any<IClientSessionHandle>());

        var result = await write(new TeamRepository<TestTeamEntity, TestMember>(collection));
        return new Run(filters, updates, options, result);
    }
}
