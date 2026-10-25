using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>A rating is set on its entry by position and stored by name, and an unrated entry stores nothing for it.</summary>
public class SupportAnswerRatingMappingTests
{
    private static SupportMessage Answer(SupportAnswerRating rating) => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.Assistant,
        AuthorName = "Assistant",
        Body = "Reindex the export.",
        SentAt = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc),
        Rating = rating
    };

    [Fact]
    public void ARating_IsSetOnItsEntry_ByName()
    {
        var rendered = MongoSupportCaseStore.RateAnswer(1, SupportAnswerRating.Down)
            .Render(new RenderArgs<SupportCaseEntity>(BsonSerializer.LookupSerializer<SupportCaseEntity>(), BsonSerializer.SerializerRegistry))
            .AsBsonDocument;

        Assert.Equal("Down", rendered["$set"]["Messages.1.Rating"].AsString);
    }

    [Fact]
    public void ARating_RoundTrips()
    {
        var stored = MongoSupportCaseStore.ToEntity(Answer(SupportAnswerRating.Up));

        Assert.Equal("Up", stored.ToBsonDocument()[nameof(SupportMessageEntity.Rating)].AsString);
        Assert.Equal(SupportAnswerRating.Up, MongoSupportCaseStore.ToMessage(stored).Rating);
    }

    [Fact]
    public void AnUnratedEntry_StoresNoRating()
    {
        Assert.False(MongoSupportCaseStore.ToEntity(Answer(SupportAnswerRating.None)).ToBsonDocument().Contains(nameof(SupportMessageEntity.Rating)));
    }
}
