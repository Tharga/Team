using System.Linq.Expressions;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Each team's AI connection in MongoDB, one document per team; and the Data Protection key ring kept beside it so
/// every instance can read the keys that protect it.
/// </summary>
public class SupportAiConnectionStoreTests
{
    private static readonly DateTime At = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISupportAiConnectionCollection _collection = Substitute.For<ISupportAiConnectionCollection>();

    private MongoSupportAiConnectionStore Store => new(_collection);

    private static StoredSupportAiConnection Connection(string teamKey = "team-a") => new()
    {
        TeamKey = teamKey,
        Kind = SupportAiConnectionKind.Neurolito,
        BaseAddress = "https://neurolito.com",
        Model = "claude-qwen3-coder:30b",
        ProtectedApiKey = "CfDJ8-protected",
        ChangedAt = At,
        ChangedByIdentity = "admin-subject",
        ChangedByName = "Ada Admin"
    };

    [Fact]
    public async Task AStoredConnection_IsReadBackAsItWasWritten()
    {
        var connection = Connection();
        _collection.GetOneAsync(Arg.Any<Expression<Func<SupportAiConnectionEntity, bool>>>()).Returns(Entity(connection));

        Assert.Equal(connection, await Store.GetAsync("team-a", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ATeamThatNeverChose_HasNoRecord()
    {
        Assert.Null(await Store.GetAsync("team-a", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoTeam_IsNotLookedUp()
    {
        Assert.Null(await Store.GetAsync(" ", TestContext.Current.CancellationToken));
        await _collection.DidNotReceive().GetOneAsync(Arg.Any<Expression<Func<SupportAiConnectionEntity, bool>>>());
    }

    [Fact]
    public async Task ChangingAnExistingConnection_UpdatesIt_AndAddsNothing()
    {
        _collection.UpdateOneAsync(Arg.Any<FilterDefinition<SupportAiConnectionEntity>>(), Arg.Any<UpdateDefinition<SupportAiConnectionEntity>>())
            .Returns(Changed(Entity(Connection())));

        await Store.SetAsync(Connection(), TestContext.Current.CancellationToken);

        await _collection.DidNotReceive().AddAsync(Arg.Any<SupportAiConnectionEntity>());
    }

    [Fact]
    public async Task AFirstConnection_IsAdded_WithEveryField()
    {
        SupportAiConnectionEntity added = null;
        await _collection.AddAsync(Arg.Do<SupportAiConnectionEntity>(x => added = x));

        await Store.SetAsync(Connection(), TestContext.Current.CancellationToken);

        Assert.NotNull(added);
        Assert.Equal(Connection(), ToRecord(added));
    }

    [Fact]
    public async Task Deleting_RemovesTheTeamsDocument_AndSaysWhetherThereWasOne()
    {
        var entity = Entity(Connection());
        _collection.GetOneAsync(Arg.Any<Expression<Func<SupportAiConnectionEntity, bool>>>()).Returns(entity, (SupportAiConnectionEntity)null);

        Assert.True(await Store.DeleteForTeamAsync("team-a", TestContext.Current.CancellationToken));
        await _collection.Received(1).DeleteOneAsync(entity.Id);
        Assert.False(await Store.DeleteForTeamAsync("team-a", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ATeamHasOneDocument()
    {
        var indices = ((SupportAiConnectionCollection)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SupportAiConnectionCollection))).Indices.ToArray();

        Assert.True(indices.Single(x => x.Options.Name == "TeamKey").Options.Unique);
    }

    /// <summary>By name, so reordering the enum cannot turn one team's choice into another.</summary>
    [Fact]
    public void TheKind_IsStoredByName()
    {
        var document = Entity(Connection()).ToBsonDocument();

        Assert.Equal("Neurolito", document["Kind"].AsString);
    }

    [Fact]
    public void TheKeyRing_StoresEachKeyAsItsXml()
    {
        var keys = Substitute.For<IDataProtectionKeyCollection>();
        DataProtectionKeyEntity added = null;
        keys.AddAsync(Arg.Do<DataProtectionKeyEntity>(x => added = x));

        Repository(keys).StoreElement(new XElement("key", new XAttribute("id", "k-1")), "key-k-1");

        Assert.Equal("key-k-1", added.FriendlyName);
        Assert.Equal("<key id=\"k-1\" />", added.Xml);
    }

    [Fact]
    public void TheKeyRing_ReadsBackEveryKey()
    {
        var keys = Substitute.For<IDataProtectionKeyCollection>();
        keys.GetAsync(Arg.Any<Expression<Func<DataProtectionKeyEntity, bool>>>(), Arg.Any<Options<DataProtectionKeyEntity>>(), Arg.Any<CancellationToken>())
            .Returns(Read(new DataProtectionKeyEntity { FriendlyName = "a", Xml = "<key id=\"a\" />" }, new DataProtectionKeyEntity { FriendlyName = "b", Xml = "<key id=\"b\" />" }));

        var elements = Repository(keys).GetAllElements();

        Assert.Equal(["a", "b"], elements.Select(x => x.Attribute("id")!.Value));
    }

    [Fact]
    public void PersistingKeysToMongo_MakesItTheKeyRing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().PersistKeysToThargaMongo();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<MongoDataProtectionKeyRepository>(provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
    }

    [Fact]
    public void TheTeamRepository_RegistersTheConnectionStore()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository(o => o.RegisterTeamRepository<MongoSupportSenderDirectoryTests.TeamEntity, MongoSupportSenderDirectoryTests.Member>());

        Assert.Contains(services, x => x.ServiceType == typeof(ISupportAiConnectionStore) && x.ImplementationType == typeof(MongoSupportAiConnectionStore));
    }

    private static MongoDataProtectionKeyRepository Repository(IDataProtectionKeyCollection keys)
    {
        var services = new ServiceCollection();
        services.AddSingleton(keys);
        return new MongoDataProtectionKeyRepository(services.BuildServiceProvider());
    }

    private static SupportAiConnectionEntity Entity(StoredSupportAiConnection record) => new()
    {
        Id = ObjectId.GenerateNewId(),
        TeamKey = record.TeamKey,
        Kind = record.Kind,
        BaseAddress = record.BaseAddress,
        Model = record.Model,
        ProtectedApiKey = record.ProtectedApiKey,
        ChangedAt = record.ChangedAt,
        ChangedByIdentity = record.ChangedByIdentity,
        ChangedByName = record.ChangedByName
    };

    private static StoredSupportAiConnection ToRecord(SupportAiConnectionEntity entity) => new()
    {
        TeamKey = entity.TeamKey,
        Kind = entity.Kind,
        BaseAddress = entity.BaseAddress,
        Model = entity.Model,
        ProtectedApiKey = entity.ProtectedApiKey,
        ChangedAt = entity.ChangedAt,
        ChangedByIdentity = entity.ChangedByIdentity,
        ChangedByName = entity.ChangedByName
    };

    private static EntityChangeResult<T> Changed<T>(T before)
        => (EntityChangeResult<T>)Activator.CreateInstance(typeof(EntityChangeResult<T>), BindingFlags.NonPublic | BindingFlags.Instance, null, [before, before], null);

    private static async IAsyncEnumerable<DataProtectionKeyEntity> Read(params DataProtectionKeyEntity[] entities)
    {
        foreach (var entity in entities)
        {
            await Task.Yield();
            yield return entity;
        }
    }
}
