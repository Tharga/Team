using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NSubstitute;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

public class TeamMailKeyStoreTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static TeamMailKeyEntry Key(string key, MailKeyRole role, DateTime? retiresAt = null) =>
        new() { Key = key, Role = role, CreatedAt = Now.AddDays(-10), RetiresAt = retiresAt };

    [Fact]
    public void Rotating_DemotesThePrimary_RetiresTheSecondaryNow_AndAddsTheNewPrimary()
    {
        var keys = new[] { Key("primary", MailKeyRole.Primary), Key("secondary", MailKeyRole.Secondary, Now.AddDays(30)) };

        var rotated = TeamMailKeyRotation.Rotate(keys, Key("fresh", MailKeyRole.Primary), Now.AddDays(90), Now);

        Assert.Equal(MailKeyRole.Secondary, rotated.Single(x => x.Key == "primary").Role);
        Assert.Equal(Now.AddDays(90), rotated.Single(x => x.Key == "primary").RetiresAt);
        Assert.Equal(Now, rotated.Single(x => x.Key == "secondary").RetiresAt);
        Assert.Equal(MailKeyRole.Primary, rotated.Single(x => x.Key == "fresh").Role);
        Assert.Single(rotated, x => x.Role == MailKeyRole.Primary);
    }

    [Fact]
    public void Rotating_KeepsRetiredKeysAsTheyWere()
    {
        var retired = Key("retired", MailKeyRole.Secondary, Now.AddDays(-5));

        var rotated = TeamMailKeyRotation.Rotate([Key("primary", MailKeyRole.Primary), retired], Key("fresh", MailKeyRole.Primary), Now.AddDays(90), Now);

        Assert.Equal(retired, rotated.Single(x => x.Key == "retired"));
        Assert.Equal(3, rotated.Length);
    }

    [Fact]
    public async Task ARotation_IsConditionalOnTheRevisionItRead()
    {
        var collection = Substitute.For<ITeamMailKeysCollection>();
        collection.GetOneAsync(Arg.Any<System.Linq.Expressions.Expression<Func<TeamMailKeysEntity, bool>>>())
            .Returns(new TeamMailKeysEntity { TeamKey = "team-a", Keys = [Key("primary", MailKeyRole.Primary)], Revision = 4 });
        FilterDefinition<TeamMailKeysEntity> sent = null;
        collection.UpdateOneAsync(Arg.Do<FilterDefinition<TeamMailKeysEntity>>(x => sent = x), Arg.Any<UpdateDefinition<TeamMailKeysEntity>>())
            .Returns((EntityChangeResult<TeamMailKeysEntity>)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new MongoTeamMailKeyStore(collection).RotateAsync("team-a", new TeamMailKeyRecord { TeamKey = "team-a", Key = "fresh", Role = MailKeyRole.Primary, CreatedAt = Now }, Now.AddDays(90), Now));

        var filter = sent.Render(new RenderArgs<TeamMailKeysEntity>(BsonSerializer.LookupSerializer<TeamMailKeysEntity>(), BsonSerializer.SerializerRegistry)).ToString();
        Assert.Contains("\"TeamKey\" : \"team-a\"", filter);
        Assert.Contains("\"Revision\" : 4", filter);
    }

    [Fact]
    public async Task ARotationThatLostARace_ChangesNothing_AndSaysSo()
    {
        var collection = Substitute.For<ITeamMailKeysCollection>();
        collection.GetOneAsync(Arg.Any<System.Linq.Expressions.Expression<Func<TeamMailKeysEntity, bool>>>())
            .Returns(new TeamMailKeysEntity { TeamKey = "team-a", Keys = [Key("primary", MailKeyRole.Primary)], Revision = 4 });
        collection.UpdateOneAsync(Arg.Any<FilterDefinition<TeamMailKeysEntity>>(), Arg.Any<UpdateDefinition<TeamMailKeysEntity>>())
            .Returns((EntityChangeResult<TeamMailKeysEntity>)null);

        var store = new MongoTeamMailKeyStore(collection);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RotateAsync("team-a", new TeamMailKeyRecord { TeamKey = "team-a", Key = "fresh", Role = MailKeyRole.Primary, CreatedAt = Now }, Now.AddDays(90), Now));
    }

    [Fact]
    public void NoKeyIsEverHeldByTwoTeams_AndATeamHasOneDocument()
    {
        var indices = ((TeamMailKeysCollection)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(TeamMailKeysCollection))).Indices.ToArray();

        Assert.True(indices.Single(x => x.Options.Name == "Keys_Key").Options.Unique);
        Assert.True(indices.Single(x => x.Options.Name == "TeamKey").Options.Unique);
    }
}
