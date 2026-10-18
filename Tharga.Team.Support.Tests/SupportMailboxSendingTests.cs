using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Service.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// An answer goes out from the mailbox its case came in through, so the customer's reply comes back to the address
/// they wrote to; and a bounce of that answer finds it whichever mailbox the bounce arrives in.
/// </summary>
public class SupportMailboxSendingTests
{
    private const string TeamKey = "acme";
    private const string CaseId = "case-1";
    private const string Hosted = "hosted";
    private const string OwnAddress = "support@example.com";
    private const string HostedAddress = "help@example.net";
    private const string Customer = "customer@example.org";
    private const string OpeningId = "opening-1@example.org";

    private readonly IMailClient _own = Sender();
    private readonly IMailClient _hosted = Sender();
    private readonly ISupportCaseStore _store = Substitute.For<ISupportCaseStore>();
    private readonly CapturingLogger<EmailSupportChannel> _log = new();

    [Fact]
    public async Task AnAnswer_GoesOutThroughTheMailboxItsCaseCameInThrough()
    {
        var result = await Channel().PostWithResultAsync(Binding(Hosted), Answer());

        Assert.True(result.Delivered);
        await _hosted.Received(1).SendAsync(Arg.Is<OutboundMail>(x => x.To == Customer && x.InReplyTo == OpeningId), Arg.Any<CancellationToken>());
        await _own.DidNotReceiveWithAnyArgs().SendAsync(default, default);
    }

    /// <summary>The per-case reply address is built from the mailbox the answer goes out from, and only when that mailbox asks for it.</summary>
    [Fact]
    public async Task ThePerCaseReplyAddress_IsTheCasesMailboxs()
    {
        await Channel().PostWithResultAsync(Binding(Hosted), Answer());
        await Channel().PostWithResultAsync(Binding(null), Answer());

        await _hosted.Received(1).SendAsync(Arg.Is<OutboundMail>(x => x.ReplyTo == $"help+{CaseId}@example.net"), Arg.Any<CancellationToken>());
        await _own.Received(1).SendAsync(Arg.Is<OutboundMail>(x => x.ReplyTo == null), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SupportCaseOptions.PrimaryMailboxName)]
    public async Task ACaseOfThePrimary_OrOneStoredBeforeMailboxesWereNamed_IsAnsweredFromThePrimary(string mailbox)
    {
        await Channel().PostWithResultAsync(Binding(mailbox), Answer());

        await _own.Received(1).SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>());
        await _hosted.DidNotReceiveWithAnyArgs().SendAsync(default, default);
    }

    [Fact]
    public async Task AMailboxNoLongerConfigured_FallsBackToThePrimary_WithAWarningNamingIt()
    {
        var result = await Channel().PostWithResultAsync(Binding("removed"), Answer());

        Assert.True(result.Delivered);
        await _own.Received(1).SendAsync(Arg.Is<OutboundMail>(x => x.ReplyTo == null), Arg.Any<CancellationToken>());
        Assert.Contains(_log.Entries, x => x.Level == LogLevel.Warning && x.Message.Contains("removed"));
    }

    [Fact]
    public async Task AMailboxThatCannotSend_SaysSo_RatherThanBorrowingThePrimarysAccount()
    {
        _hosted.CanSend.Returns(false);

        var result = await Channel().PostWithResultAsync(Binding(Hosted), Answer());

        Assert.False(result.Delivered);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        await _own.DidNotReceiveWithAnyArgs().SendAsync(default, default);
    }

    [Fact]
    public async Task TheSentAnswersId_KeepsTheCasesMailbox()
    {
        _hosted.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("answer-2@example.net"));

        await Channel().PostWithResultAsync(Binding(Hosted), Answer());

        await _store.Received(1).AddBindingAsync(TeamKey, CaseId,
            Arg.Is<SupportChannelBinding>(x => x.ExternalId == "answer-2@example.net" && x.MessageSequence == 2 && x.Mailbox == Hosted && x.MailboxAddress == HostedAddress),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The non-production policy governs every mailbox's sending, not only the primary's.</summary>
    [Fact]
    public async Task AnAddedMailboxsClient_IsGovernedByTheOutboundPolicy()
    {
        var policy = Substitute.For<IOutboundMailPolicy>();
        policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(new OutboundMailDecision(OutboundMailAction.Withhold, Customer, "Re: Export is empty"));
        var hosted = HostedOptions();
        hosted.Smtp.Host = "smtp.example.net";

        var client = new SupportMailClients(_own, policy).For(new SupportMailbox(Hosted, hosted));
        var result = await client.SendAsync(new OutboundMail(Customer, "Re: Export is empty", "Looking into it."));

        Assert.False(result.Success);
        Assert.Contains("Withheld by the non-production mail policy", result.Error);
    }

    /// <summary>The primary keeps the client the host registered; another mailbox gets one built from its own settings.</summary>
    [Fact]
    public void EachMailboxsClient_IsBuiltFromItsOwnSettings()
    {
        var clients = new SupportMailClients(_own, Substitute.For<IOutboundMailPolicy>());
        var sending = HostedOptions();
        sending.Smtp.Host = "smtp.example.net";

        Assert.Same(_own, clients.For(SupportMailbox.Primary(new MailOptions())));
        Assert.True(clients.For(new SupportMailbox(Hosted, sending)).CanSend);
        Assert.False(clients.For(new SupportMailbox(Hosted, HostedOptions())).CanSend);
    }

    /// <summary>A bounce of an answer the hosted mailbox sent arrives there, and marks that answer as not delivered.</summary>
    [Fact]
    public async Task ABounceReadFromAnAddedMailbox_MarksTheAnswerItNames()
    {
        var supportCase = Case() with
        {
            Bindings =
            [
                Binding(Hosted),
                Binding(Hosted) with { ExternalId = "answer-2@example.net", MessageSequence = 2 }
            ]
        };
        _store.GetCaseByBindingAsync(SupportChannelType.Email, "answer-2@example.net", Arg.Any<CancellationToken>()).Returns(supportCase);
        var mailboxes = Mailboxes();
        var handler = new EmailEventHandler(_store, new InMemoryLedger(), Options.Create(mailboxes.Primary.Options), TimeProvider.System, mailboxes: mailboxes);
        var bounce = new InboundMail(
            MessageId: "bounce-1@mx.example.net",
            From: "mailer-daemon@mx.example.net",
            DeliveredTo: [HostedAddress],
            Subject: "Undelivered Mail Returned to Sender",
            Body: "The mail could not be delivered.",
            SentAt: new DateTimeOffset(2026, 10, 17, 9, 30, 0, TimeSpan.Zero),
            IsAutomated: true,
            BouncedMessageId: "answer-2@example.net",
            BounceReason: "550 5.1.1 No such user");

        var throughPrimary = await handler.HandleAsync(bounce, mailboxes.Primary, CancellationToken.None);
        var throughHosted = await handler.HandleAsync(bounce, mailboxes.Find(Hosted), CancellationToken.None);

        Assert.Equal("addressed elsewhere", throughPrimary.Reason);
        Assert.True(throughHosted.Bounced);
        await _store.Received(1).SetMessageDeliveryAsync(TeamKey, CaseId, 2, SupportMessageDelivery.Failed, "550 5.1.1 No such user", Arg.Any<CancellationToken>());
    }

    private EmailSupportChannel Channel()
    {
        _store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId, Arg.Any<CancellationToken>()).Returns(Case());
        var mailboxes = Mailboxes();

        return new EmailSupportChannel(_own, _store, Options.Create(mailboxes.Primary.Options), mailboxes: mailboxes,
            mailClients: new FakeClients(_own, _hosted), logger: _log);
    }

    private static SupportMailboxes Mailboxes()
        => new(new MailOptions { FromAddress = OwnAddress, Recipients = ["example.com"] }, [new SupportMailbox(Hosted, HostedOptions())]);

    private static MailOptions HostedOptions() => new() { FromAddress = HostedAddress, FromName = "Help", PerCaseReplyTo = true, Recipients = ["example.net"] };

    private static IMailClient Sender()
    {
        var client = Substitute.For<IMailClient>();
        client.CanSend.Returns(true);
        client.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("answer@example.com"));

        return client;
    }

    private static SupportChannelBinding Binding(string mailbox) => new()
    {
        ChannelType = SupportChannelType.Email,
        ExternalId = OpeningId,
        Address = Customer,
        Mailbox = mailbox,
        MailboxAddress = mailbox == Hosted ? HostedAddress : null
    };

    private static SupportCase Case() => new()
    {
        Id = CaseId,
        TeamKey = TeamKey,
        AuthorIdentity = null,
        AuthorName = Customer,
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc),
        MessageCount = 1
    };

    private static SupportMessage Answer() => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "agent-1",
        AuthorName = "Support Agent",
        Body = "Looking into it.",
        SentAt = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc),
        AsSupport = true
    };

    private sealed class FakeClients(IMailClient primary, IMailClient hosted) : ISupportMailClients
    {
        public IMailClient For(SupportMailbox mailbox) => mailbox.IsPrimary ? primary : mailbox.Name == Hosted ? hosted : null;
    }
}
