using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The MongoDB side of the mail log saying what a mail was sent as and by whom, and of support's mail being listed in it: the new fields
/// are stored and read back, a row written already done is never claimed, and one answer is one row however often it is sent.
/// </summary>
public class UserMailSenderStoreTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);

    private readonly IUserMailCollection _mail = Substitute.For<IUserMailCollection>();
    private readonly IUserMailBatchCollection _batches = Substitute.For<IUserMailBatchCollection>();

    private static readonly UserMail Answer = new()
    {
        RecipientAddress = "user@customer.example",
        Category = "support",
        Subject = "Re: Export is empty",
        Kind = UserMailKind.SupportAnswer,
        TeamKey = "acme",
        SentByUserKey = "staff-1",
        FromAddress = "support@example.net",
        CaseId = "case-1",
        ReplyTo = "support+tag@example.net",
        DeduplicationKey = "support:case-1:2"
    };

    private static string Render(FilterDefinition<UserMailEntity> filter)
        => filter.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    [Fact]
    public void TheSenderAndTheCase_AreStored_AndReadBack()
    {
        var entity = MongoUserMailOutbox.ToEntity(Answer, Now);

        var item = MongoUserMailOutbox.ToItem(entity with { EffectiveReplyTo = "support+tag@example.net" });

        Assert.Equal(("staff-1", "support@example.net", "case-1"), (entity.SentByUserKey, entity.FromAddress, entity.CaseId));
        Assert.Equal(("staff-1", "support@example.net", "case-1"), (item.Mail.SentByUserKey, item.Mail.FromAddress, item.Mail.CaseId));
        Assert.Equal(("support@example.net", "support+tag@example.net"), (item.FromAddress, item.EffectiveReplyTo));
    }

    [Fact]
    public void TheSenderOfABatch_IsOnEveryMailOfIt()
    {
        var item = MongoUserMailOutbox.ToBatchItem("batch-1", "user-1", new UserMailBatch { RecipientUserKeys = ["user-1"], Category = "news", Subject = "Hello", SentByUserKey = "staff-1" }, Now);

        Assert.Equal("staff-1", item.SentByUserKey);
    }

    [Fact]
    public async Task ASupportAnswer_IsStoredDone_AsTheLogShowsIt_AndNeverClaimed()
    {
        UserMailEntity added = null;
        await _mail.AddAsync(Arg.Do<UserMailEntity>(x => added = x), Arg.Any<IClientSessionHandle>());

        await new MongoUserMailOutbox(_mail, _batches).AddCompletedAsync(Answer, UserMailStatus.Sent, null, "answer-1@example.net", "User@Customer.example",
            new UserMailRoute("support@example.net", "support+tag@example.net"), Now, TestContext.Current.CancellationToken);

        Assert.Equal((UserMailStatus.Sent, Now, Now, "answer-1@example.net"), (added.Status, added.QueuedAt, added.CompletedAt, added.MessageId));
        Assert.Equal(("user@customer.example", "support@example.net", "support+tag@example.net"), (added.RecipientAddress, added.FromAddress, added.EffectiveReplyTo));
        Assert.Equal((UserMailKind.SupportAnswer, "case-1", "staff-1", "acme"), (added.Kind, added.CaseId, added.SentByUserKey, added.TeamKey));
        Assert.Null(added.ClaimedBy);
        Assert.Equal(0, added.Attempts);
    }

    [Fact]
    public async Task AnAnswerSentAgain_UpdatesItsRow_InsteadOfAddingAnother()
    {
        var existing = MongoUserMailOutbox.ToEntity(Answer, Now) with { Status = UserMailStatus.Failed, Reason = "connection refused" };
        _mail.GetOneAsync(Arg.Any<System.Linq.Expressions.Expression<Func<UserMailEntity, bool>>>(), Arg.Any<OneOption<UserMailEntity>>(), Arg.Any<CancellationToken>()).Returns(existing);

        var id = await new MongoUserMailOutbox(_mail, _batches).AddCompletedAsync(Answer, UserMailStatus.Sent, null, "answer-2@example.net", "user@customer.example",
            new UserMailRoute("support@example.net", null), Now.AddMinutes(5), TestContext.Current.CancellationToken);

        Assert.Equal(existing.Id.ToString(), id);
        await _mail.DidNotReceiveWithAnyArgs().AddAsync(default, default);
        await _mail.Received(1).UpdateOneAsync(Arg.Any<FilterDefinition<UserMailEntity>>(), Arg.Any<UpdateDefinition<UserMailEntity>>(), Arg.Any<OneOption<UserMailEntity>>());
    }

    [Fact]
    public async Task ASentMail_RecordsTheRouteItWentWith()
    {
        FilterDefinition<UserMailEntity> filter = null;
        UpdateDefinition<UserMailEntity> update = null;
        await _mail.UpdateOneAsync(Arg.Do<FilterDefinition<UserMailEntity>>(x => filter = x), Arg.Do<UpdateDefinition<UserMailEntity>>(x => update = x), Arg.Any<OneOption<UserMailEntity>>());

        await new MongoUserMailOutbox(_mail, _batches).MarkSentAsync(ObjectId.GenerateNewId().ToString(), "me", "m1@example.net", "a@b.example",
            new UserMailRoute("hello@example.com", "reply@example.com"), Now, TestContext.Current.CancellationToken);

        var rendered = update.Render(new RenderArgs<UserMailEntity>(BsonSerializer.LookupSerializer<UserMailEntity>(), BsonSerializer.SerializerRegistry)).ToString();
        Assert.Contains("\"FromAddress\" : \"hello@example.com\"", rendered);
        Assert.Contains("\"EffectiveReplyTo\" : \"reply@example.com\"", rendered);
        Assert.NotNull(filter);
    }

    [Fact]
    public void ASupportMail_IsNeverInAUsersOwnHistory_EvenToTheirAddress()
    {
        var rendered = Render(MongoUserMailOutbox.ForRecipient("user-1", "kim@customer.example"));

        Assert.Contains("\"SupportAnswer\"", rendered);
        Assert.Contains("\"SupportReceipt\"", rendered);
    }

    [Fact]
    public void TheLogCanBeFilteredToSupportsMail()
    {
        var answers = Render(MongoUserMailOutbox.ForLog(new UserMailFilter { Kind = UserMailKind.SupportAnswer }));

        Assert.Contains("\"SupportAnswer\"", answers);
    }
}
