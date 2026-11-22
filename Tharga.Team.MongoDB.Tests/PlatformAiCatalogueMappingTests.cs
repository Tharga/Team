using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The platform's AI catalogue is stored as one document and read back as it was written: the models and their prices, the default, and the keys still encrypted
/// (Tharga/Team#512).
/// </summary>
public class PlatformAiCatalogueMappingTests
{
    private static StoredPlatformAiCatalogue Catalogue() => new()
    {
        Providers =
        [
            new StoredPlatformAiProvider { Id = "p1", Name = "Anthropic EU", Kind = SupportAiConnectionKind.Anthropic, BaseAddress = "https://api.example.com", ProtectedApiKey = "CfDJ8-protected" },
            new StoredPlatformAiProvider { Id = "p2", Name = "Local", Kind = SupportAiConnectionKind.Ollama, BaseAddress = "http://localhost:11434" }
        ],
        Models =
        [
            new PlatformAiModel
            {
                Id = "m1", ProviderId = "p1", ModelId = "claude-x", DisplayName = "Claude X", ProcessedIn = "Sweden", StoredIn = "Sweden", AgreementUrl = "https://example.com/dpa",
                PriceInput = 3m, PriceCachedInput = 0.3m, PriceCacheWrite = 3.75m, PriceOutput = 15m, Currency = "USD", Offered = true
            },
            new PlatformAiModel { Id = "m2", ProviderId = "p2", ModelId = "llama", DisplayName = "Llama", Offered = false }
        ],
        DefaultModelId = "m1",
        ChangedAt = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc),
        ChangedByIdentity = "staff-1",
        ChangedByName = "Sam Staff"
    };

    [Fact]
    public void TheCatalogue_ReadsBackAsItWasStored()
    {
        var stored = Catalogue();

        var back = MongoPlatformAiCatalogueStore.ToRecord(MongoPlatformAiCatalogueStore.ToEntity(stored));

        Assert.Equal(stored.DefaultModelId, back.DefaultModelId);
        Assert.Equal(stored.Providers, back.Providers);
        Assert.Equal(stored.Models, back.Models);
        Assert.Equal(("staff-1", "Sam Staff"), (back.ChangedByIdentity, back.ChangedByName));
    }

    [Fact]
    public void ThePrices_KeepTheirPrecision_ThroughTheDocument()
    {
        var entity = MongoPlatformAiCatalogueStore.ToEntity(Catalogue());

        var document = entity.ToBsonDocument();
        var back = BsonSerializer.Deserialize<PlatformAiCatalogueEntity>(document);

        Assert.Equal(0.3m, back.Models[0].PriceCachedInput);
        Assert.Equal(3.75m, back.Models[0].PriceCacheWrite);
        Assert.Equal(BsonType.Decimal128, document["Models"][0]["PriceCacheWrite"].BsonType);
        Assert.Null(back.Models[1].PriceCacheWrite);
        Assert.Equal(15m, back.Models[0].PriceOutput);
        Assert.Null(back.Models[1].PriceInput);
    }

    [Fact]
    public void AKey_IsOnlyEverTheProtectedOne_AndAProviderWithNoneStoresNone()
    {
        var document = MongoPlatformAiCatalogueStore.ToEntity(Catalogue()).ToBsonDocument();

        var providers = document["Providers"].AsBsonArray;
        Assert.Equal("CfDJ8-protected", providers[0]["ProtectedApiKey"].AsString);
        Assert.False(providers[1].AsBsonDocument.Contains("ProtectedApiKey"));
    }

    [Fact]
    public void ThereIsOneCatalogue_AndItIsNamedSo()
        => Assert.Equal(PlatformAiCatalogueEntity.Singleton, MongoPlatformAiCatalogueStore.ToEntity(Catalogue()).Name);

    [Fact]
    public void ATeamsPlatformModel_SurvivesItsStoredConnection()
    {
        var entity = new SupportAiConnectionEntity { TeamKey = "t", PlatformModelId = "m1" };

        Assert.Equal("m1", entity.PlatformModelId);
        Assert.Equal("m1", BsonSerializer.Deserialize<SupportAiConnectionEntity>(entity.ToBsonDocument()).PlatformModelId);
    }
}
