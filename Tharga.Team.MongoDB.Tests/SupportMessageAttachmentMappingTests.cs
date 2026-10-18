using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// A transcript entry keeps its files' metadata — never their content — and an entry written before files
/// were kept reads as carrying none.
/// </summary>
public class SupportMessageAttachmentMappingTests
{
    private static SupportMessage Message(params SupportAttachment[] attachments) => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.User,
        Body = "Attached.",
        SentAt = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc),
        Attachments = attachments
    };

    [Fact]
    public void AnEntrysFiles_SurviveTheRoundTripToStorage()
    {
        var attachment = new SupportAttachment { Id = "a1", FileName = "logo.png", ContentType = "image/png", Size = 42, Inline = true, ContentId = "logo@example.com" };

        var entity = MongoSupportCaseStore.ToEntity(Message(attachment));
        var back = MongoSupportCaseStore.ToMessage(BsonSerializer.Deserialize<SupportMessageEntity>(entity.ToBsonDocument()));

        Assert.Equal(attachment, Assert.Single(back.Attachments));
    }

    [Fact]
    public void AnEntryWithoutFiles_StoresNoFileField()
    {
        var document = MongoSupportCaseStore.ToEntity(Message()).ToBsonDocument();

        Assert.False(document.Contains(nameof(SupportMessageEntity.Attachments)));
    }

    [Fact]
    public void AnEntryWrittenBeforeFilesWereKept_ReadsAsCarryingNone()
    {
        var old = new BsonDocument
        {
            { "Sequence", 1 },
            { "Kind", "User" },
            { "Body", "Written long ago." },
            { "SentAt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            { "Delivery", "Sent" }
        };

        var message = MongoSupportCaseStore.ToMessage(BsonSerializer.Deserialize<SupportMessageEntity>(old));

        Assert.NotNull(message.Attachments);
        Assert.Empty(message.Attachments);
    }

    [Fact]
    public void AFilesFlagAndContentId_AreOmittedWhenNotSet()
    {
        var document = MongoSupportCaseStore.ToEntity(Message(new SupportAttachment { Id = "a1", FileName = "a.pdf", ContentType = "application/pdf", Size = 1 })).ToBsonDocument();

        var stored = document[nameof(SupportMessageEntity.Attachments)].AsBsonArray.Single().AsBsonDocument;
        Assert.False(stored.Contains(nameof(SupportAttachmentEntity.Inline)));
        Assert.False(stored.Contains(nameof(SupportAttachmentEntity.ContentId)));
        Assert.Equal(BsonType.Int64, stored[nameof(SupportAttachmentEntity.Size)].BsonType);
    }
}
