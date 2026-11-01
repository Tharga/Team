using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using global::MongoDB.Bson;
using Tharga.MongoDB;
using Tharga.MongoDB.Configuration;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

public class AuditRetentionIndexTests
{
    private static readonly TimeSpan Days427 = TimeSpan.FromDays(427);
    private static readonly TimeSpan Days90 = TimeSpan.FromDays(90);

    [Fact]
    public void NoIndex_AndNoRetention_DoesNothing()
    {
        Assert.Equal(AuditRetentionIndexAction.None, AuditRetentionIndex.Decide(new AuditRetentionIndexState(false, null), null));
    }

    [Fact]
    public void NoIndex_AndARetention_CreatesIt()
    {
        Assert.Equal(AuditRetentionIndexAction.Create, AuditRetentionIndex.Decide(new AuditRetentionIndexState(false, null), Days90));
    }

    [Fact]
    public void AnIndex_AndNoRetention_DropsIt_SoEntriesAreKeptForever()
    {
        Assert.Equal(AuditRetentionIndexAction.Drop, AuditRetentionIndex.Decide(new AuditRetentionIndexState(true, 7_776_000), null));
    }

    [Fact]
    public void AnIndexOf90Days_AndARetentionOf427_ChangesIt()
    {
        Assert.Equal(AuditRetentionIndexAction.Change, AuditRetentionIndex.Decide(new AuditRetentionIndexState(true, 7_776_000), Days427));
    }

    [Fact]
    public void AnIndexThatAlreadyMatches_IsLeftAlone()
    {
        Assert.Equal(AuditRetentionIndexAction.None, AuditRetentionIndex.Decide(new AuditRetentionIndexState(true, 7_776_000), Days90));
    }

    [Fact]
    public void AnIndexWithoutAnExpiry_IsChanged()
    {
        Assert.Equal(AuditRetentionIndexAction.Change, AuditRetentionIndex.Decide(new AuditRetentionIndexState(true, null), Days90));
    }

    [Fact]
    public void Find_ReadsTheExpiryOfTheTimestampIndex_WhateverNumberTypeTheServerAnswersWith()
    {
        var indexes = new[]
        {
            new BsonDocument { { "name", "_id_" } },
            new BsonDocument { { "name", "Feature" } },
            new BsonDocument { { "name", AuditRetentionIndex.Name }, { "expireAfterSeconds", 7_776_000 } }
        };
        var asDouble = new[] { new BsonDocument { { "name", AuditRetentionIndex.Name }, { "expireAfterSeconds", 7_776_000.0 } } };

        Assert.Equal(new AuditRetentionIndexState(true, 7_776_000), AuditRetentionIndex.Find(indexes));
        Assert.Equal(new AuditRetentionIndexState(true, 7_776_000), AuditRetentionIndex.Find(asDouble));
    }

    [Fact]
    public void Find_WithoutTheTimestampIndex_SaysItIsNotThere()
    {
        var indexes = new[] { new BsonDocument { { "name", "_id_" } }, new BsonDocument { { "name", "Feature" } } };

        Assert.Equal(new AuditRetentionIndexState(false, null), AuditRetentionIndex.Find(indexes));
    }

    [Fact]
    public void TheChangeCommand_IsCollModOnTheNamedIndex_InSeconds()
    {
        var command = AuditRetentionIndex.ChangeCommand(Days427);

        Assert.Equal("AuditLog", command["collMod"].AsString);
        Assert.Equal("Timestamp_TTL", command["index"]["name"].AsString);
        Assert.Equal(36_892_800L, command["index"]["expireAfterSeconds"].AsInt64);
    }

    [Fact]
    public void TheNamesTheIndexIsKnownBy_AreTheOnesTheCollectionDefines()
    {
        Assert.Equal("Timestamp_TTL", AuditRetentionIndex.Name);
        Assert.Equal("AuditLog", AuditRetentionIndex.CollectionName);
    }

    [Fact]
    public void MongoDbAuditStorage_RegistersTheRetentionIndexService()
    {
        var services = new ServiceCollection();
        services.AddThargaAuditLogging(o => o.StorageMode = AuditStorageMode.MongoDB);

        Assert.Contains(services, x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(AuditRetentionIndexService));
    }

    [Fact]
    public void LoggerOnlyAuditStorage_DoesNotRegisterIt()
    {
        var services = new ServiceCollection();
        services.AddThargaAuditLogging(o => o.StorageMode = AuditStorageMode.Logger);

        Assert.DoesNotContain(services, x => x.ImplementationType == typeof(AuditRetentionIndexService));
    }

    [Fact]
    public async Task WhenIndexAssuranceIsDisabled_TheIndexIsLeftToTheHost_AndTheDatabaseIsNotTouched()
    {
        var factory = Substitute.For<IMongoDbServiceFactory>();
        var service = Create(factory, new DatabaseOptions { AssureIndex = AssureIndexMode.Disabled });

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        Assert.Empty(factory.ReceivedCalls());
    }

    [Fact]
    public async Task WhenTheDatabaseCannotBeReached_TheServiceEndsQuietly_AndTheHostIsNotStopped()
    {
        var factory = Substitute.For<IMongoDbServiceFactory>();
        factory.GetMongoDbService(Arg.Any<Func<DatabaseContext>>()).Returns(_ => throw new InvalidOperationException("no database"));
        var service = Create(factory, new DatabaseOptions());

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public void TheHostsAssureIndexSetting_FromAddMongoDB_IsReadableThroughOptions()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddMongoDB(o =>
        {
            o.AutoRegisterRepositories = false;
            o.AutoRegisterCollections = false;
            o.AssureIndex = AssureIndexMode.Disabled;
        });

        using var provider = builder.Services.BuildServiceProvider();

        Assert.Equal(AssureIndexMode.Disabled, provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.AssureIndex);
    }

    private static AuditRetentionIndexService Create(IMongoDbServiceFactory factory, DatabaseOptions database)
        => new(factory, Options.Create(new AuditOptions()), NullLogger<AuditRetentionIndexService>.Instance, Options.Create(database));
}
