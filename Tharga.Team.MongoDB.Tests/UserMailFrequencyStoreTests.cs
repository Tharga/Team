using System.Reflection;
using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// How often each category was sent is one aggregation over the mail outbox: sent or bounced since a time, grouped by
/// category and recipient with mail to an address alone left out, then counted per category, every field it reads held by
/// one index so the server reads no mail.
/// </summary>
public class UserMailFrequencyStoreTests
{
    private static readonly DateTime Since = new(2025, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    private static BsonDocument[] Stages()
        => [.. UserMailCollection.SentByCategory(Since).Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).Documents];

    [Fact]
    public void TheFirstStage_TakesMailSentOrBounced_SinceTheTime()
    {
        var match = Stages()[0]["$match"].AsBsonDocument;

        Assert.Equal(new BsonArray { "Sent", "Bounced" }, match["Status"]["$in"].AsBsonArray);
        Assert.Equal(Since, match["CompletedAt"]["$gte"].ToUniversalTime());
    }

    [Fact]
    public void Mail_IsGroupedByCategoryAndRecipient_MailToAnAddressAloneLeftOut_ThenCountedPerCategory()
    {
        var stages = Stages();

        Assert.Equal(4, stages.Length);
        Assert.Equal(new BsonDocument { { "c", "$Category" }, { "u", "$RecipientUserKey" } }, stages[1]["$group"]["_id"].AsBsonDocument);
        Assert.Equal(new BsonDocument("$sum", 1), stages[1]["$group"]["n"].AsBsonDocument);
        Assert.Equal(new BsonDocument("$ne", BsonNull.Value), stages[2]["$match"]["_id.u"].AsBsonDocument);
        Assert.Equal("$_id.c", stages[3]["$group"]["_id"].AsString);
        Assert.Equal(new BsonDocument("$sum", "$n"), stages[3]["$group"]["sent"].AsBsonDocument);
        Assert.Equal(new BsonDocument("$sum", 1), stages[3]["$group"]["recipients"].AsBsonDocument);
    }

    [Fact]
    public void OneIndex_HoldsEveryFieldTheCountReads_EqualityFirstThenTheRange()
    {
        var collection = (UserMailCollection)RuntimeHelpers.GetUninitializedObject(typeof(UserMailCollection));
        typeof(UserMailCollection).GetField("_retention", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(collection, TimeSpan.FromDays(90));

        var index = collection.Indices.Single(x => x.Options.Name == "Status_CompletedAt_Category_RecipientUserKey");
        var keys = index.Keys.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry));

        Assert.Equal(["Status", "CompletedAt", "Category", "RecipientUserKey"], keys.Names);
        Assert.All(keys.Values, x => Assert.Equal(1, x.ToInt32()));
        Assert.Equal(TimeSpan.FromDays(90), collection.Retention);
    }

    [Fact]
    public void ARow_IsReadAsTheCategorysCount()
    {
        var counted = UserMailCollection.ToCategorySent(new BsonDocument { { "_id", "news" }, { "sent", 12 }, { "recipients", 4L } });

        Assert.Equal(new UserMailCategorySent("news", 12, 4), counted);
    }

    [Fact]
    public async Task TheOutbox_CountsThroughTheCollection_AndKeepsMailAsLongAsItDoes()
    {
        var collection = Substitute.For<IUserMailCollection>();
        collection.CountSentByCategoryAsync(Since, Arg.Any<CancellationToken>()).Returns([new UserMailCategorySent("news", 3, 2)]);
        collection.Retention.Returns(TimeSpan.FromDays(400));
        var outbox = new MongoUserMailOutbox(collection, Substitute.For<IUserMailBatchCollection>());

        var counted = await outbox.CountSentByCategoryAsync(Since, TestContext.Current.CancellationToken);

        Assert.Equal([new UserMailCategorySent("news", 3, 2)], counted);
        Assert.Equal(TimeSpan.FromDays(400), ((IUserMailOutbox)outbox).Retention);
    }
}
