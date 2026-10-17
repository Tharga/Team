using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Stamping a member's Last Seen writes that one field and nothing else (Tharga/Team#317). It became the most
/// frequent write to a team once it followed activity; replacing the whole document then could undo an
/// administrator's concurrent change to the team.
/// </summary>
public class TeamRepositoryLastSeenTests
{
    public record TestMember : TeamMemberBase;
    public record TestTeamEntity : TeamEntityBase<TestMember>;

    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static RenderArgs<TestTeamEntity> Args
        => new(BsonSerializer.LookupSerializer<TestTeamEntity>(), BsonSerializer.SerializerRegistry);

    [Fact]
    public async Task TheStamp_SetsOnlyThatMembersLastSeen()
    {
        var (_, update, _) = await StampAsync();

        var expected = new BsonDocument("$set", new BsonDocument("Members.$.LastSeen", Now));
        Assert.Equal(expected, update);
    }

    [Fact]
    public async Task TheStamp_TargetsTheMemberOnALiveTeam()
    {
        var (filter, _, _) = await StampAsync();

        var expected = BsonDocument.Parse(
            """{ "Key" : "T1", "DeletedAt" : null, "Members" : { "$elemMatch" : { "Key" : "u1" } } }""");
        Assert.Equal(expected, filter);
    }

    /// <summary>No read-modify-write: the team is neither read nor replaced.</summary>
    [Fact]
    public async Task TheStamp_NeverReplacesTheTeam()
    {
        var (_, _, collection) = await StampAsync();

        await collection.DidNotReceiveWithAnyArgs().ReplaceOneAsync(default);
        await collection.DidNotReceiveWithAnyArgs().GetOneAsync(default(FilterDefinition<TestTeamEntity>));
    }

    private static async Task<(BsonDocument Filter, BsonDocument Update, ITeamRepositoryCollection<TestTeamEntity, TestMember> Collection)> StampAsync()
    {
        FilterDefinition<TestTeamEntity> filter = null;
        UpdateDefinition<TestTeamEntity> update = null;
        var collection = Substitute.For<ITeamRepositoryCollection<TestTeamEntity, TestMember>>();
        _ = collection.UpdateOneAsync(Arg.Do<FilterDefinition<TestTeamEntity>>(f => filter = f), Arg.Do<UpdateDefinition<TestTeamEntity>>(u => update = u));

        await new TeamRepository<TestTeamEntity, TestMember>(collection).SetLastSeenAsync("T1", "u1", Now);

        Assert.NotNull(filter);
        Assert.NotNull(update);
        return (filter.Render(Args), update.Render(Args).AsBsonDocument, collection);
    }
}
