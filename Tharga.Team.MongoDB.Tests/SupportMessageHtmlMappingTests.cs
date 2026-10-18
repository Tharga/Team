using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// A transcript entry keeps its formatting beside its text, a plain entry stores no formatting field, and an entry
/// written before formatting was kept reads as plain.
/// </summary>
public class SupportMessageHtmlMappingTests
{
    private static SupportMessage Message(string html) => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.User,
        Body = "Try this:\n\n- Restart",
        BodyHtml = html,
        SentAt = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void AnEntrysFormatting_SurvivesTheRoundTripToStorage()
    {
        const string html = "<p>Try this:</p><ul><li><b>Restart</b></li></ul>";

        var entity = MongoSupportCaseStore.ToEntity(Message(html));
        var back = MongoSupportCaseStore.ToMessage(BsonSerializer.Deserialize<SupportMessageEntity>(entity.ToBsonDocument()));

        Assert.Equal(html, back.BodyHtml);
        Assert.Equal("Try this:\n\n- Restart", back.Body);
    }

    [Fact]
    public void APlainEntry_StoresNoFormattingField()
    {
        var document = MongoSupportCaseStore.ToEntity(Message(null)).ToBsonDocument();

        Assert.False(document.Contains(nameof(SupportMessageEntity.BodyHtml)));
    }

    [Fact]
    public void AnEntryWrittenBeforeFormattingWasKept_ReadsAsPlain()
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

        Assert.Null(message.BodyHtml);
        Assert.Equal("Written long ago.", message.Body);
    }
}
