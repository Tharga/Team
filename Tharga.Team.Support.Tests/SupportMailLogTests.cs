using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Support's own mail in the mail log: an answer and a contact-form receipt are listed, as their own kinds, with the address they went
/// from, the reply address they carried, who sent them and the case they belong to; the log says what the case says; and a mail log that
/// fails never costs the answer.
/// </summary>
/// <remarks>Addresses are made up, on more than one domain.</remarks>
public class SupportMailLogTests
{
    private const string TeamKey = "team-a";
    private const string CaseId = "case-1";
    private const string Customer = "user@customer.example";
    private const string Main = "support@example.net";
    private const string Staff = "staff-user-1";
    private static readonly DateTime Opened = new(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);

    private readonly InMemorySupportCaseStore _store = new();
    private readonly IMailClient _mail = Substitute.For<IMailClient>();
    private readonly IUserMailOutbox _log = Substitute.For<IUserMailOutbox>();
    private readonly List<(UserMail Mail, UserMailStatus Status, string Reason, string MessageId, UserMailRoute Route)> _rows = [];

    public SupportMailLogTests()
    {
        _mail.CanSend.Returns(true);
        _mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("answer-1@example.net"));
        _log.AddCompletedAsync(Arg.Any<UserMail>(), Arg.Any<UserMailStatus>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<UserMailRoute>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                _rows.Add((x.ArgAt<UserMail>(0), x.ArgAt<UserMailStatus>(1), x.ArgAt<string>(2), x.ArgAt<string>(3), x.ArgAt<UserMailRoute>(5)));
                return Task.FromResult("row-" + _rows.Count);
            });
    }

    private EmailSupportChannel Channel(bool withLog = true, bool withUser = true)
    {
        var users = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns(Staff);
        users.GetCurrentUserAsync(Arg.Any<System.Security.Claims.ClaimsPrincipal>()).Returns(Task.FromResult(user));

        var options = new MailOptions { FromAddress = Main, FromName = "Example Support", PerCaseReplyTo = true };

        return new EmailSupportChannel(_mail, _store, Options.Create(options), mailLog: withLog ? _log : null, userService: withUser ? users : null);
    }

    private async Task<SupportChannelBinding> CaseAsync()
    {
        await _store.AddCaseAsync(
            new SupportCase
            {
                Id = CaseId,
                TeamKey = TeamKey,
                AuthorIdentity = null,
                AuthorName = "A Customer",
                Subject = "Export is empty",
                Status = SupportCaseStatus.Open,
                CreatedAt = Opened,
                MessageCount = 1,
                Bindings = [new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = "opening-1@customer.example", Address = Customer, MailboxAddress = Main }]
            },
            new SupportMessage { Sequence = 1, Kind = SupportMessageKind.User, AuthorName = "A Customer", Body = "The export is empty.", SentAt = Opened, Source = SupportChannelType.Email, Delivery = SupportMessageDelivery.Sent });

        return (await _store.GetCaseAsync(TeamKey, CaseId)).Bindings[0];
    }

    private static SupportMessage Answer(int sequence = 2) => new()
    {
        Sequence = sequence,
        Kind = SupportMessageKind.User,
        AuthorName = "Anna Andersson",
        Body = "Try again.",
        SentAt = Opened.AddHours(1),
        AsSupport = true
    };

    [Fact]
    public async Task AnAnswer_IsListedAsASupportAnswer_WithWhoSentIt_AndTheCase()
    {
        var binding = await CaseAsync();

        await Channel().PostWithResultAsync(binding, Answer());

        var row = Assert.Single(_rows);
        Assert.Equal(UserMailKind.SupportAnswer, row.Mail.Kind);
        Assert.Equal(UserMailStatus.Sent, row.Status);
        Assert.Equal(CaseId, row.Mail.CaseId);
        Assert.Equal(TeamKey, row.Mail.TeamKey);
        Assert.Equal(Staff, row.Mail.SentByUserKey);
        Assert.Equal(Customer, row.Mail.RecipientAddress);
        Assert.Equal("Re: Export is empty", row.Mail.Subject);
        Assert.Equal("answer-1@example.net", row.MessageId);
    }

    [Fact]
    public async Task AnAnswer_RecordsTheAddressItWentFrom_AndTheReplyAddressItCarried()
    {
        var binding = await CaseAsync();

        await Channel().PostWithResultAsync(binding, Answer());

        var row = Assert.Single(_rows);
        Assert.Equal(Main, row.Route.From);
        Assert.Matches(@"^support\+[^@]+@example\.net$", row.Route.ReplyTo);
    }

    [Fact]
    public async Task AnAnswerThatDidNotGo_IsListedAsFailed_WithTheReason()
    {
        var binding = await CaseAsync();
        _mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("connection refused"));

        var result = await Channel().PostWithResultAsync(binding, Answer());

        Assert.False(result.Delivered);
        var row = Assert.Single(_rows);
        Assert.Equal(UserMailStatus.Failed, row.Status);
        Assert.Equal("connection refused", row.Reason);
    }

    [Fact]
    public async Task SendingTheSameAnswerAgain_IsOneRowKey_SoTheLogSaysWhatTheCaseSays()
    {
        var binding = await CaseAsync();
        _mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("connection refused"), MailSendResult.Ok("answer-2@example.net"));
        var channel = Channel();

        await channel.PostWithResultAsync(binding, Answer(2));
        await channel.PostWithResultAsync(binding, Answer(2));
        await channel.PostWithResultAsync(binding, Answer(3));

        Assert.Equal(["support:case-1:2", "support:case-1:2", "support:case-1:3"], _rows.Select(x => x.Mail.DeduplicationKey));
        Assert.Equal([UserMailStatus.Failed, UserMailStatus.Sent, UserMailStatus.Sent], _rows.Select(x => x.Status));
    }

    [Fact]
    public async Task AMailLogThatFails_NeverCostsTheAnswer()
    {
        var binding = await CaseAsync();
        _log.AddCompletedAsync(default, default, default, default, default, default, default, default).ReturnsForAnyArgs<Task<string>>(_ => throw new InvalidOperationException("the log store is down"));

        var result = await Channel().PostWithResultAsync(binding, Answer());

        Assert.True(result.Delivered);
        await _mail.Received(1).SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoMailLogRegistered_AnswersGoOutAsBefore()
    {
        var binding = await CaseAsync();

        var result = await Channel(withLog: false).PostWithResultAsync(binding, Answer());

        Assert.True(result.Delivered);
        Assert.Empty(_rows);
    }

    [Fact]
    public async Task WithNoUserToHand_TheRowIsStillWritten_WithoutASender()
    {
        var binding = await CaseAsync();

        await Channel(withUser: false).PostWithResultAsync(binding, Answer());

        Assert.Null(Assert.Single(_rows).Mail.SentByUserKey);
    }

    [Fact]
    public async Task AMailFromTheCustomer_IsNotLogged_ItWasNeverSent()
    {
        var binding = await CaseAsync();

        await Channel().PostWithResultAsync(binding, Answer() with { Source = SupportChannelType.Email });

        Assert.Empty(_rows);
    }

    // ---- the contact form's receipt

    private (ExternalSupportCaseService Service, IMailClient Client) Receipt(bool logFails = false)
    {
        var store = Substitute.For<ISupportCaseStore>();
        var client = Substitute.For<IMailClient>();
        client.CanSend.Returns(true);
        client.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("receipt@example.net"));
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(new DateTimeOffset(Opened));
        if (logFails) _log.AddCompletedAsync(default, default, default, default, default, default, default, default).ReturnsForAnyArgs<Task<string>>(_ => throw new InvalidOperationException("the log store is down"));

        return (new ExternalSupportCaseService(store, new SupportMailboxes(new MailOptions { FromAddress = Main }), time, mailClient: client, options: new SupportExternalCaseOptions { SendReceipt = true }, mailLog: _log), client);
    }

    [Fact]
    public async Task TheReceiptOfAContactForm_IsListedAsASupportReceipt_PointingBackToTheCase()
    {
        var (service, _) = Receipt();

        var raised = await service.RaiseExternalCaseAsync(new ExternalSupportContact { Email = "vera@sample.test", Name = "Vera" }, "Question", "Can you help?");

        var row = Assert.Single(_rows);
        Assert.Equal(UserMailKind.SupportReceipt, row.Mail.Kind);
        Assert.Equal(UserMailStatus.Sent, row.Status);
        Assert.Equal(raised.Id, row.Mail.CaseId);
        Assert.Equal("vera@sample.test", row.Mail.RecipientAddress);
        Assert.Equal(Main, row.Route.From);
        Assert.Null(row.Mail.SentByUserKey);
    }

    [Fact]
    public async Task AReceiptThatFailed_IsListedAsFailed()
    {
        var (service, client) = Receipt();
        client.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("mailbox full"));

        await service.RaiseExternalCaseAsync(new ExternalSupportContact { Email = "vera@sample.test" }, "Question", "Can you help?");

        var row = Assert.Single(_rows);
        Assert.Equal(UserMailStatus.Failed, row.Status);
        Assert.Equal("mailbox full", row.Reason);
    }

    [Fact]
    public async Task AMailLogThatFails_NeverCostsTheCase()
    {
        var (service, _) = Receipt(logFails: true);

        var raised = await service.RaiseExternalCaseAsync(new ExternalSupportContact { Email = "vera@sample.test" }, "Question", "Can you help?");

        Assert.NotNull(raised);
    }

    // ---- a bounce

    private static InboundMail Bounce(string bouncedId) => new(
        MessageId: "report-1@mx.example.net",
        From: "mailer-daemon@mx.example.net",
        DeliveredTo: [Main],
        Subject: "Undelivered Mail Returned to Sender",
        Body: "This is the mail system.",
        SentAt: new DateTimeOffset(Opened),
        IsAutomated: true,
        BouncedMessageId: bouncedId,
        BounceReason: "550 no such user");

    private (EmailEventHandler Handler, RecordingSink Sink) BounceHandler(UserMailBounceOutcome outcome)
    {
        var store = Substitute.For<ISupportCaseStore>();
        store.GetCaseByBindingAsync(SupportChannelType.Email, "answer-1@example.net", Arg.Any<CancellationToken>()).Returns(new SupportCase
        {
            Id = CaseId,
            TeamKey = TeamKey,
            AuthorIdentity = null,
            AuthorName = "A Customer",
            Subject = "Export is empty",
            Status = SupportCaseStatus.Open,
            CreatedAt = Opened,
            MessageCount = 2,
            Bindings = [new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = "answer-1@example.net", Address = Customer, MessageSequence = 2 }]
        });
        var ledger = Substitute.For<ISupportEventLedger>();
        ledger.TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(new DateTimeOffset(Opened));
        _log.MarkBouncedAsync("answer-1@example.net", Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(new UserMailBounceResult(outcome, "row-1"));
        _log.GetAsync("row-1", Arg.Any<CancellationToken>()).Returns(new UserMailItem
        {
            Id = "row-1",
            Mail = new UserMail { Category = "support", Subject = "Re: Export is empty", Kind = UserMailKind.SupportAnswer, SentByUserKey = Staff, CaseId = CaseId },
            Status = UserMailStatus.Bounced,
            QueuedAt = Opened
        });

        var sink = new RecordingSink();
        var handler = new EmailEventHandler(store, ledger, Options.Create(new MailOptions { FromAddress = Main, Recipients = ["example.net"] }), time, mailLog: _log, outcomes: new MailOutcomeNotifier([sink]));

        return (handler, sink);
    }

    [Fact]
    public async Task ABounceOfAnAnswer_MarksTheLogRowBounced_AndTellsTheSinksOnce()
    {
        var (handler, sink) = BounceHandler(UserMailBounceOutcome.Bounced);

        await handler.HandleAsync(Bounce("answer-1@example.net"));

        await _log.Received(1).MarkBouncedAsync("answer-1@example.net", "550 no such user", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        var told = Assert.Single(sink.Told);
        Assert.Equal(MailOutcomeKind.Bounced, told.Outcome);
        Assert.Equal(UserMailKind.SupportAnswer, told.Kind);
        Assert.Equal(Staff, told.SentByUserKey);
        Assert.Equal(CaseId, told.CaseId);
        Assert.Equal(TeamKey, told.TeamKey);
    }

    [Fact]
    public async Task ABounceReadAgain_ChangesTheRowNoMore_AndTellsNobody()
    {
        var (handler, sink) = BounceHandler(UserMailBounceOutcome.AlreadyBounced);

        await handler.HandleAsync(Bounce("answer-1@example.net"));

        Assert.Empty(sink.Told);
    }

    [Fact]
    public async Task AMailLogThatCannotBeUpdated_DoesNotUndoWhatTheCaseNowSays()
    {
        var (handler, _) = BounceHandler(UserMailBounceOutcome.Bounced);
        _log.MarkBouncedAsync(default, default, default, default).ReturnsForAnyArgs<Task<UserMailBounceResult>>(_ => throw new InvalidOperationException("the log store is down"));

        var outcome = await handler.HandleAsync(Bounce("answer-1@example.net"));

        Assert.True(outcome.Bounced);
    }

    private sealed class RecordingSink : IMailOutcomeSink
    {
        public List<MailOutcome> Told { get; } = [];

        public Task OnMailOutcomeAsync(MailOutcome outcome, CancellationToken cancellationToken = default)
        {
            Told.Add(outcome);

            return Task.CompletedTask;
        }
    }
}
