using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NSubstitute;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Why an entry was not delivered is stored with a failure and cleared by anything else; the id an answer was
/// mailed under keeps the entry it carried.
/// </summary>
public class SupportDeliveryErrorTests
{
    private readonly ISupportCaseRepositoryCollection _collection = Substitute.For<ISupportCaseRepositoryCollection>();
    private UpdateDefinition<SupportCaseEntity> _sentUpdate;

    private MongoSupportCaseStore Store(SupportCaseEntity stored)
    {
        _collection.GetOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>()).Returns(stored);
        _collection.UpdateOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>(), Arg.Do<UpdateDefinition<SupportCaseEntity>>(x => _sentUpdate = x));

        return new MongoSupportCaseStore(_collection);
    }

    private static SupportCaseEntity Case(params SupportChannelBindingEntity[] bindings) => new()
    {
        CaseId = "case-1",
        TeamKey = "acme",
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Subject = "Subject",
        Status = SupportCaseStatus.Open,
        CreatedAt = DateTime.UtcNow,
        Messages =
        [
            new SupportMessageEntity { Sequence = 1, Kind = SupportMessageKind.User, Body = "Help.", SentAt = DateTime.UtcNow },
            new SupportMessageEntity { Sequence = 2, Kind = SupportMessageKind.User, Body = "Answer.", SentAt = DateTime.UtcNow, Delivery = SupportMessageDelivery.Failed, DeliveryError = "Mailbox full", AsSupport = true }
        ],
        Bindings = bindings
    };

    private BsonDocument RenderedUpdate() =>
        _sentUpdate.Render(new RenderArgs<SupportCaseEntity>(
            BsonSerializer.LookupSerializer<SupportCaseEntity>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;

    [Fact]
    public async Task AFailure_StoresItsReason()
    {
        await Store(Case()).SetMessageDeliveryAsync("acme", "case-1", 2, SupportMessageDelivery.Failed, "554 5.7.23 SPF validation failed", TestContext.Current.CancellationToken);

        var set = RenderedUpdate()["$set"].AsBsonDocument;
        Assert.Equal("Failed", set["Messages.1.Delivery"].AsString);
        Assert.Equal("554 5.7.23 SPF validation failed", set["Messages.1.DeliveryError"].AsString);
    }

    [Fact]
    public async Task AnythingButAFailure_ClearsTheReason()
    {
        await Store(Case()).SetMessageDeliveryAsync("acme", "case-1", 2, SupportMessageDelivery.Sent, "ignored", TestContext.Current.CancellationToken);

        var update = RenderedUpdate();
        Assert.Equal("Sent", update["$set"]["Messages.1.Delivery"].AsString);
        Assert.False(update["$set"].AsBsonDocument.Contains("Messages.1.DeliveryError"));
        Assert.True(update["$unset"].AsBsonDocument.Contains("Messages.1.DeliveryError"));
    }

    [Fact]
    public async Task TheReason_IsReadBack()
    {
        var page = await Store(Case()).GetMessagesAsync("acme", "case-1", null, 50, TestContext.Current.CancellationToken);

        Assert.Equal("Mailbox full", page.Items[^1].DeliveryError);
        Assert.Null(page.Items[0].DeliveryError);
    }

    [Fact]
    public async Task ABindingsMessageSequence_IsWrittenAndReadBack()
    {

        var store = Store(Case(new SupportChannelBindingEntity { ChannelType = SupportChannelType.Email, ExternalId = "answer-2@example.net", MessageSequence = 2 }));

        await store.AddBindingAsync("acme", "case-1", new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = "answer-3@example.net", MessageSequence = 3 }, TestContext.Current.CancellationToken);
        var pushed = BsonSerializer.Deserialize<SupportChannelBindingEntity>(RenderedUpdate()["$push"]["Bindings"].AsBsonDocument);

        var read = await store.GetCaseAsync("acme", "case-1", TestContext.Current.CancellationToken);

        Assert.Equal(3, pushed.MessageSequence);
        Assert.Equal(2, Assert.Single(read.Bindings).MessageSequence);
    }

    [Fact]
    public void AnEntryWithNoReason_StoresNone()
    {
        var document = new SupportMessageEntity { Sequence = 1, Kind = SupportMessageKind.User, Body = "x", SentAt = DateTime.UtcNow }.ToBsonDocument();

        Assert.False(document.Contains(nameof(SupportMessageEntity.DeliveryError)));
    }
}
