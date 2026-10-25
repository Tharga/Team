using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>What the assistant could not do is appended in one update that keeps only the newest, and read back as empty on an older case.</summary>
public class SupportCaseAssistantGapTests
{
    [Fact]
    public void Gaps_ArePushed_KeepingOnlyTheNewest()
    {
        var rendered = MongoSupportCaseStore.AddGaps(["search cases by subject"])
            .Render(new RenderArgs<SupportCaseEntity>(BsonSerializer.LookupSerializer<SupportCaseEntity>(), BsonSerializer.SerializerRegistry))
            .AsBsonDocument;

        var push = rendered["$push"][nameof(SupportCaseEntity.AssistantGaps)];
        Assert.Equal("search cases by subject", push["$each"][0].AsString);
        Assert.Equal(-SupportCaseLimits.MaxAssistantGaps, push["$slice"].AsInt32);
    }

    [Fact]
    public async Task NothingToAdd_WritesNothing()
    {
        var collection = Substitute.For<ISupportCaseRepositoryCollection>();

        await new MongoSupportCaseStore(collection).AddAssistantGapsAsync("acme", "case-1", [], TestContext.Current.CancellationToken);

        await collection.DidNotReceiveWithAnyArgs().UpdateOneAsync(default(FilterDefinition<SupportCaseEntity>), default(UpdateDefinition<SupportCaseEntity>));
    }
}
