using System.Reflection;
using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Contact mail in the queue of mail to users: a batch stores one mail per address alone, as a contact, after its users;
/// a user's history never holds contact mail to their address; a bounce finds a contact mail as any other; and how often each
/// contact category is sent is one aggregation per distinct address, read from an index of its own.
/// </summary>
public class ContactMailStoreTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private readonly IUserMailCollection _mail = Substitute.For<IUserMailCollection>();
    private readonly IUserMailBatchCollection _batches = Substitute.For<IUserMailBatchCollection>();

    private static string Render(FilterDefinition<UserMailEntity> filter)
        => filter.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    private static UserMailBatch ContactBatch() => new()
    {
        RecipientUserKeys = [],
        RecipientAddresses = [new UserMailBatchAddress("kim@customer.example", "Kim Customer"), new UserMailBatchAddress("lee@customer.example")],
        Category = "offers",
        Subject = "Something for you",
        Body = "An offer.",
        Kind = UserMailKind.Contact
    };

    [Fact]
    public async Task AContactBatch_StoresOneMailPerAddressAlone_NamingNoUser()
    {
        UserMailBatchEntity stored = null;
        List<UserMailEntity> items = null;
        await _batches.AddAsync(Arg.Do<UserMailBatchEntity>(x => stored = x));
        await _mail.AddManyAsync(Arg.Do<IEnumerable<UserMailEntity>>(x => items = [.. x]));

        var id = await new MongoUserMailOutbox(_mail, _batches).AddBatchAsync(ContactBatch(), Now, TestContext.Current.CancellationToken);

        Assert.Equal(2, stored.RecipientCount);
        Assert.Equal(["kim@customer.example", "lee@customer.example"], items.Select(x => x.RecipientAddress));
        Assert.Equal(["Kim Customer", null], items.Select(x => x.RecipientName));
        Assert.All(items, x =>
        {
            Assert.Null(x.RecipientUserKey);
            Assert.Equal(UserMailKind.Contact, x.Kind);
            Assert.Equal(id, x.BatchId);
            Assert.Equal("offers", x.Category);
            Assert.Equal(UserMailStatus.Queued, x.Status);
        });
    }

    [Fact]
    public void AContactMailOfABatch_IsReadWithItsAddressAndKind()
    {
        var entity = MongoUserMailOutbox.ToBatchItems("b-1", ContactBatch(), Now).First();
        var item = MongoUserMailOutbox.ToItem(entity);

        Assert.Equal(("kim@customer.example", "Kim Customer", UserMailKind.Contact), (item.Mail.RecipientAddress, item.Mail.RecipientName, item.Mail.Kind));
        Assert.Equal("kim@customer.example", item.RecipientAddress);
        Assert.Equal("Contact", entity.ToBsonDocument()[nameof(UserMailEntity.Kind)].AsString);
    }

    [Fact]
    public void ABatchNamingUsersAndAddresses_CountsBoth_UsersFirst()
    {
        var batch = ContactBatch() with { RecipientUserKeys = ["u-1"] };

        Assert.Equal(3, MongoUserMailOutbox.ToBatchEntity(batch, Now).RecipientCount);
        Assert.Equal(["u-1", null, null], MongoUserMailOutbox.ToBatchItems("b-1", batch, Now).Select(x => x.RecipientUserKey));
    }

    [Fact]
    public void AUsersHistory_LeavesOutContactMailToTheirAddress()
    {
        var rendered = Render(MongoUserMailOutbox.ForRecipient("user-1", "kim@customer.example"));

        Assert.Contains("\"RecipientUserKey\" : null, \"RecipientAddress\" : \"kim@customer.example\", \"Kind\" : { \"$ne\" : \"Contact\" }", rendered);
        Assert.Contains("{ \"RecipientUserKey\" : \"user-1\" }", rendered);
    }

    [Fact]
    public void ABounce_FindsTheSentMailByItsMessageIdAlone_WhateverKindItIs()
    {
        var rendered = Render(MongoUserMailOutbox.BouncedSent(MongoUserMailOutbox.MessageIdForms("<m1@example.com>")));

        Assert.DoesNotContain("Kind", rendered);
        Assert.DoesNotContain("RecipientUserKey", rendered);
        Assert.Contains("\"Status\" : \"Sent\"", rendered);
    }

    [Fact]
    public void HowOftenContactMailIsSent_TakesContactMailSentOrBounced_SinceTheTime_GroupedByAddress()
    {
        var stages = UserMailCollection.SentToContactsByCategory(Now).Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).Documents.ToArray();

        var match = stages[0]["$match"].AsBsonDocument;
        Assert.Equal("Contact", match["Kind"].AsString);
        Assert.Equal(new BsonArray { "Sent", "Bounced" }, match["Status"]["$in"].AsBsonArray);
        Assert.Equal(Now, match["CompletedAt"]["$gte"].ToUniversalTime());
        Assert.Equal(new BsonDocument { { "c", "$Category" }, { "u", "$RecipientAddress" } }, stages[1]["$group"]["_id"].AsBsonDocument);
        Assert.Equal("$_id.c", stages[3]["$group"]["_id"].AsString);
        Assert.Equal(new BsonDocument("$sum", 1), stages[3]["$group"]["recipients"].AsBsonDocument);
    }

    [Fact]
    public void HowOftenMailToUsersIsSent_StillGroupsByUser()
    {
        var stages = UserMailCollection.SentByCategory(Now).Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).Documents.ToArray();

        Assert.False(stages[0]["$match"].AsBsonDocument.Contains("Kind"));
        Assert.Equal(new BsonDocument { { "c", "$Category" }, { "u", "$RecipientUserKey" } }, stages[1]["$group"]["_id"].AsBsonDocument);
    }

    [Fact]
    public void OneIndex_HoldsEveryFieldTheContactCountReads_EqualityFirstThenTheRange()
    {
        var collection = (UserMailCollection)RuntimeHelpers.GetUninitializedObject(typeof(UserMailCollection));
        typeof(UserMailCollection).GetField("_retention", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(collection, TimeSpan.FromDays(90));

        var index = collection.Indices.Single(x => x.Options.Name == "Kind_Status_CompletedAt_Category_RecipientAddress");
        var keys = index.Keys.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry));

        Assert.Equal(["Kind", "Status", "CompletedAt", "Category", "RecipientAddress"], keys.Names);
    }

    [Fact]
    public async Task TheOutbox_CountsContactMailThroughTheCollection()
    {
        _mail.CountSentToContactsByCategoryAsync(Now, Arg.Any<CancellationToken>()).Returns([new UserMailCategorySent("offers", 4, 2)]);

        var counted = await new MongoUserMailOutbox(_mail, _batches).CountSentToContactsByCategoryAsync(Now, TestContext.Current.CancellationToken);

        Assert.Equal([new UserMailCategorySent("offers", 4, 2)], counted);
    }
}
