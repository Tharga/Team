using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Email.Users;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Every registered outcome sink is told when a support answer or a receipt is recorded as failed, each failed attempt
/// again, and when an answer's row in the mail log turns bounced, once however often the bounce is read. A sink that
/// throws never changes the answer.
/// </summary>
public class SupportMailOutcomeTests
{
    private const string TeamKey = "acme";
    private const string CaseId = "case-1";
    private const string RowId = "row-1";
    private const string Correspondent = "kund@customer.example";
    private const string OpeningId = "opening-1@customer.example";
    private const string AnswerId = "answer-2@example.net";
    private const string SupportAddress = "support@example.net";
    private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private static SupportCase Case() => new()
    {
        Id = CaseId,
        TeamKey = TeamKey,
        AuthorIdentity = "sub-1",
        AuthorName = "Kim Kund",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = Now.AddHours(-1),
        MessageCount = 2,
        TrackingToken = "tracking-abc"
    };

    private static SupportChannelBinding Binding() => new() { ChannelType = SupportChannelType.Email, ExternalId = OpeningId, Address = Correspondent };

    private static SupportMessage Answer() => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "sub-2",
        AuthorName = "Sam Staff",
        Body = "Looking into it.",
        SentAt = Now,
        AsSupport = true
    };

    private static TimeProvider Clock()
    {
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(new DateTimeOffset(Now));
        return time;
    }

    private static IUserMailOutbox Outbox()
    {
        var outbox = Substitute.For<IUserMailOutbox>();
        outbox.RecordAsync(Arg.Any<UserMailRecord>(), Arg.Any<CancellationToken>()).Returns(RowId);
        return outbox;
    }

    private static (EmailSupportChannel Sut, IMailClient Mail) Channel(MailOutcomeNotifier outcomes, IUserMailOutbox outbox = null)
    {
        var mail = Substitute.For<IMailClient>();
        mail.CanSend.Returns(true);
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok(AnswerId));

        var store = Substitute.For<ISupportCaseStore>();
        store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId, Arg.Any<CancellationToken>()).Returns(Case());

        var user = Substitute.For<IUser>();
        user.Key.Returns("user-2");
        user.Identity.Returns("sub-2");
        var users = Substitute.For<IUserService>();
        users.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(Task.FromResult(user));

        var options = new MailOptions { FromAddress = SupportAddress, FromName = "Example Support" };

        return (new EmailSupportChannel(mail, store, Options.Create(options), userMailOutbox: outbox ?? Outbox(), userService: users, timeProvider: Clock(), mailOutcomes: outcomes), mail);
    }

    [Fact]
    public async Task AFailedAnswer_TellsTheSinks_WithTheCaseEntry_TheStaffMember_AndTheRow()
    {
        var sink = new Sink();
        var (channel, mail) = Channel(new MailOutcomeNotifier([sink]));
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("550 5.7.1 Relaying denied"));

        await channel.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);

        var outcome = Assert.Single(sink.Told);
        Assert.Equal((RowId, UserMailKind.SupportAnswer, UserMailStatus.Failed, "550 5.7.1 Relaying denied"), (outcome.Id, outcome.Kind, outcome.Status, outcome.Reason));
        Assert.Equal((CaseId, (int?)2, TeamKey, "user-2"), (outcome.CaseId, outcome.CaseMessageSequence, outcome.TeamKey, outcome.SentByUserKey));
        Assert.Equal(Now, outcome.At);
    }

    [Fact]
    public async Task ASentAnswer_TellsNobody()
    {
        var sink = new Sink();
        var (channel, _) = Channel(new MailOutcomeNotifier([sink]));

        await channel.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);

        Assert.Empty(sink.Told);
    }

    [Fact]
    public async Task AResendThatFailsAgain_TellsTheSinksAgain()
    {
        var sink = new Sink();
        var (channel, mail) = Channel(new MailOutcomeNotifier([sink]));
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("timeout"), MailSendResult.Failed("timeout again"));

        await channel.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);
        await channel.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);

        Assert.Equal(["timeout", "timeout again"], sink.Told.Select(x => x.Reason));
        Assert.All(sink.Told, x => Assert.Equal((RowId, CaseId, (int?)2), (x.Id, x.CaseId, x.CaseMessageSequence)));
    }

    [Fact]
    public async Task AResendThatSucceeds_TellsNobodyMore()
    {
        var sink = new Sink();
        var (channel, mail) = Channel(new MailOutcomeNotifier([sink]));
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("timeout"), MailSendResult.Ok(AnswerId));

        await channel.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);
        await channel.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);

        Assert.Equal("timeout", Assert.Single(sink.Told).Reason);
    }

    [Fact]
    public async Task ASinkThatThrows_NeitherStopsTheOthers_NorChangesTheAnswer()
    {
        var sink = new Sink();
        var broken = Substitute.For<IMailOutcomeSink>();
        broken.OnMailOutcomeAsync(Arg.Any<MailOutcome>(), Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("sink down"));
        var (channel, mail) = Channel(new MailOutcomeNotifier([broken, sink]));
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("550 no such user"));

        var result = await channel.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);

        Assert.Equal((false, "550 no such user"), (result.Delivered, result.Error));
        Assert.Single(sink.Told);
    }

    [Fact]
    public async Task WithNoSinks_AFailedAnswerIsRecordedAsBefore()
    {
        var outbox = Outbox();
        var (channel, mail) = Channel(new MailOutcomeNotifier([]), outbox);
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("550 no such user"));

        var result = await channel.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);

        Assert.False(result.Delivered);
        await outbox.Received(1).RecordAsync(Arg.Is<UserMailRecord>(x => x.Status == UserMailStatus.Failed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheChannelResolvedFromTheContainer_TellsTheRegisteredSinks()
    {
        var mail = Substitute.For<IMailClient>();
        mail.CanSend.Returns(true);
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("550 no such user"));
        var store = Substitute.For<ISupportCaseStore>();
        store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId, Arg.Any<CancellationToken>()).Returns(Case());
        var sink = new Sink();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mail);
        services.AddSingleton(store);
        services.AddSingleton(Outbox());
        services.AddSingleton<IMailOutcomeSink>(sink);
        services.AddThargaSupportCases(o =>
        {
            o.Email.Smtp.Host = "smtp.example.net";
            o.Email.FromAddress = SupportAddress;
        });

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var channel = scope.ServiceProvider.GetServices<ISupportChannel>().Single(x => x.ChannelType == SupportChannelType.Email);

        await channel.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);

        Assert.Equal(UserMailKind.SupportAnswer, Assert.Single(sink.Told).Kind);
    }

    public class Bounce
    {
        private static InboundMail Report(string messageId = "E1tXyZa@mx.example.net") => new(
            MessageId: messageId,
            From: "mailer-daemon@mx.example.net",
            DeliveredTo: [SupportAddress],
            Subject: "Mail delivery failed: returning message to sender",
            Body: "This message was created automatically by mail delivery software.",
            SentAt: new DateTimeOffset(Now),
            IsAutomated: true,
            BouncedMessageId: AnswerId,
            BounceReason: "554 5.7.23 SPF validation failed");

        private static EmailEventHandler Handler(IUserMailOutbox outbox, MailOutcomeNotifier outcomes)
        {
            var supportCase = Case() with
            {
                Bindings =
                [
                    new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = OpeningId, Address = Correspondent },
                    new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = AnswerId, Address = Correspondent, MessageSequence = 2 }
                ]
            };
            var store = Substitute.For<ISupportCaseStore>();
            store.GetCaseByBindingAsync(SupportChannelType.Email, AnswerId, Arg.Any<CancellationToken>()).Returns(supportCase);

            var ledger = Substitute.For<ISupportEventLedger>();
            ledger.TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

            var options = new MailOptions { FromAddress = SupportAddress, Recipients = ["example.net"] };

            return new EmailEventHandler(store, ledger, Options.Create(options), Clock(), userMailOutbox: outbox, mailOutcomes: outcomes);
        }

        private static IUserMailOutbox BouncingOutbox()
        {
            var outbox = Substitute.For<IUserMailOutbox>();
            outbox.MarkBouncedAsync(AnswerId, Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(new UserMailBounceResult(UserMailBounceOutcome.Bounced, RowId), new UserMailBounceResult(UserMailBounceOutcome.AlreadyBounced, RowId));
            outbox.GetAsync(RowId, Arg.Any<CancellationToken>()).Returns(new UserMailItem
            {
                Id = RowId,
                Mail = new UserMail { RecipientAddress = Correspondent, Category = null!, Subject = "Re: Export is empty", Kind = UserMailKind.SupportAnswer, TeamKey = TeamKey, SentByUserKey = "user-2" },
                Status = UserMailStatus.Bounced,
                QueuedAt = Now.AddMinutes(-5),
                CaseId = CaseId,
                CaseMessageSequence = 2
            });
            return outbox;
        }

        [Fact]
        public async Task ABouncedAnswer_TellsTheSinks_WithTheCaseEntry_AndWhoSentIt()
        {
            var sink = new Sink();

            await Handler(BouncingOutbox(), new MailOutcomeNotifier([sink])).HandleAsync(Report(), TestContext.Current.CancellationToken);

            var outcome = Assert.Single(sink.Told);
            Assert.Equal((RowId, UserMailKind.SupportAnswer, UserMailStatus.Bounced, "554 5.7.23 SPF validation failed"), (outcome.Id, outcome.Kind, outcome.Status, outcome.Reason));
            Assert.Equal((CaseId, (int?)2, TeamKey, "user-2"), (outcome.CaseId, outcome.CaseMessageSequence, outcome.TeamKey, outcome.SentByUserKey));
            Assert.Equal(Now, outcome.At);
        }

        [Fact]
        public async Task TheSameBounceReadTwice_OrOnTwoInstances_TellsTheSinksOnce()
        {
            var sink = new Sink();
            var outcomes = new MailOutcomeNotifier([sink]);
            var outbox = BouncingOutbox();

            await Handler(outbox, outcomes).HandleAsync(Report(), TestContext.Current.CancellationToken);
            await Handler(outbox, outcomes).HandleAsync(Report("E1tXyZb@mx2.example.net"), TestContext.Current.CancellationToken);

            Assert.Single(sink.Told);
        }

        [Fact]
        public async Task ARowThatCannotBeReadBack_StillTellsTheSinks_WhatTheCaseKnows()
        {
            var sink = new Sink();
            var outbox = BouncingOutbox();
            outbox.GetAsync(RowId, Arg.Any<CancellationToken>()).Returns<Task<UserMailItem>>(_ => throw new InvalidOperationException("database down"));

            await Handler(outbox, new MailOutcomeNotifier([sink])).HandleAsync(Report(), TestContext.Current.CancellationToken);

            var outcome = Assert.Single(sink.Told);
            Assert.Equal((RowId, UserMailKind.SupportAnswer, CaseId, (int?)2, TeamKey), (outcome.Id, outcome.Kind, outcome.CaseId, outcome.CaseMessageSequence, outcome.TeamKey));
            Assert.Null(outcome.SentByUserKey);
        }
    }

    public class Receipt
    {
        private static ExternalSupportCaseService Service(IMailClient mail, MailOutcomeNotifier outcomes)
        {
            var mailboxes = new SupportMailboxes(new MailOptions { FromAddress = SupportAddress, FromName = "Example Support" });

            return new ExternalSupportCaseService(Substitute.For<ISupportCaseStore>(), mailboxes, Clock(), null, null, mail, new SupportExternalCaseOptions { SendReceipt = true }, null, Outbox(), outcomes);
        }

        private static ExternalSupportContact Contact() => new() { Name = "Vera Visitor", Email = "visitor@example.com" };

        [Fact]
        public async Task AFailedReceipt_TellsTheSinks_WithItsCase_AndNoSender()
        {
            var sink = new Sink();
            var mail = Substitute.For<IMailClient>();
            mail.CanSend.Returns(true);
            mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("421 try later"));

            var raised = await Service(mail, new MailOutcomeNotifier([sink])).RaiseExternalCaseAsync(Contact(), "Question", "Hello", TestContext.Current.CancellationToken);

            var outcome = Assert.Single(sink.Told);
            Assert.Equal((RowId, UserMailKind.Receipt, UserMailStatus.Failed, "421 try later"), (outcome.Id, outcome.Kind, outcome.Status, outcome.Reason));
            Assert.Equal((raised.Id, (int?)null, (string)null, (string)null), (outcome.CaseId, outcome.CaseMessageSequence, outcome.SentByUserKey, outcome.TeamKey));
        }

        [Fact]
        public async Task ASentReceipt_TellsNobody()
        {
            var sink = new Sink();
            var mail = Substitute.For<IMailClient>();
            mail.CanSend.Returns(true);
            mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("receipt-1@example.net"));

            await Service(mail, new MailOutcomeNotifier([sink])).RaiseExternalCaseAsync(Contact(), "Question", "Hello", TestContext.Current.CancellationToken);

            Assert.Empty(sink.Told);
        }

        [Fact]
        public async Task TheRegisteredService_TellsTheRegisteredSinks()
        {
            var mail = Substitute.For<IMailClient>();
            mail.CanSend.Returns(true);
            mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("421 try later"));
            var sink = new Sink();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddThargaSupportCases(o =>
            {
                o.External.SendReceipt = true;
                o.Email.FromAddress = SupportAddress;
            });
            services.AddSingleton(Substitute.For<ISupportCaseStore>());
            services.AddSingleton(mail);
            services.AddSingleton(Outbox());
            services.AddSingleton<IMailOutcomeSink>(sink);
            services.AddSingleton(new CompositeAuditLogger([], Options.Create(new AuditOptions())));
            services.AddSingleton<IAuditEntryFactory>(new AuditEntryFactory(null));

            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();

            await scope.ServiceProvider.GetRequiredService<IExternalSupportCaseService>().RaiseExternalCaseAsync(Contact(), "Question", "Hello", TestContext.Current.CancellationToken);

            Assert.Equal(UserMailKind.Receipt, Assert.Single(sink.Told).Kind);
        }
    }

    private sealed class Sink : IMailOutcomeSink
    {
        private readonly List<MailOutcome> _told = [];

        public IReadOnlyList<MailOutcome> Told => _told;

        public Task OnMailOutcomeAsync(MailOutcome outcome, CancellationToken cancellationToken)
        {
            _told.Add(outcome);
            return Task.CompletedTask;
        }
    }
}
