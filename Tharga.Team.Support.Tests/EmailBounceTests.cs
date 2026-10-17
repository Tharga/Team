using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A bounce marks the answer it reports as not delivered, and is never read as somebody writing in.
/// </summary>
/// <remarks>
/// Before this a bounce was dropped as automated mail, so an answer the customer never received still read
/// as sent — and without the automated check it would have opened a case in a mail server's name.
/// </remarks>
public class EmailBounceTests
{
    private const string TeamKey = "acme";
    private const string CaseId = "case-1";
    private const string OpeningId = "opening-1@kommun.se";
    private const string AnswerId = "answer-2@fortdocs.se";
    private const string Reason = "554 5.7.23 SPF validation failed";

    [Fact]
    public async Task ABounceForAnAnswer_MarksThatAnswerFailed_WithTheReason()
    {
        var (handler, store, _, _) = Build();

        var outcome = await handler.HandleAsync(Bounce());

        Assert.True(outcome.WasApplied);
        Assert.True(outcome.Bounced);
        Assert.Equal(CaseId, outcome.CaseId);
        await store.Received(1).SetMessageDeliveryAsync(TeamKey, CaseId, 2, SupportMessageDelivery.Failed, Reason, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ABounce_IsNeitherAppendedNorOpensACase()
    {
        var (handler, store, _, _) = Build();

        await handler.HandleAsync(Bounce());

        await store.DidNotReceiveWithAnyArgs().AppendMessageAsync(default, default, default, default);
        await store.DidNotReceiveWithAnyArgs().AddCaseAsync(default, default, default);
    }

    [Fact]
    public async Task ABounce_IsAnnouncedAsADeliveryChange()
    {
        var (handler, _, notifier, _) = Build();

        await handler.HandleAsync(Bounce());

        notifier.Received(1).Notify(Arg.Is<SupportCaseUpdatedEventArgs>(x =>
            x.CaseId == CaseId && x.TeamKey == TeamKey && x.FromChannel && x.Change == SupportCaseChange.DeliveryChanged));
    }

    [Fact]
    public async Task ABounceForAMailNobodyHere_Sent_IsIgnored_AndOpensNoCase()
    {
        var (handler, store, notifier, _) = Build();

        var outcome = await handler.HandleAsync(Bounce() with { BouncedMessageId = "somebody-else@example.com" });

        Assert.False(outcome.WasApplied);
        await store.DidNotReceiveWithAnyArgs().AddCaseAsync(default, default, default);
        await store.DidNotReceiveWithAnyArgs().SetMessageDeliveryAsync(default, default, default, default, default, default);
        notifier.DidNotReceiveWithAnyArgs().Notify(default);
    }

    /// <summary>The opening mail's id finds the case, but names no answer of ours to mark.</summary>
    [Fact]
    public async Task ABounceForAnIdThatCarriedNoAnswer_IsIgnored()
    {
        var (handler, store, _, _) = Build();

        var outcome = await handler.HandleAsync(Bounce() with { BouncedMessageId = OpeningId });

        Assert.False(outcome.WasApplied);
        await store.DidNotReceiveWithAnyArgs().SetMessageDeliveryAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task ABounceThatGivesNoReason_StillSaysItCameBack()
    {
        var (handler, store, _, _) = Build();

        await handler.HandleAsync(Bounce() with { BounceReason = null });

        await store.Received(1).SetMessageDeliveryAsync(TeamKey, CaseId, 2, SupportMessageDelivery.Failed,
            Arg.Is<string>(x => !string.IsNullOrWhiteSpace(x)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheSameBounceTwice_IsAppliedOnce()
    {
        var (handler, store, _, ledger) = Build();
        ledger.TryRecordAsync(EmailEventHandler.Source, "E1tXyZa@s1.hostup.se", Arg.Any<CancellationToken>()).Returns(true, false);

        await handler.HandleAsync(Bounce());
        var second = await handler.HandleAsync(Bounce());

        Assert.False(second.WasApplied);
        await store.ReceivedWithAnyArgs(1).SetMessageDeliveryAsync(default, default, default, default, default, default);
    }

    /// <summary>The whole way: the mail as Exim sends it, read and applied.</summary>
    [Fact]
    public async Task AnEximReport_ReadFromTheMailbox_MarksTheAnswerFailed()
    {
        var (handler, store, _, _) = Build();

        var mail = InboundMailReader.Read(EximReport());
        await handler.HandleAsync(mail);

        await store.Received(1).SetMessageDeliveryAsync(TeamKey, CaseId, 2, SupportMessageDelivery.Failed, Reason, Arg.Any<CancellationToken>());
    }

    private static MimeKit.MimeMessage EximReport()
    {
        var report = new MimeKit.MultipartReport("delivery-status")
        {
            new MimeKit.TextPart("plain")
            {
                Text = "This message was created automatically by mail delivery software.\n\n" +
                       "A message that you sent could not be delivered to one or more of its\nrecipients. This is a permanent error. The following address(es) failed:\n\n" +
                       "  kund@kommun.se\n    host mx1.kommun.se [192.0.2.10]\n    SMTP error from remote mail server after RCPT TO:<kund@kommun.se>:\n    " + Reason + "\n"
            }
        };

        var status = new MimeKit.MessageDeliveryStatus();
        var header = new MimeKit.HeaderList { { "Reporting-MTA", "dns; s1.hostup.se" } };
        var recipient = new MimeKit.HeaderList
        {
            { "Action", "failed" },
            { "Final-Recipient", "rfc822;kund@kommun.se" },
            { "Status", "5.7.23" },
            { "Diagnostic-Code", "smtp; " + Reason }
        };
        status.StatusGroups.Add(header);
        status.StatusGroups.Add(recipient);
        report.Add(status);

        var original = new MimeKit.MimeMessage { Subject = "Re: Export is empty", MessageId = AnswerId, Body = new MimeKit.TextPart("plain") { Text = "Looking into it." } };
        original.From.Add(new MimeKit.MailboxAddress("FortDocs Support", "support@fortdocs.se"));
        original.To.Add(new MimeKit.MailboxAddress("", "kund@kommun.se"));
        report.Add(new MimeKit.MessagePart { Message = original });

        var message = new MimeKit.MimeMessage { Subject = "Mail delivery failed: returning message to sender", MessageId = "E1tXyZa@s1.hostup.se", Body = report };
        message.From.Add(new MimeKit.MailboxAddress("Mail Delivery System", "Mailer-Daemon@s1.hostup.se"));
        message.To.Add(new MimeKit.MailboxAddress("", "support@fortdocs.se"));
        message.Headers.Add("Return-Path", "<>");

        return message;
    }

    private static InboundMail Bounce() => new(
        MessageId: "E1tXyZa@s1.hostup.se",
        From: "mailer-daemon@s1.hostup.se",
        DeliveredTo: ["support@fortdocs.se"],
        Subject: "Mail delivery failed: returning message to sender",
        Body: "This message was created automatically by mail delivery software.",
        SentAt: new DateTimeOffset(2026, 10, 17, 8, 12, 3, TimeSpan.Zero),
        IsAutomated: true,
        BouncedMessageId: AnswerId,
        BounceReason: Reason);

    private static SupportCase Case() => new()
    {
        Id = CaseId,
        TeamKey = TeamKey,
        AuthorIdentity = null,
        AuthorName = "kund@kommun.se",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc),
        MessageCount = 2,
        Bindings =
        [
            new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = OpeningId, Address = "kund@kommun.se" },
            new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = AnswerId, Address = "kund@kommun.se", MessageSequence = 2 }
        ]
    };

    private static (EmailEventHandler Handler, ISupportCaseStore Store, ISupportCaseNotifier Notifier, ISupportEventLedger Ledger) Build()
    {
        var store = Substitute.For<ISupportCaseStore>();
        store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId, Arg.Any<CancellationToken>()).Returns(Case());
        store.GetCaseByBindingAsync(SupportChannelType.Email, AnswerId, Arg.Any<CancellationToken>()).Returns(Case());

        var ledger = Substitute.For<ISupportEventLedger>();
        ledger.TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var notifier = Substitute.For<ISupportCaseNotifier>();

        var options = new MailOptions { FromAddress = "support@fortdocs.se", Recipients = ["fortdocs.se"] };

        return (new EmailEventHandler(store, ledger, Options.Create(options), TimeProvider.System, notifier), store, notifier, ledger);
    }
}
