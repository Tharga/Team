using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NSubstitute;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

public class TeamMailStoreTests
{
    private static readonly DateTime Received = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly IInboundMailCollection _collection = Substitute.For<IInboundMailCollection>();
    private FilterDefinition<InboundMailEntity> _sent;

    private static string Render(FilterDefinition<InboundMailEntity> filter) =>
        filter.Render(new RenderArgs<InboundMailEntity>(BsonSerializer.LookupSerializer<InboundMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    private static InboundMailItem Item(string teamKey = "team-a") => new()
    {
        Id = "mail-1",
        TeamKey = teamKey,
        ItemKey = "case42",
        Envelope = new MailEnvelope { MessageId = "<m1@example.org>", From = "anna@example.org", DeliveredTo = "key-case+case42@fortdocs.app", Subject = "Hej", To = ["key-case@fortdocs.app"] },
        Reason = MailRouteReason.RetiringKey,
        ReceivedAt = Received,
        KeyRetiresAt = Received.AddDays(30),
        Owner = new MailThreadOwner { Type = "case", Id = "42" },
        Outcome = "registered",
        HandledAt = Received.AddHours(1),
        HandledBy = "user-1"
    };

    [Fact]
    public void AMail_SurvivesTheRoundTripToStorage()
    {
        var item = Item();

        var back = MongoTeamMailStore.ToItem(MongoTeamMailStore.ToEntity(item));

        Assert.Equivalent(item, back);
    }

    [Fact]
    public async Task AMailAlreadyTakenIn_IsNotAddedAgain_SoAnotherInstanceReadingItIsNoError()
    {
        _collection.CountAsync(Arg.Do<FilterDefinition<InboundMailEntity>>(x => _sent = x)).Returns(1L);

        var added = await new MongoTeamMailStore(_collection).AddAsync(Item());

        Assert.False(added);
        Assert.Contains("\"MailId\" : \"mail-1\"", Render(_sent));
        await _collection.DidNotReceive().AddAsync(Arg.Any<InboundMailEntity>());
    }

    [Fact]
    public async Task ATeamsUnhandledCount_NamesTheTeam_AndCountsOnlyMailWithNoOutcome()
    {
        _collection.CountAsync(Arg.Do<FilterDefinition<InboundMailEntity>>(x => _sent = x)).Returns(2L);

        var count = await new MongoTeamMailStore(_collection).CountUnhandledAsync("team-a");

        var filter = Render(_sent);
        Assert.Equal(2, count);
        Assert.Contains("\"TeamKey\" : \"team-a\"", filter);
        Assert.Contains("\"Outcome\" : { \"$exists\" : false }", filter);
    }

    [Fact]
    public async Task ThePostmastersCount_IsMailWithNoTeam()
    {
        _collection.CountAsync(Arg.Do<FilterDefinition<InboundMailEntity>>(x => _sent = x)).Returns(1L);

        await new MongoTeamMailStore(_collection).CountUnroutedAsync();

        Assert.Contains("\"TeamKey\" : { \"$exists\" : false }", Render(_sent));
    }

    [Fact]
    public async Task Routing_MovesOnlyMailThePostmasterStillHolds()
    {
        _collection.UpdateOneAsync(Arg.Do<FilterDefinition<InboundMailEntity>>(x => _sent = x), Arg.Any<UpdateDefinition<InboundMailEntity>>())
            .Returns((EntityChangeResult<InboundMailEntity>)null);

        var routed = await new MongoTeamMailStore(_collection).RouteAsync("mail-1", "team-b");

        var filter = Render(_sent);
        Assert.Null(routed);
        Assert.Contains("\"TeamKey\" : { \"$exists\" : false }", filter);
        Assert.Contains("\"MailId\" : \"mail-1\"", filter);
    }

    [Fact]
    public async Task RecordingAnOutcome_NamesTheTeam_SoAnotherTeamsMailIsOutOfReach()
    {
        _collection.UpdateOneAsync(Arg.Do<FilterDefinition<InboundMailEntity>>(x => _sent = x), Arg.Any<UpdateDefinition<InboundMailEntity>>())
            .Returns((EntityChangeResult<InboundMailEntity>)null);

        Assert.Null(await new MongoTeamMailStore(_collection).SetOutcomeAsync("team-a", "mail-1", "registered", null, "user-1", Received));

        Assert.Contains("\"TeamKey\" : \"team-a\"", Render(_sent));
    }

    [Fact]
    public void ACursor_ContinuesAfterTheLastMailItNamed()
    {
        var cursor = MongoTeamMailStore.CursorOf(MongoTeamMailStore.ToEntity(Item()));

        var filter = Render(MongoTeamMailStore.After(cursor));

        Assert.Contains("$lt", filter);
        Assert.Contains("\"MailId\" : { \"$gt\" : \"mail-1\" }", filter);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData(":mail-1")]
    [InlineData("-5:mail-1")]
    public void ACursorTheMailboxDidNotIssue_IsRefused(string cursor)
    {
        Assert.Throws<ArgumentException>(() => MongoTeamMailStore.After(cursor));
    }

    [Fact]
    public void EachDeliveryIsKeptOnce_ForAsLongAsTheMailIs()
    {
        var mailId = ((InboundMailCollection)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(InboundMailCollection))).Indices.Single(x => x.Options.Name == "MailId");

        Assert.True(mailId.Options.Unique);
        Assert.Null(mailId.Options.ExpireAfter);
    }
}
