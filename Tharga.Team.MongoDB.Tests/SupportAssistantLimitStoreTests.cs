using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NSubstitute;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>A team's own assistant limit in MongoDB: one document per team, the amount stored as a decimal, and no limit stored as no amount.</summary>
public class SupportAssistantLimitStoreTests
{
    private static StoredSupportAssistantLimit Limit(decimal? amount) => new()
    {
        TeamKey = "team-a",
        Limit = amount,
        ChangedAt = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc),
        ChangedByIdentity = "staff-1",
        ChangedByName = "Sam Staff"
    };

    [Fact]
    public void ALimit_ReadsBackAsItWasStored()
        => Assert.Equal(Limit(12.5m), MongoSupportAssistantLimitStore.ToRecord(MongoSupportAssistantLimitStore.ToEntity(Limit(12.5m))));

    [Fact]
    public void TheAmount_IsStoredAsDecimal128_NotADouble()
        => Assert.Equal(BsonType.Decimal128, MongoSupportAssistantLimitStore.ToEntity(Limit(12.5m)).ToBsonDocument()["Limit"].BsonType);

    /// <summary>The document is the override; a limit of none is a document with no amount, not a missing document.</summary>
    [Fact]
    public void ALimitOfNone_IsAnOverrideWithNoAmount()
    {
        var entity = MongoSupportAssistantLimitStore.ToEntity(Limit(null));

        Assert.False(entity.ToBsonDocument().Contains("Limit"));
        Assert.Null(MongoSupportAssistantLimitStore.ToRecord(entity).Limit);
    }

    [Fact]
    public async Task ATeamWithNoDocument_HasNoOwnLimit()
    {
        var store = new MongoSupportAssistantLimitStore(Substitute.For<ISupportAssistantLimitCollection>());

        Assert.Null(await store.GetAsync("team-a", TestContext.Current.CancellationToken));
        Assert.Null(await store.GetAsync(" ", TestContext.Current.CancellationToken));
        Assert.False(await store.DeleteForTeamAsync(null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void OneLimitPerTeam_IsWhatTheUniqueIndexKeeps()
    {
        var collection = (SupportAssistantLimitCollection)RuntimeHelpers.GetUninitializedObject(typeof(SupportAssistantLimitCollection));

        var index = Assert.Single(collection.Indices);
        var keys = index.Keys.Render(new RenderArgs<SupportAssistantLimitEntity>(BsonSerializer.LookupSerializer<SupportAssistantLimitEntity>(), BsonSerializer.SerializerRegistry));

        Assert.True(index.Options.Unique);
        Assert.Equal("TeamKey", index.Options.Name);
        Assert.Equal(["TeamKey"], keys.Names);
        Assert.Equal("SupportAssistantLimit", new ThargaTeamOptions().SupportAssistantLimitCollectionName);
    }
}
