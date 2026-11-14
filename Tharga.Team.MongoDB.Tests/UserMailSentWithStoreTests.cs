using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The MongoDB side of who sent a mail and the From and Reply-To it went with: kept on the mail, a batch's on each of its
/// mails, written with the outcome of each attempt, and read as nothing on a mail stored before they were.
/// </summary>
public class UserMailSentWithStoreTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    private static readonly UserMailHeaders Headers = new("hello@example.com", "Example", "support@example.com");

    private readonly IUserMailCollection _mail = Substitute.For<IUserMailCollection>();
    private readonly IUserMailBatchCollection _batches = Substitute.For<IUserMailBatchCollection>();

    private UpdateDefinition<UserMailEntity> _update;

    private static string Render(UpdateDefinition<UserMailEntity> update)
        => update.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    private static EntityChangeResult<T> Changed<T>(T before)
        => (EntityChangeResult<T>)Activator.CreateInstance(typeof(EntityChangeResult<T>), BindingFlags.NonPublic | BindingFlags.Instance, null, [before, before], null);

    private UserMailEntity Stored()
    {
        var stored = MongoUserMailOutbox.ToEntity(Mail(), Now);
        _mail.UpdateOneAsync(
                Arg.Any<FilterDefinition<UserMailEntity>>(),
                Arg.Do<UpdateDefinition<UserMailEntity>>(x => _update = x),
                Arg.Any<OneOption<UserMailEntity>>(),
                Arg.Any<IClientSessionHandle>())
            .Returns(Changed(stored));
        return stored;
    }

    private MongoUserMailOutbox Outbox() => new(_mail, _batches);

    private static UserMail Mail() => new()
    {
        RecipientUserKey = "user-1",
        Category = "news",
        Subject = "Hello",
        Body = "Hi",
        SentByUserKey = "staff-1",
        SentByName = "Sam Staff"
    };

    [Fact]
    public void WhoSentAMail_SurvivesTheRoundTripToStorage()
    {
        var item = MongoUserMailOutbox.ToItem(MongoUserMailOutbox.ToEntity(Mail(), Now));

        Assert.Equal(("staff-1", "Sam Staff"), (item.Mail.SentByUserKey, item.Mail.SentByName));
    }

    [Fact]
    public void EachMailOfABatch_NamesWhoSentTheBatch()
    {
        var batch = new UserMailBatch { RecipientUserKeys = ["u1", "u2"], Category = "news", Subject = "Hello", Body = "Hi", SentByUserKey = "staff-1", SentByName = "Sam Staff" };

        var items = MongoUserMailOutbox.ToBatchItems("batch-1", batch, Now).Select(MongoUserMailOutbox.ToItem).ToArray();

        Assert.Equal(2, items.Length);
        Assert.All(items, x => Assert.Equal(("staff-1", "Sam Staff"), (x.Mail.SentByUserKey, x.Mail.SentByName)));
    }

    [Fact]
    public void TheFromAndReplyTo_SurviveTheRoundTripToStorage_ApartFromTheMailsOwnReplyAddress()
    {
        var entity = MongoUserMailOutbox.ToEntity(Mail() with { ReplyTo = "team@example.com" }, Now) with
        {
            FromAddress = "hello@example.com",
            FromName = "Example",
            EffectiveReplyTo = "team@example.com"
        };

        var item = MongoUserMailOutbox.ToItem(BsonSerializer.Deserialize<UserMailEntity>(entity.ToBsonDocument()));

        Assert.Equal(("hello@example.com", "Example", "team@example.com"), (item.FromAddress, item.FromName, item.ReplyTo));
        Assert.Equal("team@example.com", item.Mail.ReplyTo);
    }

    [Fact]
    public void AMailStoredBeforeTheseWere_ReadsAsNothing()
    {
        var document = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { nameof(UserMailEntity.RecipientUserKey), "user-1" },
            { nameof(UserMailEntity.Category), "news" },
            { nameof(UserMailEntity.Subject), "Hello" },
            { nameof(UserMailEntity.Body), "Hi" },
            { nameof(UserMailEntity.Attachments), new BsonArray() },
            { nameof(UserMailEntity.Status), "Sent" },
            { nameof(UserMailEntity.Attempts), 1 },
            { nameof(UserMailEntity.QueuedAt), Now },
            { nameof(UserMailEntity.NextAttemptAt), Now },
            { nameof(UserMailEntity.CompletedAt), Now }
        };

        var item = MongoUserMailOutbox.ToItem(BsonSerializer.Deserialize<UserMailEntity>(document));

        Assert.Equal(UserMailStatus.Sent, item.Status);
        Assert.Equal((null, null, null, null, null), (item.Mail.SentByUserKey, item.Mail.SentByName, item.FromAddress, item.FromName, item.ReplyTo));
    }

    [Fact]
    public void WhatIsNotKnown_IsNotStored()
    {
        var document = MongoUserMailOutbox.ToEntity(Mail() with { SentByUserKey = null, SentByName = null }, Now).ToBsonDocument();

        Assert.False(document.Contains(nameof(UserMailEntity.SentByUserKey)));
        Assert.False(document.Contains(nameof(UserMailEntity.SentByName)));
        Assert.False(document.Contains(nameof(UserMailEntity.FromAddress)));
        Assert.False(document.Contains(nameof(UserMailEntity.FromName)));
        Assert.False(document.Contains(nameof(UserMailEntity.EffectiveReplyTo)));
    }

    [Fact]
    public async Task ASentMail_IsRecordedWithTheFromAndReplyToItWentWith_InTheSameWrite()
    {
        var stored = Stored();

        Assert.True(await Outbox().MarkSentAsync(stored.Id.ToString(), "instance-a", "<m1@example.com>", "anna@example.org", Headers, Now, TestContext.Current.CancellationToken));

        var update = Render(_update);
        Assert.Contains("\"Status\" : \"Sent\"", update);
        Assert.Contains("\"FromAddress\" : \"hello@example.com\"", update);
        Assert.Contains("\"FromName\" : \"Example\"", update);
        Assert.Contains("\"EffectiveReplyTo\" : \"support@example.com\"", update);
    }

    [Fact]
    public async Task AFailedOrSkippedMail_IsRecordedWithTheFromAndReplyToItWouldHaveGoneWith()
    {
        var stored = Stored();

        await Outbox().MarkFailedAsync(stored.Id.ToString(), "instance-a", "No user has this key.", Headers, Now, TestContext.Current.CancellationToken);
        var failed = Render(_update);
        await Outbox().MarkSkippedAsync(stored.Id.ToString(), "instance-a", "Turned off.", Headers, Now, TestContext.Current.CancellationToken);
        var skipped = Render(_update);

        Assert.Contains("\"Status\" : \"Failed\"", failed);
        Assert.Contains("\"FromAddress\" : \"hello@example.com\"", failed);
        Assert.Contains("\"EffectiveReplyTo\" : \"support@example.com\"", failed);
        Assert.Contains("\"Status\" : \"Skipped\"", skipped);
        Assert.Contains("\"FromAddress\" : \"hello@example.com\"", skipped);
    }

    [Fact]
    public async Task APostponedMail_IsRecordedWithTheFromAndReplyToThisAttemptUsed()
    {
        var stored = Stored();

        await Outbox().PostponeAsync(stored.Id.ToString(), "instance-a", "451 try later", Headers, Now.AddMinutes(2), TestContext.Current.CancellationToken);

        var update = Render(_update);
        Assert.DoesNotContain("\"Status\"", update);
        Assert.Contains("\"FromName\" : \"Example\"", update);
        Assert.Contains("\"EffectiveReplyTo\" : \"support@example.com\"", update);
    }

    [Fact]
    public async Task AnAttemptWithNoReplyTo_RemovesOneAnEarlierAttemptRecorded()
    {
        var stored = Stored();

        await Outbox().MarkSentAsync(stored.Id.ToString(), "instance-a", "<m1@example.com>", "anna@example.org", Headers with { FromName = null, ReplyTo = null }, Now, TestContext.Current.CancellationToken);

        var update = Render(_update);
        Assert.Contains("\"FromAddress\" : \"hello@example.com\"", update);
        Assert.Contains("\"FromName\" : 1", update);
        Assert.Contains("\"EffectiveReplyTo\" : 1", update);
    }

    [Fact]
    public async Task AnOutcomeWithoutThem_LeavesTheFromAndReplyToAlone()
    {
        var stored = Stored();

        await Outbox().MarkSentAsync(stored.Id.ToString(), "instance-a", "<m1@example.com>", "anna@example.org", Now, TestContext.Current.CancellationToken);

        var update = Render(_update);
        Assert.DoesNotContain("FromAddress", update);
        Assert.DoesNotContain("FromName", update);
        Assert.DoesNotContain("EffectiveReplyTo", update);
    }
}
