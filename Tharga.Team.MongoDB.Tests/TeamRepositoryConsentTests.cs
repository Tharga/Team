using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Setting a team's consent directly makes it standing: a time-bound window from an approved access request ends.
/// </summary>
public class TeamRepositoryConsentTests
{
    public record TestMember : TeamMemberBase;
    public record TestTeamEntity : TeamEntityBase<TestMember>;

    [Fact]
    public async Task SetConsent_ClearsAnyTemporaryConsent()
    {
        var update = await CapturedUpdateAsync(repository => repository.SetConsentAsync("T1", ["Developer"], AccessLevel.Viewer));

        var unset = update["$unset"].AsBsonDocument;
        Assert.True(unset.Contains(nameof(TeamEntityBase<TestMember>.TemporaryConsent)));
    }

    [Fact]
    public async Task SetConsent_StillSetsTheRolesAndLevel()
    {
        var update = await CapturedUpdateAsync(repository => repository.SetConsentAsync("T1", ["Developer"], AccessLevel.Viewer));

        var set = update["$set"].AsBsonDocument;
        Assert.Equal("Viewer", set[nameof(TeamEntityBase<TestMember>.ConsentAccessLevel)].AsString);
        Assert.Equal("Developer", set[nameof(TeamEntityBase<TestMember>.ConsentedRoles)].AsBsonArray[0].AsString);
    }

    private static async Task<BsonDocument> CapturedUpdateAsync(Func<TeamRepository<TestTeamEntity, TestMember>, Task> act)
    {
        UpdateDefinition<TestTeamEntity> captured = null;
        var collection = Substitute.For<ITeamRepositoryCollection<TestTeamEntity, TestMember>>();
        collection.UpdateOneAsync(Arg.Any<FilterDefinition<TestTeamEntity>>(), Arg.Do<UpdateDefinition<TestTeamEntity>>(u => captured = u));

        await act(new TeamRepository<TestTeamEntity, TestMember>(collection));

        Assert.NotNull(captured);
        var args = new RenderArgs<TestTeamEntity>(BsonSerializer.LookupSerializer<TestTeamEntity>(), BsonSerializer.SerializerRegistry);
        return captured.Render(args).AsBsonDocument;
    }
}
