using Microsoft.Extensions.Options;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Applying a received mail to its case: what is accepted, what is refused, and in what order.
/// </summary>
/// <remarks>
/// <b>The ordering test is the one to keep.</b> A mail addressed to the other site must not reach the
/// deduplication ledger, because the ledger is shared: claiming an id and then discarding the mail leaves the
/// instance that wanted it seeing a duplicate and concluding somebody handled it. Nobody did.
/// </remarks>
public class EmailEventHandlerTests
{
    private const string TeamKey = "acme";
    private const string CaseId = "case-1";
    private const string Correspondent = "user@example.com";
    private const string ThreadId = "opening-1@fortdocs.se";
    private const string From = "support@fortdocs.se";

    [Fact]
    public async Task AReplyInAKnownThread_IsAppendedToTheCase()
    {
        var (handler, store, _, _) = Build();

        var outcome = await handler.HandleAsync(Mail());

        Assert.True(outcome.WasApplied);
        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.Body == "Any news?" && x.AuthorIdentity == Correspondent),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnInboundReply_IsMarkedAsHavingComeFromEmail()
    {
        var (handler, store, _, _) = Build();

        await handler.HandleAsync(Mail());

        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.Source == SupportChannelType.Email && x.Delivery == SupportMessageDelivery.Sent),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheHostIsNotified_ThatSomebodyOutsideAnswered()
    {
        var (handler, _, notifier, _) = Build();

        await handler.HandleAsync(Mail());

        notifier.Received(1).Notify(Arg.Is<SupportCaseUpdatedEventArgs>(x =>
            x.CaseId == CaseId && x.FromChannel && x.Change == SupportCaseChange.Replied));
    }

    [Fact]
    public async Task AThreadNamedOnlyInReferences_StillMatches()
    {
        var (handler, store, _, _) = Build();

        var outcome = await handler.HandleAsync(Mail() with { InReplyTo = null, References = [ThreadId] });

        Assert.True(outcome.WasApplied);
    }

    /// <summary>
    /// The whole point of the filter: one mailbox, two sites, and each instance takes only its own.
    /// </summary>
    [Fact]
    public async Task AMailAddressedToTheOtherSite_IsIgnored()
    {
        var (handler, store, _, _) = Build(["fortdocs.se"]);

        var outcome = await handler.HandleAsync(Mail() with { DeliveredTo = ["support@eplicta.se"] });

        Assert.False(outcome.WasApplied);
        await store.DidNotReceive().AppendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// <b>Order, not merely outcome.</b> The ledger is shared between the two sites, so a mail this instance
    /// will never accept must leave no trace in it — otherwise the other instance is told it was handled.
    /// </summary>
    [Fact]
    public async Task AMailAddressedElsewhere_NeverReachesTheLedger()
    {
        var (handler, _, _, ledger) = Build(["fortdocs.se"]);

        await handler.HandleAsync(Mail() with { DeliveredTo = ["support@eplicta.se"] });

        await ledger.DidNotReceive().TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoFilterConfigured_MailForAnyDomainIsTaken()
    {
        var (handler, _, _, _) = Build([]);

        Assert.True((await handler.HandleAsync(Mail() with { DeliveredTo = ["anything@elsewhere.example"] })).WasApplied);
    }

    [Fact]
    public async Task TheSameMailTwice_IsAppendedOnce()
    {
        var (handler, store, _, ledger) = Build();
        ledger.TryRecordAsync(EmailEventHandler.Source, "reply-1@example.com", Arg.Any<CancellationToken>())
            .Returns(true, false);

        await handler.HandleAsync(Mail());
        var second = await handler.HandleAsync(Mail());

        Assert.False(second.WasApplied);
        await store.Received(1).AppendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAutoResponder_IsIgnored()
    {
        var (handler, store, _, _) = Build();

        var outcome = await handler.HandleAsync(Mail() with { IsAutomated = true });

        Assert.False(outcome.WasApplied);
        await store.DidNotReceive().AppendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OurOwnMailComingBack_IsIgnored()
    {
        var (handler, store, _, _) = Build();

        var outcome = await handler.HandleAsync(Mail() with { From = From });

        Assert.False(outcome.WasApplied);
        await store.DidNotReceive().AppendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// <b>A <c>From</c> header authenticates nobody.</b> Anyone who learns a thread id could otherwise write
    /// into a transcript that a real person reads and trusts.
    /// </summary>
    [Fact]
    public async Task AMailFromSomebodyElse_IsRefusedEvenWithTheRightThreadId()
    {
        var (handler, store, _, _) = Build();

        var outcome = await handler.HandleAsync(Mail() with { From = "stranger@example.com" });

        Assert.False(outcome.WasApplied);
        Assert.Contains("correspond", outcome.Reason);
        await store.DidNotReceive().AppendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMailNamingNoKnownThread_IsIgnored()
    {
        var (handler, store, _, _) = Build();

        var outcome = await handler.HandleAsync(Mail() with { InReplyTo = "unknown@example.com", References = [] });

        Assert.False(outcome.WasApplied);
        await store.DidNotReceive().AppendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The fallback for clients that drop the threading headers, which is why the address carries the case.
    /// </summary>
    [Fact]
    public async Task AMailWithNoThreadHeaders_IsMatchedByItsPerCaseAddress()
    {
        var (handler, store, _, _) = Build();
        store.GetCaseByIdAsync(CaseId, Arg.Any<CancellationToken>()).Returns(Case());

        var outcome = await handler.HandleAsync(Mail() with
        {
            InReplyTo = null,
            References = [],
            DeliveredTo = ["support+case-1@fortdocs.se"]
        });

        Assert.True(outcome.WasApplied);
    }

    [Fact]
    public async Task TheQuotedThread_IsTrimmedOffBeforeItIsStored()
    {
        var (handler, store, _, _) = Build();

        await handler.HandleAsync(Mail() with { Body = "Fixed, thanks.\n\n-- \nA User" });

        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.Body == "Fixed, thanks."), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMailWithAttachments_SaysTheyWereNotStored()
    {
        var (handler, store, _, _) = Build();

        await handler.HandleAsync(Mail() with { HadAttachments = true });

        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.Body.Contains("attachments were not stored")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMailWithNothingInIt_IsIgnored()
    {
        var (handler, store, _, _) = Build();

        var outcome = await handler.HandleAsync(Mail() with { Body = "   " });

        Assert.False(outcome.WasApplied);
        await store.DidNotReceive().AppendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMailWithNoMessageId_IsIgnored()
    {
        var (handler, _, _, ledger) = Build();

        Assert.False((await handler.HandleAsync(Mail() with { MessageId = null })).WasApplied);
        await ledger.DidNotReceive().TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static InboundMail Mail() => new(
        MessageId: "reply-1@example.com",
        From: Correspondent,
        DeliveredTo: ["support@fortdocs.se"],
        Subject: "Re: Export is empty",
        Body: "Any news?",
        SentAt: new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
        InReplyTo: ThreadId,
        References: [ThreadId]);

    private static SupportCase Case() => new()
    {
        Id = CaseId,
        TeamKey = TeamKey,
        AuthorIdentity = "sub-1",
        AuthorName = "A User",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        MessageCount = 1,
        Bindings = [new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = ThreadId, Address = Correspondent }]
    };

    private static (EmailEventHandler Handler, ISupportCaseStore Store, ISupportCaseNotifier Notifier, ISupportEventLedger Ledger) Build(
        string[] recipients = null)
    {
        var store = Substitute.For<ISupportCaseStore>();
        store.GetCaseByBindingAsync(SupportChannelType.Email, ThreadId, Arg.Any<CancellationToken>()).Returns(Case());

        var ledger = Substitute.For<ISupportEventLedger>();
        ledger.TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var notifier = Substitute.For<ISupportCaseNotifier>();

        var options = new MailOptions { FromAddress = From, Recipients = recipients ?? ["fortdocs.se"] };

        var handler = new EmailEventHandler(store, ledger, Options.Create(options), TimeProvider.System, notifier);

        return (handler, store, notifier, ledger);
    }
}
