using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NSubstitute;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The support mailbox a case came in through is stored on its mail binding, and a binding stored before mailboxes
/// were named reads back as the primary's.
/// </summary>
public class SupportBindingMailboxMappingTests
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
        Messages = [],
        Bindings = bindings
    };

    private BsonDocument RenderedUpdate() =>
        _sentUpdate.Render(new RenderArgs<SupportCaseEntity>(
            BsonSerializer.LookupSerializer<SupportCaseEntity>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;

    [Fact]
    public async Task ABindingsMailbox_IsWrittenAndReadBack()
    {
        var store = Store(Case(new SupportChannelBindingEntity { ChannelType = SupportChannelType.Email, ExternalId = "<m1@example.org>", Address = "anna@example.org", Mailbox = "hosted", MailboxAddress = "help@example.net" }));

        await store.AddBindingAsync("acme", "case-1", new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = "<m2@example.org>", Mailbox = "hosted", MailboxAddress = "help@example.net" }, TestContext.Current.CancellationToken);
        var pushed = BsonSerializer.Deserialize<SupportChannelBindingEntity>(RenderedUpdate()["$push"]["Bindings"].AsBsonDocument);
        var read = Assert.Single((await store.GetCaseAsync("acme", "case-1", TestContext.Current.CancellationToken)).Bindings);

        Assert.Equal(("hosted", "help@example.net"), (pushed.Mailbox, pushed.MailboxAddress));
        Assert.Equal(("hosted", "help@example.net"), (read.Mailbox, read.MailboxAddress));
    }

    [Fact]
    public async Task ABindingStoredBeforeMailboxesWereNamed_ReadsAsThePrimarys()
    {
        var old = BsonSerializer.Deserialize<SupportChannelBindingEntity>(new BsonDocument { ["ChannelType"] = "Email", ["ExternalId"] = "<m1@example.org>", ["Address"] = "anna@example.org" });

        var read = Assert.Single((await Store(Case(old)).GetCaseAsync("acme", "case-1", TestContext.Current.CancellationToken)).Bindings);

        Assert.Null(read.Mailbox);
        Assert.Null(read.MailboxAddress);
    }

    [Fact]
    public void ABindingWithNoMailbox_StoresNone()
    {
        var document = new SupportChannelBindingEntity { ChannelType = SupportChannelType.Email, ExternalId = "<m1@example.org>" }.ToBsonDocument();

        Assert.False(document.Contains(nameof(SupportChannelBindingEntity.Mailbox)));
        Assert.False(document.Contains(nameof(SupportChannelBindingEntity.MailboxAddress)));
    }
}
