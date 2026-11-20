using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>A conversation's rating is stored on its case by name, taken back by removing it, and an unrated case stores nothing for it.</summary>
public class SupportConversationRatingMappingTests
{
    private static readonly SupportConversationRating Rating = new()
    {
        Rating = SupportAnswerRating.Down,
        Comment = "It never looked at the retention rules.",
        RatedAt = new DateTime(2026, 11, 20, 14, 30, 0, DateTimeKind.Utc),
        RatedBy = "alice-subject"
    };

    private static SupportCaseEntity Case(SupportConversationRatingEntity rating = null) => new()
    {
        CaseId = "case-1",
        TeamKey = "team-a",
        AuthorIdentity = "alice-subject",
        AuthorName = "Alice",
        Subject = "How long are grades kept?",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc),
        Messages = [],
        ConversationRating = rating
    };

    private static BsonDocument Rendered(UpdateDefinition<SupportCaseEntity> update)
        => update.Render(new RenderArgs<SupportCaseEntity>(BsonSerializer.LookupSerializer<SupportCaseEntity>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;

    [Fact]
    public void ARating_IsSetOnTheCase_ByName()
    {
        var set = Rendered(MongoSupportCaseStore.RateConversation(Rating))["$set"]["ConversationRating"].AsBsonDocument;

        Assert.Equal("Down", set["Rating"].AsString);
        Assert.Equal(Rating.Comment, set["Comment"].AsString);
        Assert.Equal("alice-subject", set["RatedBy"].AsString);
    }

    [Fact]
    public void TakingTheRatingBack_RemovesIt()
    {
        var rendered = Rendered(MongoSupportCaseStore.RateConversation(null));

        Assert.True(rendered["$unset"].AsBsonDocument.Contains("ConversationRating"));
    }

    [Fact]
    public void ARating_RoundTrips()
    {
        var restored = MongoSupportCaseStore.ToCase(Case(MongoSupportCaseStore.ToEntity(Rating)));

        Assert.Equal(Rating, restored.ConversationRating);
    }

    [Fact]
    public void ARatingWithoutAComment_StoresNoComment()
    {
        var stored = MongoSupportCaseStore.ToEntity(Rating with { Comment = null }).ToBsonDocument();

        Assert.False(stored.Contains(nameof(SupportConversationRatingEntity.Comment)));
    }

    [Fact]
    public void AnUnratedCase_StoresNoRating_AndReadsUnrated()
    {
        var entity = Case();

        Assert.False(entity.ToBsonDocument().Contains(nameof(SupportCaseEntity.ConversationRating)));
        Assert.Null(MongoSupportCaseStore.ToCase(entity).ConversationRating);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhetherACallSucceeded_RoundTrips(bool succeeded)
    {
        var message = Answer(new SupportAssistantCall { Name = "read_manual", Succeeded = succeeded });

        Assert.Equal(succeeded, Assert.Single(MongoSupportCaseStore.ToMessage(MongoSupportCaseStore.ToEntity(message)).AssistantCalls).Succeeded);
    }

    [Fact]
    public void ACallNotKnownToHaveSucceeded_StoresNothingForIt()
    {
        var stored = MongoSupportCaseStore.ToEntity(Answer(new SupportAssistantCall { Name = "read_manual" }));

        Assert.False(stored.AssistantCalls.Single().ToBsonDocument().Contains(nameof(SupportAssistantCallEntity.Succeeded)));
        Assert.Null(Assert.Single(MongoSupportCaseStore.ToMessage(stored).AssistantCalls).Succeeded);
    }

    private static SupportMessage Answer(SupportAssistantCall call) => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.Assistant,
        AuthorName = "Assistant",
        Body = "Grades are kept for ten years.",
        SentAt = new DateTime(2026, 11, 20, 14, 1, 0, DateTimeKind.Utc),
        AssistantCalls = [call]
    };
}
