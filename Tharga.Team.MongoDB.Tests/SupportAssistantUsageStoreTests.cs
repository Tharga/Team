using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NSubstitute;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The assistant's usage in MongoDB: one document per time it was asked, totalled per team, provider and model by
/// the server, and removed with the team.
/// </summary>
public class SupportAssistantUsageStoreTests
{
    private static readonly DateTime From = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly ISupportAssistantUsageCollection _collection = Substitute.For<ISupportAssistantUsageCollection>();

    private MongoSupportAssistantUsageStore Store => new(_collection);

    private static SupportAssistantUsageRecord Record() => new()
    {
        TeamKey = "team-a",
        CaseId = "case-1",
        AskedBy = "alice-subject",
        Provider = "openai",
        Model = "gpt-4.1",
        InputTokens = 610,
        CachedInputTokens = 400,
        OutputTokens = 220,
        TotalTokens = 830,
        ToolCalls = 2,
        Answered = true,
        Duration = TimeSpan.FromSeconds(3.5),
        At = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc)
    };

    private static BsonDocument[] Stages(string teamKey)
        => [.. SupportAssistantUsageCollection.Totals(From, To, teamKey).Render(new RenderArgs<SupportAssistantUsageRecordEntity>(BsonSerializer.LookupSerializer<SupportAssistantUsageRecordEntity>(), BsonSerializer.SerializerRegistry)).Documents];

    [Fact]
    public async Task ARecord_IsWrittenAsItCame()
    {
        SupportAssistantUsageRecordEntity written = null;
        await _collection.AddAsync(Arg.Do<SupportAssistantUsageRecordEntity>(x => written = x));

        await Store.RecordAsync(Record(), TestContext.Current.CancellationToken);

        Assert.Equal(Record(), MongoSupportAssistantUsageStore.ToRecord(written));
    }

    [Fact]
    public void ThePeriod_IsFromInclusiveToExclusive_ForEveryTeam()
    {
        var match = Stages(null)[0]["$match"].AsBsonDocument;

        Assert.Equal(From, match["At"]["$gte"].ToUniversalTime());
        Assert.Equal(To, match["At"]["$lt"].ToUniversalTime());
        Assert.False(match.Contains("TeamKey"));
    }

    [Fact]
    public void OneTeam_IsMatchedToo()
    {
        Assert.Equal("team-a", Stages("team-a")[0]["$match"]["TeamKey"].AsString);
    }

    [Fact]
    public void Usage_IsGroupedByTeamProviderAndModel_AnswersAndDeclinesCountedApart()
    {
        var group = Stages(null)[1]["$group"].AsBsonDocument;

        Assert.Equal(new BsonDocument { { "t", "$TeamKey" }, { "p", "$Provider" }, { "m", "$Model" }, { "c", "$Currency" } }, group["_id"].AsBsonDocument);
        Assert.Equal(new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray { "$Answered", 1, 0 })), group["answers"].AsBsonDocument);
        Assert.Equal(new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray { "$Answered", 0, 1 })), group["declines"].AsBsonDocument);
        Assert.Equal(new BsonDocument("$sum", "$ToolCalls"), group["toolCalls"].AsBsonDocument);
        Assert.Equal(new BsonDocument("$sum", "$CachedInputTokens"), group["cached"].AsBsonDocument);
    }

    [Fact]
    public void TheCost_IsSummedByTheServer_AndCountsHowManyAnswersWerePriced()
    {
        var group = Stages(null)[1]["$group"].AsBsonDocument;

        Assert.Equal(new BsonDocument("$sum", "$Cost"), group["cost"].AsBsonDocument);
        Assert.Equal(new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray { new BsonDocument("$isNumber", "$Cost"), 1, 0 })), group["priced"].AsBsonDocument);
    }

    [Fact]
    public void APricedRow_CarriesItsCostAndCurrency()
    {
        var row = new BsonDocument
        {
            { "_id", new BsonDocument { { "t", "team-a" }, { "p", "openai" }, { "m", "gpt" }, { "c", "USD" } } },
            { "answers", 2 }, { "declines", 0 }, { "toolCalls", 0L }, { "input", 10L }, { "cached", 0L }, { "output", 5L }, { "total", 15L },
            { "cost", new BsonDecimal128(0.0125m) }, { "priced", 2 }
        };

        var total = SupportAssistantUsageCollection.ToTotal(row);

        Assert.Equal(0.0125m, total.Cost);
        Assert.Equal("USD", total.Currency);
    }

    [Fact]
    public void ARowWithNoPricedAnswer_HasNoCost_NotACostOfZero()
    {
        var row = new BsonDocument
        {
            { "_id", new BsonDocument { { "t", "team-a" }, { "p", BsonNull.Value }, { "m", BsonNull.Value }, { "c", BsonNull.Value } } },
            { "answers", 1 }, { "declines", 0 }, { "toolCalls", 0L }, { "input", 10L }, { "cached", 0L }, { "output", 5L }, { "total", 15L },
            { "cost", 0 }, { "priced", 0 }
        };

        var total = SupportAssistantUsageCollection.ToTotal(row);

        Assert.Null(total.Cost);
        Assert.Null(total.Currency);
    }

    [Fact]
    public void ARow_BecomesATotal()
    {
        var row = new BsonDocument
        {
            { "_id", new BsonDocument { { "t", "team-a" }, { "p", "openai" }, { "m", BsonNull.Value } } },
            { "answers", 3 }, { "declines", 1 }, { "toolCalls", 5L }, { "input", 1000L }, { "cached", 600L }, { "output", 200L }, { "total", 1200L }
        };

        var total = SupportAssistantUsageCollection.ToTotal(row);

        Assert.Equal(new SupportAssistantUsageTotal
        {
            TeamKey = "team-a", Provider = "openai", Model = null, Answers = 3, Declines = 1, ToolCalls = 5,
            InputTokens = 1000, CachedInputTokens = 600, OutputTokens = 200, TotalTokens = 1200
        }, total);
    }

    [Fact]
    public void CacheWrites_AreSummedByTheServer_AndReadBack()
    {
        var group = Stages(null)[1]["$group"].AsBsonDocument;
        Assert.Equal(new BsonDocument("$sum", "$CacheWriteTokens"), group["cacheWrite"].AsBsonDocument);

        var row = new BsonDocument
        {
            { "_id", new BsonDocument { { "t", "team-a" }, { "p", "anthropic" }, { "m", "m" }, { "c", "USD" } } },
            { "answers", 1 }, { "declines", 0 }, { "toolCalls", 0L }, { "input", 10L }, { "cached", 0L }, { "cacheWrite", 4L }, { "output", 5L }, { "total", 15L }
        };

        Assert.Equal(4, SupportAssistantUsageCollection.ToTotal(row).CacheWriteTokens);
    }

    [Fact]
    public void ARecordWithCacheWrites_KeepsThemAndThePrice_AndStoresThePriceAsDecimal128()
    {
        var record = Record() with { CacheWriteTokens = 120, PriceCacheWrite = 3.75m };

        var entity = MongoSupportAssistantUsageStore.ToEntity(record);

        Assert.Equal(record, MongoSupportAssistantUsageStore.ToRecord(entity));
        Assert.Equal(BsonType.Decimal128, entity.ToBsonDocument()["PriceCacheWrite"].BsonType);
    }

    [Fact]
    public void NoCacheWritePrice_IsNotStored()
        => Assert.False(MongoSupportAssistantUsageStore.ToEntity(Record()).ToBsonDocument().Contains("PriceCacheWrite"));

    [Fact]
    public async Task TheTotals_AreTheCollections()
    {
        SupportAssistantUsageTotal[] totals = [new() { TeamKey = "team-a", Answers = 1 }];
        _collection.TotalAsync(From, To, "team-a", Arg.Any<CancellationToken>()).Returns(totals);

        Assert.Equal(totals, await Store.GetTotalsAsync(From, To, "team-a", TestContext.Current.CancellationToken));
    }

    /// <summary>The store is registered, and is also the sink every record is handed to, whichever store a host keeps.</summary>
    [Fact]
    public void TheTeamRepository_RegistersTheStore_AsASinkToo()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository(o => o.RegisterTeamRepository<MongoSupportSenderDirectoryTests.TeamEntity, MongoSupportSenderDirectoryTests.Member>());

        Assert.Contains(services, x => x.ServiceType == typeof(ISupportAssistantUsageStore) && x.ImplementationType == typeof(MongoSupportAssistantUsageStore));
        Assert.Single(services, x => x.ServiceType == typeof(ISupportAssistantUsageSink) && x.ImplementationType == typeof(SupportAssistantUsageStoreSink));
    }

    [Fact]
    public async Task TheSink_HandsTheRecordToTheStore()
    {
        var store = Substitute.For<ISupportAssistantUsageStore>();

        await new SupportAssistantUsageStoreSink(store).RecordAsync(Record(), TestContext.Current.CancellationToken);

        await store.Received(1).RecordAsync(Record(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PurgingATeam_DeletesItsRecords()
    {
        _collection.DeleteManyAsync(Arg.Any<FilterDefinition<SupportAssistantUsageRecordEntity>>()).Returns(7L);

        Assert.Equal(7, await Store.DeleteForTeamAsync("team-a", TestContext.Current.CancellationToken));
    }
}
