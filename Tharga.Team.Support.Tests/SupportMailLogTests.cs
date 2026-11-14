using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Every support answer mailed, and every receipt, is recorded in the mail log: sent with its <c>Message-ID</c> or failed with
/// the reason, with the From and Reply-To it went with and the case entry it belongs to. A resend writes over the same row,
/// a bounce marks it, and nothing about the log can stop or change an answer's delivery.
/// </summary>
public class SupportMailLogTests
{
    private const string TeamKey = "acme";
    private const string CaseId = "case-1";
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

    private static SupportChannelBinding Binding() => new()
    {
        ChannelType = SupportChannelType.Email,
        ExternalId = OpeningId,
        Address = Correspondent
    };

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

    private sealed record Channel(EmailSupportChannel Sut, IMailClient Mail, ISupportCaseStore Store, IUserMailOutbox Outbox);

    private static Channel Build(IUserMailOutbox outbox = null, bool withOutbox = true)
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

        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(new DateTimeOffset(Now));

        outbox ??= withOutbox ? Substitute.For<IUserMailOutbox>() : null;
        var options = new MailOptions { FromAddress = SupportAddress, FromName = "Example Support", PerCaseReplyTo = true };

        return new Channel(new EmailSupportChannel(mail, store, Options.Create(options), userMailOutbox: outbox, userService: users, timeProvider: time), mail, store, outbox);
    }

    private static UserMailRecord RecordedBy(IUserMailOutbox outbox)
        => (UserMailRecord)outbox.ReceivedCalls().Single(x => x.GetMethodInfo().Name == nameof(IUserMailOutbox.RecordAsync)).GetArguments()[0];

    [Fact]
    public async Task ASentAnswer_IsRecorded_WithTheHeadersItWentWith_TheStaffMember_AndTheCaseEntry()
    {
        var channel = Build();
        OutboundMail? sent = null;
        channel.Mail.SendAsync(Arg.Do<OutboundMail>(x => sent = x), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok(AnswerId));

        var result = await channel.Sut.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);

        Assert.True(result.Delivered);
        var record = RecordedBy(channel.Outbox);
        Assert.Equal((UserMailKind.SupportAnswer, UserMailStatus.Sent, AnswerId), (record.Kind, record.Status, record.MessageId));
        Assert.Null(record.Reason);
        Assert.Equal((Correspondent, "Re: Export is empty", TeamKey), (record.RecipientAddress, record.Subject, record.TeamKey));
        Assert.Equal(("user-2", "Sam Staff"), (record.SentByUserKey, record.SentByName));
        Assert.Equal((SupportAddress, "Example Support"), (record.Headers.FromAddress, record.Headers.FromName));
        Assert.NotNull(sent?.ReplyTo);
        Assert.Equal(sent?.ReplyTo, record.Headers.ReplyTo);
        Assert.Equal((CaseId, (int?)2), (record.CaseId, record.CaseMessageSequence));
        Assert.Equal(Now, record.At);
        Assert.Null(record.Category);
    }

    [Fact]
    public async Task ARefusedAnswer_IsRecordedFailed_WithTheReason()
    {
        var channel = Build();
        channel.Mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("550 5.7.1 Relaying denied"));

        var result = await channel.Sut.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);

        Assert.False(result.Delivered);
        var record = RecordedBy(channel.Outbox);
        Assert.Equal((UserMailStatus.Failed, "550 5.7.1 Relaying denied", (string)null), (record.Status, record.Reason, record.MessageId));
        Assert.Equal((CaseId, (int?)2), (record.CaseId, record.CaseMessageSequence));
    }

    [Fact]
    public async Task AnAnswerHeldBackForAMissingFile_IsRecordedFailed()
    {
        var channel = Build();
        var withFile = Answer() with { Attachments = [new SupportAttachment { Id = "gone", FileName = "gone.pdf", ContentType = "application/pdf", Size = 1 }] };

        var result = await channel.Sut.PostWithResultAsync(Binding(), withFile, TestContext.Current.CancellationToken);

        Assert.False(result.Delivered);
        var record = RecordedBy(channel.Outbox);
        Assert.Equal(UserMailStatus.Failed, record.Status);
        Assert.Equal(result.Error, record.Reason);
        await channel.Mail.DidNotReceiveWithAnyArgs().SendAsync(default);
    }

    [Fact]
    public async Task AnAssistantsAnswer_IsRecordedAsSentByNobody()
    {
        var channel = Build();

        await channel.Sut.PostWithResultAsync(Binding(), Answer() with { Kind = SupportMessageKind.Assistant, AuthorIdentity = null, AuthorName = "Assistant" }, TestContext.Current.CancellationToken);

        var record = RecordedBy(channel.Outbox);
        Assert.Equal(((string)null, (string)null), (record.SentByUserKey, record.SentByName));
    }

    [Fact]
    public async Task AnAnswerWrittenBySomeoneElse_KeepsTheirNameButNotTheCurrentUsersKey()
    {
        var channel = Build();

        await channel.Sut.PostWithResultAsync(Binding(), Answer() with { AuthorIdentity = "sub-9", AuthorName = "Other Staff" }, TestContext.Current.CancellationToken);

        var record = RecordedBy(channel.Outbox);
        Assert.Equal(((string)null, "Other Staff"), (record.SentByUserKey, record.SentByName));
    }

    [Fact]
    public async Task AResend_RecordsTheSameCaseEntryAgain_SoTheStoreUpdatesOneRow()
    {
        var channel = Build();
        channel.Mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("timeout"), MailSendResult.Ok(AnswerId));

        await channel.Sut.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);
        await channel.Sut.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);

        var records = channel.Outbox.ReceivedCalls().Where(x => x.GetMethodInfo().Name == nameof(IUserMailOutbox.RecordAsync)).Select(x => (UserMailRecord)x.GetArguments()[0]).ToArray();
        Assert.Equal(2, records.Length);
        Assert.All(records, x => Assert.Equal((UserMailKind.SupportAnswer, CaseId, (int?)2), (x.Kind, x.CaseId, x.CaseMessageSequence)));
        Assert.Equal([UserMailStatus.Failed, UserMailStatus.Sent], records.Select(x => x.Status));
    }

    [Fact]
    public async Task AMessageThatArrivedByMail_IsNotSent_AndSoNotRecorded()
    {
        var channel = Build();

        await channel.Sut.PostWithResultAsync(Binding(), Answer() with { Source = SupportChannelType.Email }, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(channel.Outbox.ReceivedCalls(), x => x.GetMethodInfo().Name == nameof(IUserMailOutbox.RecordAsync));
    }

    [Fact]
    public async Task WithNoOutbox_TheAnswerIsStillSent()
    {
        var channel = Build(withOutbox: false);

        var result = await channel.Sut.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);

        Assert.True(result.Delivered);
        await channel.Store.Received(1).AddBindingAsync(TeamKey, CaseId, Arg.Is<SupportChannelBinding>(x => x.ExternalId == AnswerId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ALogThatCannotBeWritten_NeitherFailsNorChangesTheAnswer()
    {
        var outbox = Substitute.For<IUserMailOutbox>();
        outbox.RecordAsync(Arg.Any<UserMailRecord>(), Arg.Any<CancellationToken>()).Returns<Task<string>>(_ => throw new InvalidOperationException("database down"));
        var channel = Build(outbox);

        var result = await channel.Sut.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);

        Assert.True(result.Delivered);
        await channel.Store.Received(1).AddBindingAsync(TeamKey, CaseId, Arg.Is<SupportChannelBinding>(x => x.ExternalId == AnswerId && x.MessageSequence == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedLogWrite_DoesNotTurnARefusalIntoSomethingElse()
    {
        var outbox = Substitute.For<IUserMailOutbox>();
        outbox.RecordAsync(Arg.Any<UserMailRecord>(), Arg.Any<CancellationToken>()).Returns<Task<string>>(_ => throw new InvalidOperationException("database down"));
        var channel = Build(outbox);
        channel.Mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("550 no such user"));

        var result = await channel.Sut.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);

        Assert.Equal((false, "550 no such user"), (result.Delivered, result.Error));
    }

    [Fact]
    public async Task TheChannelResolvedFromTheContainer_RecordsInTheRegisteredOutbox()
    {
        var outbox = Substitute.For<IUserMailOutbox>();
        var mail = Substitute.For<IMailClient>();
        mail.CanSend.Returns(true);
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok(AnswerId));
        var store = Substitute.For<ISupportCaseStore>();
        store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId, Arg.Any<CancellationToken>()).Returns(Case());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mail);
        services.AddSingleton(store);
        services.AddSingleton(outbox);
        services.AddThargaSupportCases(o =>
        {
            o.Email.Smtp.Host = "smtp.example.net";
            o.Email.FromAddress = SupportAddress;
        });

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var channel = scope.ServiceProvider.GetServices<ISupportChannel>().Single(x => x.ChannelType == SupportChannelType.Email);

        await channel.PostWithResultAsync(Binding(), Answer(), TestContext.Current.CancellationToken);

        Assert.Equal(UserMailKind.SupportAnswer, RecordedBy(outbox).Kind);
    }

    public class Bounce
    {
        private static InboundMail Report() => new(
            MessageId: "E1tXyZa@mx.example.net",
            From: "mailer-daemon@mx.example.net",
            DeliveredTo: [SupportAddress],
            Subject: "Mail delivery failed: returning message to sender",
            Body: "This message was created automatically by mail delivery software.",
            SentAt: new DateTimeOffset(Now),
            IsAutomated: true,
            BouncedMessageId: AnswerId,
            BounceReason: "554 5.7.23 SPF validation failed");

        private static (EmailEventHandler Handler, ISupportEventLedger Ledger, ISupportCaseStore Store) Build(IUserMailOutbox outbox)
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

            var time = Substitute.For<TimeProvider>();
            time.GetUtcNow().Returns(new DateTimeOffset(Now));

            var options = new MailOptions { FromAddress = SupportAddress, Recipients = ["example.net"] };

            return (new EmailEventHandler(store, ledger, Options.Create(options), time, userMailOutbox: outbox), ledger, store);
        }

        [Fact]
        public async Task ABouncedAnswer_MarksItsLogRowBounced_ByTheAnswersMessageId()
        {
            var outbox = Substitute.For<IUserMailOutbox>();
            var (handler, _, _) = Build(outbox);

            var outcome = await handler.HandleAsync(Report(), TestContext.Current.CancellationToken);

            Assert.True(outcome.Bounced);
            await outbox.Received(1).MarkBouncedAsync(AnswerId, "554 5.7.23 SPF validation failed", Now, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task TheSameBounceReadTwice_MarksTheRowOnce()
        {
            var outbox = Substitute.For<IUserMailOutbox>();
            var (handler, ledger, _) = Build(outbox);
            ledger.TryRecordAsync(EmailEventHandler.Source, "E1tXyZa@mx.example.net", Arg.Any<CancellationToken>()).Returns(true, false);

            await handler.HandleAsync(Report(), TestContext.Current.CancellationToken);
            var second = await handler.HandleAsync(Report(), TestContext.Current.CancellationToken);

            Assert.False(second.WasApplied);
            await outbox.ReceivedWithAnyArgs(1).MarkBouncedAsync(default, default, default, default);
        }

        [Fact]
        public async Task ALogThatCannotBeMarked_StillMarksTheAnswerNotDelivered()
        {
            var outbox = Substitute.For<IUserMailOutbox>();
            outbox.MarkBouncedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns<Task<UserMailBounceResult>>(_ => throw new InvalidOperationException("database down"));
            var (handler, _, store) = Build(outbox);

            var outcome = await handler.HandleAsync(Report(), TestContext.Current.CancellationToken);

            Assert.True(outcome.Bounced);
            await store.Received(1).SetMessageDeliveryAsync(TeamKey, CaseId, 2, SupportMessageDelivery.Failed, "554 5.7.23 SPF validation failed", Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task WithNoOutbox_ABounceIsAppliedAsBefore()
        {
            var (handler, _, store) = Build(null);

            var outcome = await handler.HandleAsync(Report(), TestContext.Current.CancellationToken);

            Assert.True(outcome.Bounced);
            await store.Received(1).SetMessageDeliveryAsync(TeamKey, CaseId, 2, SupportMessageDelivery.Failed, "554 5.7.23 SPF validation failed", Arg.Any<CancellationToken>());
        }
    }

    public class Receipt
    {
        private const string Visitor = "visitor@example.com";

        private static (ExternalSupportCaseService Service, IMailClient Mail, IUserMailOutbox Outbox) Build(IUserMailOutbox outbox = null, bool withOutbox = true)
        {
            var store = Substitute.For<ISupportCaseStore>();
            var mail = Substitute.For<IMailClient>();
            mail.CanSend.Returns(true);
            mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("receipt-1@example.net"));

            var time = Substitute.For<TimeProvider>();
            time.GetUtcNow().Returns(new DateTimeOffset(Now));

            outbox ??= withOutbox ? Substitute.For<IUserMailOutbox>() : null;
            var mailboxes = new SupportMailboxes(new MailOptions { FromAddress = SupportAddress, FromName = "Example Support" });

            return (new ExternalSupportCaseService(store, mailboxes, time, null, null, mail, new SupportExternalCaseOptions { SendReceipt = true }, null, outbox), mail, outbox);
        }

        private static ExternalSupportContact Contact() => new() { Name = "Vera Visitor", Email = Visitor };

        [Fact]
        public async Task ASentReceipt_IsRecorded_ForTheCase_FromNobody()
        {
            var (service, mail, outbox) = Build();
            OutboundMail? sent = null;
            mail.SendAsync(Arg.Do<OutboundMail>(x => sent = x), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("receipt-1@example.net"));

            var raised = await service.RaiseExternalCaseAsync(Contact(), "Question", "Hello", TestContext.Current.CancellationToken);

            var record = RecordedBy(outbox);
            Assert.Equal((UserMailKind.Receipt, UserMailStatus.Sent, "receipt-1@example.net"), (record.Kind, record.Status, record.MessageId));
            Assert.Equal((Visitor, "Vera Visitor", "Re: Question"), (record.RecipientAddress, record.RecipientName, record.Subject));
            Assert.Equal(((string)null, (string)null, (string)null), (record.SentByUserKey, record.SentByName, record.TeamKey));
            Assert.Equal((SupportAddress, "Example Support", sent?.ReplyTo), (record.Headers.FromAddress, record.Headers.FromName, record.Headers.ReplyTo));
            Assert.Equal((raised.Id, (int?)null), (record.CaseId, record.CaseMessageSequence));
            Assert.Equal(Now, record.At);
        }

        [Fact]
        public async Task ARefusedReceipt_IsRecordedFailed_WithTheReason()
        {
            var (service, mail, outbox) = Build();
            mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Failed("421 try later"));

            await service.RaiseExternalCaseAsync(Contact(), "Question", "Hello", TestContext.Current.CancellationToken);

            var record = RecordedBy(outbox);
            Assert.Equal((UserMailStatus.Failed, "421 try later", (string)null), (record.Status, record.Reason, record.MessageId));
        }

        [Fact]
        public async Task AReceiptThatThrows_IsRecordedFailed_AndTheCaseStands()
        {
            var (service, mail, outbox) = Build();
            mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns<MailSendResult>(_ => throw new InvalidOperationException("smtp down"));

            var raised = await service.RaiseExternalCaseAsync(Contact(), "Question", "Hello", TestContext.Current.CancellationToken);

            Assert.NotNull(raised);
            Assert.Equal((UserMailStatus.Failed, "smtp down"), (RecordedBy(outbox).Status, RecordedBy(outbox).Reason));
        }

        [Fact]
        public async Task ALogThatCannotBeWritten_NeverCostsTheCase()
        {
            var outbox = Substitute.For<IUserMailOutbox>();
            outbox.RecordAsync(Arg.Any<UserMailRecord>(), Arg.Any<CancellationToken>()).Returns<Task<string>>(_ => throw new InvalidOperationException("database down"));
            var (service, _, _) = Build(outbox);

            Assert.NotNull(await service.RaiseExternalCaseAsync(Contact(), "Question", "Hello", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task WithNoOutbox_TheReceiptIsStillSent()
        {
            var (service, mail, _) = Build(withOutbox: false);

            await service.RaiseExternalCaseAsync(Contact(), "Question", "Hello", TestContext.Current.CancellationToken);

            await mail.Received(1).SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task TheRegisteredService_RecordsReceiptsInTheRegisteredOutbox()
        {
            var outbox = Substitute.For<IUserMailOutbox>();
            var mail = Substitute.For<IMailClient>();
            mail.CanSend.Returns(true);
            mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("receipt-1@example.net"));

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddThargaSupportCases(o =>
            {
                o.External.SendReceipt = true;
                o.Email.FromAddress = SupportAddress;
            });
            services.AddSingleton(Substitute.For<ISupportCaseStore>());
            services.AddSingleton(mail);
            services.AddSingleton(outbox);
            services.AddSingleton(new CompositeAuditLogger([], Options.Create(new AuditOptions())));
            services.AddSingleton<IAuditEntryFactory>(new AuditEntryFactory(null));

            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();

            await scope.ServiceProvider.GetRequiredService<IExternalSupportCaseService>().RaiseExternalCaseAsync(Contact(), "Question", "Hello", TestContext.Current.CancellationToken);

            Assert.Equal(UserMailKind.Receipt, RecordedBy(outbox).Kind);
        }
    }
}
