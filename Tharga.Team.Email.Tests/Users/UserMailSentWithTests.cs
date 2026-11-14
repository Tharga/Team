using Tharga.Team.Email.Users;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// Every mail records who caused it to be sent and the From and Reply-To it went with, or would have gone with when it was
/// never delivered: mail to users from the configured sender, contact mail from the contact sender.
/// </summary>
public class UserMailSentWithTests
{
    private const string Claimant = "instance-a";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UserMail Mail(string replyTo = null, string sentByUserKey = null, string sentByName = null) => new()
    {
        RecipientUserKey = UserMailFixture.UserKey,
        Category = UserMailFixture.News,
        Subject = "Hello",
        Body = "Text.",
        ReplyTo = replyTo,
        SentByUserKey = sentByUserKey,
        SentByName = sentByName
    };

    private static (string FromAddress, string FromName, string ReplyTo) SentWith(UserMailItem item) => (item.FromAddress, item.FromName, item.ReplyTo);

    [Fact]
    public async Task ASentMail_RecordsTheConfiguredFromAndReplyTo()
    {
        var fixture = new UserMailFixture();
        await fixture.Queue.QueueAsync(Mail(), Ct);

        Assert.Equal(UserMailStatus.Sent, await fixture.Dispatcher().SendNextAsync(Claimant, Ct));

        Assert.Equal(("hello@example.com", "Example", "support@example.com"), SentWith(fixture.Outbox.Single));
    }

    [Fact]
    public async Task AReplyAddressOnTheMail_IsRecordedAsTheReplyToItWentWith_AndTheOverrideIsKept()
    {
        var fixture = new UserMailFixture();
        await fixture.Queue.QueueAsync(Mail(replyTo: "team@example.com"), Ct);

        await fixture.Dispatcher().SendNextAsync(Claimant, Ct);

        var item = fixture.Outbox.Single;
        Assert.Equal("team@example.com", item.ReplyTo);
        Assert.Equal("team@example.com", item.Mail.ReplyTo);
    }

    [Fact]
    public async Task WithNoReplyAddressConfigured_NoReplyToIsRecorded_ButTheFromIs()
    {
        var fixture = new UserMailFixture(o => o.ReplyTo = null);
        await fixture.Queue.QueueAsync(Mail(), Ct);

        await fixture.Dispatcher().SendNextAsync(Claimant, Ct);

        Assert.Equal(("hello@example.com", "Example", (string)null), SentWith(fixture.Outbox.Single));
    }

    [Fact]
    public async Task AMailThatFailsAtOnce_StillRecordsTheFromAndReplyToItWouldHaveGoneWith()
    {
        var fixture = new UserMailFixture();
        await fixture.Queue.QueueAsync(Mail(), Ct);
        fixture.Recipients.Remove(UserMailFixture.UserKey);

        Assert.Equal(UserMailStatus.Failed, await fixture.Dispatcher().SendNextAsync(Claimant, Ct));

        Assert.Equal(("hello@example.com", "Example", "support@example.com"), SentWith(fixture.Outbox.Single));
    }

    [Fact]
    public async Task AMailGivenUp_StillRecordsTheFromAndReplyTo()
    {
        var fixture = new UserMailFixture(o => o.MaxAttempts = 1);
        fixture.Mail.Respond = _ => MailSendResult.Failed("550 mailbox unavailable");
        await fixture.Queue.QueueAsync(Mail(replyTo: "team@example.com"), Ct);

        Assert.Equal(UserMailStatus.Failed, await fixture.Dispatcher().SendNextAsync(Claimant, Ct));

        Assert.Equal(("hello@example.com", "Example", "team@example.com"), SentWith(fixture.Outbox.Single));
    }

    [Fact]
    public async Task AMailLeftToBeTriedAgain_RecordsTheFromAndReplyTo()
    {
        var fixture = new UserMailFixture();
        fixture.Mail.Respond = _ => MailSendResult.Failed("451 try later");
        await fixture.Queue.QueueAsync(Mail(), Ct);

        Assert.Equal(UserMailStatus.Queued, await fixture.Dispatcher().SendNextAsync(Claimant, Ct));

        Assert.Equal(("hello@example.com", "Example", "support@example.com"), SentWith(fixture.Outbox.Single));
    }

    [Fact]
    public async Task AMailSkipped_RecordsTheFromAndReplyTo()
    {
        var fixture = new UserMailFixture();
        await fixture.Queue.QueueAsync(Mail() with { Category = UserMailFixture.Digest }, Ct);

        Assert.Equal(UserMailStatus.Skipped, await fixture.Dispatcher().SendNextAsync(Claimant, Ct));

        Assert.Equal(("hello@example.com", "Example", "support@example.com"), SentWith(fixture.Outbox.Single));
    }

    [Fact]
    public async Task AMailNeverAttempted_HasNoFromOrReplyToYet()
    {
        var fixture = new UserMailFixture();
        await fixture.Queue.QueueAsync(Mail(), Ct);

        Assert.Equal((null, null, null), SentWith(fixture.Outbox.Single));
    }

    [Fact]
    public async Task AContactMail_RecordsTheContactSenderAndReplyTo()
    {
        var fixture = new ContactMailFixture(o =>
        {
            o.FromAddress = "sales@example.com";
            o.FromName = "Example Sales";
            o.ReplyTo = "deals@example.com";
        });
        await fixture.QueueAsync();

        await fixture.Dispatcher().RunAsync(Claimant, Ct);

        var item = fixture.Mail.Outbox.Single;
        Assert.Equal(UserMailStatus.Sent, item.Status);
        Assert.Equal(("sales@example.com", "Example Sales", "deals@example.com"), SentWith(item));
    }

    [Fact]
    public async Task AContactMailWithNoSenderOfItsOwn_RecordsThatOfMailToUsers()
    {
        var fixture = new ContactMailFixture();
        await fixture.QueueAsync();

        await fixture.Dispatcher().RunAsync(Claimant, Ct);

        Assert.Equal(("hello@example.com", "Example", "support@example.com"), SentWith(fixture.Mail.Outbox.Single));
    }

    [Fact]
    public async Task AContactMailThatIsWithheld_StillRecordsTheContactSenderAndReplyTo()
    {
        var fixture = new ContactMailFixture(o => o.ReplyTo = "deals@example.com");
        fixture.Mail.Policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(new OutboundMailDecision(OutboundMailAction.Withhold, null, null));
        await fixture.QueueAsync();

        await fixture.Dispatcher().RunAsync(Claimant, Ct);

        var item = fixture.Mail.Outbox.Single;
        Assert.Equal(UserMailStatus.Failed, item.Status);
        Assert.Equal(("hello@example.com", "Example", "deals@example.com"), SentWith(item));
    }

    [Fact]
    public async Task AContactMailWithContactMailNotRegistered_RecordsNoSender_SinceItHasNone()
    {
        var fixture = new ContactMailFixture();
        await fixture.QueueAsync();

        await fixture.Mail.Dispatcher().RunAsync(Claimant, Ct);

        var item = fixture.Mail.Outbox.Single;
        Assert.Equal(UserMailStatus.Failed, item.Status);
        Assert.Equal((null, null, null), SentWith(item));
    }

    [Fact]
    public async Task WhoCausedAMailToBeSent_IsKept_Trimmed()
    {
        var fixture = new UserMailFixture();

        await fixture.Queue.QueueAsync(Mail(sentByUserKey: " staff-1 ", sentByName: " Sam\nStaff "), Ct);

        var mail = fixture.Outbox.Single.Mail;
        Assert.Equal(("staff-1", "Sam Staff"), (mail.SentByUserKey, mail.SentByName));
    }

    [Fact]
    public async Task AnAutomaticMail_NamesNobody()
    {
        var fixture = new UserMailFixture();

        await fixture.Queue.QueueAsync(Mail(sentByUserKey: " ", sentByName: ""), Ct);

        var mail = fixture.Outbox.Single.Mail;
        Assert.Equal(((string)null, (string)null), (mail.SentByUserKey, mail.SentByName));
    }

    [Fact]
    public async Task ABatch_KeepsWhoCausedItToBeSent_OnEachOfItsMails()
    {
        var fixture = new UserMailFixture();
        fixture.Recipients.Add(new UserMailRecipient { Key = "user-2", EMail = "bo@example.org" });

        await fixture.Queue.QueueBatchAsync(new UserMailBatch
        {
            RecipientUserKeys = [UserMailFixture.UserKey, "user-2"],
            Category = UserMailFixture.News,
            Subject = "Hello",
            Body = "Text.",
            SentByUserKey = " staff-1 ",
            SentByName = "Sam Staff"
        }, Ct);

        Assert.All(fixture.Outbox.Items, x => Assert.Equal(("staff-1", "Sam Staff"), (x.Mail.SentByUserKey, x.Mail.SentByName)));
    }

    [Fact]
    public async Task ARefusedMail_StillNamesWhoTriedToSendIt()
    {
        var fixture = new UserMailFixture();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Queue.QueueAsync(Mail(sentByUserKey: "staff-1", sentByName: "Sam Staff") with { Category = "nope" }, Ct));

        var mail = fixture.Outbox.Single.Mail;
        Assert.Equal(("staff-1", "Sam Staff"), (mail.SentByUserKey, mail.SentByName));
    }

    [Fact]
    public async Task WhatBecameOfAMail_SaysWhoSentIt_AndTheFromAndReplyToItWentWith()
    {
        var fixture = new UserMailFixture();
        var queued = await fixture.Queue.QueueAsync(Mail(sentByUserKey: "staff-1", sentByName: "Sam Staff"), Ct);
        await fixture.Dispatcher().SendNextAsync(Claimant, Ct);

        var delivery = await fixture.Queue.GetAsync(queued.Id, Ct);

        Assert.Equal(("staff-1", "Sam Staff"), (delivery.SentByUserKey, delivery.SentByName));
        Assert.Equal(("hello@example.com", "Example", "support@example.com"), (delivery.FromAddress, delivery.FromName, delivery.ReplyTo));
    }
}
