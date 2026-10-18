using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The log of messages to contacts: a message is kept with every address it was listed for and how each was checked, a
/// page of the log is read newest first without the addresses, the log is unique on the message and expires with the mail
/// it queued, and the repository registers it unless the host keeps its own.
/// </summary>
public class ContactMessageLogTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static ContactMessageLogEntry Entry(string id = "m-1") => new()
    {
        Id = id,
        Category = "offers",
        Subject = "Something for you",
        AttachmentCount = 1,
        SentByUserKey = "staff-1",
        SentByName = "Sam Staff",
        SentAt = Now,
        RecipientCount = 1,
        OptedOutCount = 1,
        LeftOutCount = 2,
        Addressees =
        [
            new ContactAddresseeCheck { Address = "kim@customer.example", Name = "Kim", Detail = "Customer Ltd", References = ["c-1", "c-3"], Status = ContactAddresseeStatus.Reached },
            new ContactAddresseeCheck { Address = "lee@customer.example", References = ["c-2"], Status = ContactAddresseeStatus.OptedOut, OptedOutOfAll = true },
            new ContactAddresseeCheck { Address = "not an address", Status = ContactAddresseeStatus.LeftOut, InvalidAddress = true },
            new ContactAddresseeCheck { Address = "max@customer.example", Status = ContactAddresseeStatus.LeftOut, ExcludedReason = "Already a customer" }
        ]
    };

    [Fact]
    public void AnEntry_SurvivesTheRoundTripToStorage_WithItsAddressesAndHowEachWasChecked()
    {
        var read = MongoContactMessageLog.ToEntry(MongoContactMessageLog.ToEntity(Entry()));

        Assert.Equivalent(Entry(), read);
        Assert.Equal(DateTimeKind.Utc, read.SentAt.Kind);
    }

    [Fact]
    public void AnAddressesStatus_IsStoredByName_AndWhatItDoesNotHaveIsLeftOut()
    {
        var document = MongoContactMessageLog.ToEntity(Entry()).ToBsonDocument();
        var addressees = document[nameof(ContactMessageEntity.Addressees)].AsBsonArray;

        Assert.Equal("Reached", addressees[0]["Status"].AsString);
        Assert.False(addressees[0].AsBsonDocument.Contains("OptedOutOfAll"));
        Assert.False(addressees[0].AsBsonDocument.Contains("ExcludedReason"));
        Assert.True(addressees[1]["OptedOutOfAll"].AsBoolean);
    }

    [Fact]
    public async Task APageOfTheLog_IsReadNewestFirst_WithoutTheAddresses_AndACursorForTheNext()
    {
        var collection = Substitute.For<IContactMessageCollection>();
        Options<ContactMessageEntity> options = null;
        collection.GetManyAsync(Arg.Any<FilterDefinition<ContactMessageEntity>>(), Arg.Do<Options<ContactMessageEntity>>(x => options = x), Arg.Any<CancellationToken>())
            .Returns(new Result<ContactMessageEntity, ObjectId> { Items = [MongoContactMessageLog.ToEntity(Entry("m-2")), MongoContactMessageLog.ToEntity(Entry("m-1"))] });

        var page = await new MongoContactMessageLog(collection).ListAsync(null, 1, TestContext.Current.CancellationToken);

        Assert.Equal(["m-2"], page.Items.Select(x => x.Id));
        Assert.Equal($"{Now.Ticks}:m-2", page.NextCursor);
        Assert.Equal(2, options.Limit);
        Assert.Equal("{ \"Addressees\" : 0 }", options.Projection.Render(new RenderArgs<ContactMessageEntity>(BsonSerializer.LookupSerializer<ContactMessageEntity>(), BsonSerializer.SerializerRegistry)).ToString());
        Assert.Equal("{ \"SentAt\" : -1, \"MessageId\" : 1 }", options.Sort.Render(new RenderArgs<ContactMessageEntity>(BsonSerializer.LookupSerializer<ContactMessageEntity>(), BsonSerializer.SerializerRegistry)).ToString());
    }

    [Fact]
    public void TheCursor_ContinuesAfterTheLastMessageShown_AndOneNotIssuedIsRefused()
    {
        var rendered = MongoContactMessageLog.After($"{Now.Ticks}:m-2").Render(new RenderArgs<ContactMessageEntity>(BsonSerializer.LookupSerializer<ContactMessageEntity>(), BsonSerializer.SerializerRegistry)).ToString();

        Assert.Contains("$lt", rendered);
        Assert.Contains("\"MessageId\" : { \"$gt\" : \"m-2\" }", rendered);
        Assert.Throws<ArgumentException>(() => MongoContactMessageLog.After("not-a-cursor"));
    }

    [Fact]
    public void TheLog_IsUniqueOnTheMessage_AndExpiresAfterTheRetention()
    {
        var collection = (ContactMessageCollection)RuntimeHelpers.GetUninitializedObject(typeof(ContactMessageCollection));
        typeof(ContactMessageCollection).GetField("_retention", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(collection, TimeSpan.FromDays(30));

        var indices = collection.Indices.ToDictionary(x => x.Options.Name);

        Assert.True(indices["MessageId"].Options.Unique);
        Assert.Contains("SentAt_MessageId", indices.Keys);
        Assert.Equal(TimeSpan.FromDays(30), indices["SentAt_ttl"].Options.ExpireAfter);
        Assert.Equal("ContactMessage", new ThargaTeamOptions().ContactMessageCollectionName);
    }

    [Fact]
    public void TheRepository_RegistersTheLog_UnlessTheHostHasItsOwn()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository();

        Assert.Contains(services, x => x.ServiceType == typeof(IContactMessageLog) && x.ImplementationType == typeof(MongoContactMessageLog));

        var own = new ServiceCollection();
        var mine = Substitute.For<IContactMessageLog>();
        own.AddSingleton(mine);
        own.AddThargaTeamRepository();

        Assert.Same(mine, Assert.Single(own, x => x.ServiceType == typeof(IContactMessageLog)).ImplementationInstance);
    }
}
