using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// The mail log says what a mail was sent as and by whom, tells the host once when a mail ends badly, and lets the sender read one mail
/// by its id without the whole log.
/// </summary>
public class UserMailLogSenderTests
{
    private const string Staff = "staff-1";
    private const string Stranger = "stranger-1";

    private readonly UserMailFixture _mail = new();

    private async Task<UserMailItem> SentByAsync(string sentBy, string replyTo = null)
    {
        await _mail.Queue.QueueAsync(new UserMail
        {
            RecipientUserKey = UserMailFixture.UserKey,
            Category = UserMailFixture.News,
            Subject = "Hello",
            Body = "Hi.",
            SentByUserKey = sentBy,
            ReplyTo = replyTo
        });
        await _mail.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);

        return _mail.Outbox.Single;
    }

    [Fact]
    public async Task ASentMail_RecordsWhatItWasSentFrom_AndTheReplyAddressItCarried()
    {
        var sent = await SentByAsync(Staff);

        Assert.Equal("hello@example.com", sent.FromAddress);
        Assert.Equal("support@example.com", sent.EffectiveReplyTo);
    }

    [Fact]
    public async Task AReplyAddressTheMailNamed_IsTheOneRecorded_NotTheConfiguredOne()
    {
        var sent = await SentByAsync(Staff, replyTo: "sales@example.org");

        Assert.Equal("sales@example.org", sent.EffectiveReplyTo);
    }

    [Fact]
    public async Task TheLogCarriesTheRoute_AndWhoSentIt()
    {
        var sent = await SentByAsync(Staff);

        var delivery = await _mail.Queue.GetAsync(sent.Id, TestContext.Current.CancellationToken);

        Assert.Equal(Staff, delivery.SentByUserKey);
        Assert.Equal("hello@example.com", delivery.FromAddress);
        Assert.Equal("support@example.com", delivery.ReplyTo);
    }

    [Fact]
    public async Task AMailTheApplicationSentOnItsOwn_HasNoSender()
    {
        var sent = await SentByAsync(null);

        Assert.Null((await _mail.Queue.GetAsync(sent.Id, TestContext.Current.CancellationToken)).SentByUserKey);
    }

    [Fact]
    public async Task ABatch_CarriesWhoSentItOnEveryMail()
    {
        await _mail.Queue.QueueBatchAsync(new UserMailBatch
        {
            RecipientUserKeys = [UserMailFixture.UserKey],
            Category = UserMailCategories.ServiceKey,
            Subject = "Maintenance",
            Body = "Tonight.",
            SentByUserKey = Staff
        }, TestContext.Current.CancellationToken);

        Assert.Equal(Staff, _mail.Outbox.Single.Mail.SentByUserKey);
    }

    [Fact]
    public async Task AMailGivenUp_TellsTheSinks_Once_WithWhoSentIt()
    {
        _mail.Recipients.Remove(UserMailFixture.UserKey);
        await _mail.Queue.QueueAsync(new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = UserMailFixture.News, Subject = "Hello", Body = "Hi.", SentByUserKey = Staff, TeamKey = "team-a" }, TestContext.Current.CancellationToken);

        await _mail.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);

        var told = Assert.Single(_mail.Sink.Told);
        Assert.Equal(MailOutcomeKind.GivenUp, told.Outcome);
        Assert.Equal(Staff, told.SentByUserKey);
        Assert.Equal("Hello", told.Subject);
        Assert.Equal("team-a", told.TeamKey);
        Assert.Equal(_mail.Outbox.Single.Id, told.MailId);
        Assert.False(string.IsNullOrWhiteSpace(told.Reason));
    }

    [Fact]
    public async Task AMailThatFailsAndIsTriedAgain_TellsNobodyUntilItIsGivenUp()
    {
        _mail.Mail.Respond = _ => MailSendResult.Failed("connection refused");
        await SentByAsync(Staff);

        Assert.Equal(UserMailStatus.Queued, _mail.Outbox.Single.Status);
        Assert.Empty(_mail.Sink.Told);
    }

    [Fact]
    public async Task AMailThatWasSent_TellsNobody()
    {
        await SentByAsync(Staff);

        Assert.Empty(_mail.Sink.Told);
    }

    [Fact]
    public async Task ASinkThatThrows_IsLogged_AndTheOthersAreStillTold_AndTheMailIsStillRecorded()
    {
        var broken = Substitute.For<IMailOutcomeSink>();
        broken.OnMailOutcomeAsync(Arg.Any<MailOutcome>(), Arg.Any<CancellationToken>()).Returns(_ => throw new InvalidOperationException("host sink is broken"));
        var notifier = new MailOutcomeNotifier([broken, _mail.Sink]);
        _mail.Recipients.Remove(UserMailFixture.UserKey);
        await _mail.Queue.QueueAsync(new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = UserMailFixture.News, Subject = "Hello", Body = "Hi." }, TestContext.Current.CancellationToken);
        var dispatcher = new UserMailDispatcher(_mail.Outbox, _mail.Categories, _mail.Preferences, _mail.Recipients, _mail.Mail, _mail.Policy, _mail.Footer(), _mail.Wrapped, _mail.Time, null, _mail.Template, null, notifier);

        await dispatcher.RunAsync("me", TestContext.Current.CancellationToken);

        Assert.Single(_mail.Sink.Told);
        Assert.Equal(UserMailStatus.Failed, _mail.Outbox.Single.Status);
    }

    [Fact]
    public async Task AMailTheQueueRefuses_TellsTheSinks_OnceForTheSameCauseTheSameDay()
    {
        var refused = new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = "no-such-category", Subject = "Hello", Body = "Hi.", SentByUserKey = Staff };

        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(refused, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => _mail.Queue.QueueAsync(refused, TestContext.Current.CancellationToken));

        var told = Assert.Single(_mail.Sink.Told);
        Assert.Equal(MailOutcomeKind.Refused, told.Outcome);
        Assert.Equal(Staff, told.SentByUserKey);
    }

    [Fact]
    public async Task ABounce_TellsTheSinks_Once_AReadAgainDoesNot()
    {
        var sent = await SentByAsync(Staff);
        var handler = new UserMailBounceHandler(_mail.Outbox, null, _mail.Notifier);
        var report = new MailBounce { OriginalMessageId = sent.MessageId, Reason = "550 no such user", ReceivedAt = UserMailFixture.Start.AddHours(1) };

        await handler.HandleAsync(report, TestContext.Current.CancellationToken);
        await handler.HandleAsync(report, TestContext.Current.CancellationToken);

        var told = Assert.Single(_mail.Sink.Told);
        Assert.Equal(MailOutcomeKind.Bounced, told.Outcome);
        Assert.Equal(Staff, told.SentByUserKey);
        Assert.Equal(sent.Id, told.MailId);
        Assert.Equal("550 no such user", told.Reason);
    }

    [Fact]
    public async Task ABounceForAMailNobodyKnows_TellsNobody()
    {
        var handler = new UserMailBounceHandler(_mail.Outbox, null, _mail.Notifier);

        var recognised = await handler.HandleAsync(new MailBounce { OriginalMessageId = "unknown@example.com", ReceivedAt = UserMailFixture.Start }, TestContext.Current.CancellationToken);

        Assert.False(recognised);
        Assert.Empty(_mail.Sink.Told);
    }

    // ---- one mail, by its id

    private UserMailHistoryService Service(string currentUserKey, params Claim[] claims)
    {
        var users = Substitute.For<IUserService>();
        users.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(_ =>
        {
            var user = Substitute.For<IUser>();
            user.Key.Returns(currentUserKey);
            return Task.FromResult(user);
        });

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Sam"), .. claims], "Test"));

        return new UserMailHistoryService(_mail.Queue, _mail.Recipients, new TeamAuthorizer(new FixedPrincipal(principal)), users,
            new CompositeAuditLogger([_mail.Audit], Options.Create(new AuditOptions())), new AuditEntryFactory(null));
    }

    private static Claim Manage => new(TeamClaimTypes.SystemScope, SystemUserScopes.Manage);

    [Fact]
    public async Task TheUserWhoSentAMail_ReadsIt_WithWhatItWasSentAs_AndTheirName()
    {
        _mail.Recipients.Add(new UserMailRecipient { Key = Staff, EMail = "sam@example.net", Name = "Sam Sender" });
        var sent = await SentByAsync(Staff);

        var item = await Service(Staff).GetMailAsync(sent.Id, TestContext.Current.CancellationToken);

        Assert.Equal(sent.Id, item.Id);
        Assert.Equal("Hello", item.Subject);
        Assert.Equal(UserMailStatus.Sent, item.Status);
        Assert.Equal("hello@example.com", item.FromAddress);
        Assert.Equal("support@example.com", item.ReplyTo);
        Assert.Equal(Staff, item.SentByUserKey);
        Assert.Equal("Sam Sender", item.SentByName);
    }

    [Fact]
    public async Task TheSender_ReadsAFailedMail_WithTheReason()
    {
        _mail.Recipients.Remove(UserMailFixture.UserKey);
        var queued = await _mail.Queue.QueueAsync(new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = UserMailFixture.News, Subject = "Hello", Body = "Hi.", SentByUserKey = Staff }, TestContext.Current.CancellationToken);
        await _mail.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);

        var item = await Service(Staff).GetMailAsync(queued.Id, TestContext.Current.CancellationToken);

        Assert.Equal(UserMailStatus.Failed, item.Status);
        Assert.False(string.IsNullOrWhiteSpace(item.Reason));
    }

    [Fact]
    public async Task StaffHoldingManage_ReadAMailTheyDidNotSend_AndItIsAudited()
    {
        var sent = await SentByAsync(Staff);

        var item = await Service(Stranger, Manage).GetMailAsync(sent.Id, TestContext.Current.CancellationToken);

        Assert.Equal(sent.Id, item.Id);
        Assert.Contains(_mail.Audit.Entries, x => x.Feature == UserMailAudit.Feature && x.Success && x.Metadata[UserMailAudit.MailId] == sent.Id);
    }

    [Fact]
    public async Task SomebodyWhoDidNotSendIt_IsRefused_AndTheRefusalIsAudited()
    {
        var sent = await SentByAsync(Staff);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(Stranger).GetMailAsync(sent.Id, TestContext.Current.CancellationToken));

        Assert.Contains(_mail.Audit.Entries, x => x.Feature == UserMailAudit.Feature && !x.Success);
    }

    [Fact]
    public async Task AMailWithNoRecordedSender_IsForStaffOnly()
    {
        var sent = await SentByAsync(null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(Staff).GetMailAsync(sent.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(await Service(Stranger, Manage).GetMailAsync(sent.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnIdThatNamesNothing_IsNoneForStaff_AndARefusalForEverybodyElse()
    {
        Assert.Null(await Service(Staff, Manage).GetMailAsync("no-such-mail", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(Staff).GetMailAsync("no-such-mail", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheGlobalLog_NamesWhoSentEachMail()
    {
        _mail.Recipients.Add(new UserMailRecipient { Key = Staff, EMail = "sam@example.net", Name = "Sam Sender" });
        await SentByAsync(Staff);

        var page = await Service(Stranger, Manage).ListAllAsync(null, null, 50, TestContext.Current.CancellationToken);

        var item = Assert.Single(page.Items);
        Assert.Equal(Staff, item.SentByUserKey);
        Assert.Equal("Sam Sender", item.SentByName);
        Assert.Equal("hello@example.com", item.FromAddress);
    }

    private sealed class FixedPrincipal(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
