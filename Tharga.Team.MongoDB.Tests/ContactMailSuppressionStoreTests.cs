using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The addresses that opted out of contact mail: one document per address and category, the address trimmed and in lower
/// case, an opt-out of every category stored under a category no key can be, the first one kept, and an address's
/// opt-outs read in slices by the unique index.
/// </summary>
public class ContactMailSuppressionStoreTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private readonly IContactMailSuppressionCollection _collection = Substitute.For<IContactMailSuppressionCollection>();

    private static string Render(FilterDefinition<ContactMailSuppressionEntity> filter)
        => filter.Render(new RenderArgs<ContactMailSuppressionEntity>(BsonSerializer.LookupSerializer<ContactMailSuppressionEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    [Fact]
    public void AnOptOutOfOneCategory_SurvivesTheRoundTripToStorage_ItsAddressTrimmedAndLowerCase()
    {
        var entity = MongoContactMailSuppressionStore.ToEntity(new ContactMailSuppression { Address = "  Anna@Example.ORG ", Category = "news", SuppressedAt = Now, Source = ContactMailSuppressionSource.OneClick });
        var read = MongoContactMailSuppressionStore.ToSuppression(entity);

        Assert.Equal("anna@example.org", entity.Address);
        Assert.Equal(new ContactMailSuppression { Address = "anna@example.org", Category = "news", SuppressedAt = Now, Source = ContactMailSuppressionSource.OneClick }, read);
        Assert.Equal(DateTimeKind.Utc, read.SuppressedAt.Kind);
    }

    [Fact]
    public void AnOptOutOfEverything_IsStoredUnderACategoryNoKeyCanBe_AndReadAsNoCategory()
    {
        var entity = MongoContactMailSuppressionStore.ToEntity(new ContactMailSuppression { Address = "anna@example.org", Category = null, SuppressedAt = Now, Source = ContactMailSuppressionSource.Link });

        Assert.Equal("*", entity.Category);
        Assert.Null(MongoContactMailSuppressionStore.ToSuppression(entity).Category);
        Assert.True(MongoContactMailSuppressionStore.ToSuppression(entity).AppliesTo("news"));
    }

    [Fact]
    public void HowItWasAskedFor_IsStoredByName()
    {
        var document = MongoContactMailSuppressionStore.ToEntity(new ContactMailSuppression { Address = "anna@example.org", SuppressedAt = Now, Source = ContactMailSuppressionSource.Staff }).ToBsonDocument();

        Assert.Equal("Staff", document[nameof(ContactMailSuppressionEntity.Source)].AsString);
    }

    [Fact]
    public void AnOptOutWithNoAddress_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => MongoContactMailSuppressionStore.ToEntity(new ContactMailSuppression { Address = " ", SuppressedAt = Now, Source = ContactMailSuppressionSource.Staff }));
    }

    [Fact]
    public async Task Adding_InsertsOnce_AndSaysWhetherItWasNew()
    {
        ContactMailSuppressionEntity added = null;
        _collection.TryAddAsync(Arg.Do<ContactMailSuppressionEntity>(x => added = x), Arg.Any<IClientSessionHandle>()).Returns(true, false);
        var store = new MongoContactMailSuppressionStore(_collection);
        var suppression = new ContactMailSuppression { Address = "Anna@example.org", Category = "news", SuppressedAt = Now, Source = ContactMailSuppressionSource.Link };

        Assert.True(await store.AddAsync(suppression, TestContext.Current.CancellationToken));
        Assert.False(await store.AddAsync(suppression, TestContext.Current.CancellationToken));
        Assert.Equal("anna@example.org", added.Address);
        Assert.NotEqual(ObjectId.Empty, added.Id);
    }

    [Fact]
    public async Task AnAddressesOptOuts_AreReadByTheirNormalizedAddresses_InSlices()
    {
        var filters = new List<FilterDefinition<ContactMailSuppressionEntity>>();
        _collection.GetAsync(Arg.Do<FilterDefinition<ContactMailSuppressionEntity>>(filters.Add), Arg.Any<Options<ContactMailSuppressionEntity>>(), Arg.Any<CancellationToken>())
            .Returns(Read(new ContactMailSuppressionEntity { Address = "anna@example.org", Category = "*", SuppressedAt = Now, Source = ContactMailSuppressionSource.Link }));
        var store = new MongoContactMailSuppressionStore(_collection);
        string[] addresses = [" Anna@Example.org", "anna@example.org", .. Enumerable.Range(0, MongoContactMailSuppressionStore.AddressesPerQuery).Select(i => $"p{i}@example.org")];

        var found = await store.GetAsync(addresses, TestContext.Current.CancellationToken);

        Assert.Equal(2, filters.Count);
        Assert.Contains("\"anna@example.org\"", Render(filters[0]));
        Assert.DoesNotContain("Anna", Render(filters[0]));
        Assert.All(found, x => Assert.Null(x.Category));
    }

    [Fact]
    public async Task NoAddresses_ReadsNothing()
    {
        var store = new MongoContactMailSuppressionStore(_collection);

        Assert.Empty(await store.GetAsync([null, " "], TestContext.Current.CancellationToken));
        _collection.DidNotReceiveWithAnyArgs().GetAsync(default(FilterDefinition<ContactMailSuppressionEntity>));
    }

    [Fact]
    public void TheCollection_IsUniqueOnAddressAndCategory()
    {
        var collection = (ContactMailSuppressionCollection)RuntimeHelpers.GetUninitializedObject(typeof(ContactMailSuppressionCollection));

        var index = Assert.Single(collection.Indices);
        var keys = index.Keys.Render(new RenderArgs<ContactMailSuppressionEntity>(BsonSerializer.LookupSerializer<ContactMailSuppressionEntity>(), BsonSerializer.SerializerRegistry));

        Assert.Equal("Address_Category", index.Options.Name);
        Assert.True(index.Options.Unique);
        Assert.Equal(["Address", "Category"], keys.Names);
        Assert.Equal("ContactMailSuppression", new ThargaTeamOptions().ContactMailSuppressionCollectionName);
    }

    [Fact]
    public void TheRepository_RegistersTheStore_UnlessTheHostHasItsOwn()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository();

        Assert.Contains(services, x => x.ServiceType == typeof(IContactMailSuppressionStore) && x.ImplementationType == typeof(MongoContactMailSuppressionStore));

        var own = new ServiceCollection();
        var mine = Substitute.For<IContactMailSuppressionStore>();
        own.AddSingleton(mine);
        own.AddThargaTeamRepository();

        Assert.Same(mine, Assert.Single(own, x => x.ServiceType == typeof(IContactMailSuppressionStore)).ImplementationInstance);
    }

    private static async IAsyncEnumerable<ContactMailSuppressionEntity> Read(params ContactMailSuppressionEntity[] entities)
    {
        foreach (var entity in entities)
        {
            await Task.Yield();
            yield return entity;
        }
    }
}
