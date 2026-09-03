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
        // Named but not identified: the address is a display name, and AuthorIdentity is the field
        // authorization compares a caller's subject against.
        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.Body == "Any news?" && x.AuthorIdentity == null && x.AuthorName == Correspondent),
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

    /// <summary>
    /// Somebody asking for help for the first time. It used to be dropped, which meant a customer wrote to
    /// support and heard nothing.
    /// </summary>
    [Fact]
    public async Task AMailNamingNoKnownThread_OpensAnUnassignedCase()
    {
        var (handler, store, _, _) = Build();

        var outcome = await handler.HandleAsync(NewThread());

        Assert.True(outcome.WasApplied);
        Assert.True(outcome.OpenedCase);
        Assert.NotNull(outcome.CaseId);

        await store.Received(1).AddCaseAsync(
            Arg.Is<SupportCase>(x =>
                x.TeamKey == null &&
                x.AuthorIdentity == null &&
                x.AuthorName == "stranger@example.com" &&
                x.Subject == "Export is empty" &&
                x.Status == SupportCaseStatus.Open),
            Arg.Is<SupportMessage>(x => x.Body == "Nothing comes out." && x.Source == SupportChannelType.Email),
            Arg.Any<CancellationToken>());

        // Nothing is appended to anything: the case and its first message are written as one unit.
        await store.DidNotReceive().AppendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// <b>The binding is what makes the trust rule work from the second mail onwards.</b> The case now
    /// corresponds with exactly one address, so a stranger who learns the thread id cannot write into it.
    /// </summary>
    [Fact]
    public async Task TheNewCase_CorrespondsWithTheSenderOnItsOwnThread()
    {
        var (handler, store, _, _) = Build();

        await handler.HandleAsync(NewThread());

        await store.Received(1).AddCaseAsync(
            Arg.Is<SupportCase>(x => x.Bindings.Length == 1 &&
                x.Bindings[0].ChannelType == SupportChannelType.Email &&
                x.Bindings[0].ExternalId == "first-1@example.com" &&
                x.Bindings[0].Address == "stranger@example.com"),
            Arg.Any<SupportMessage>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpeningACaseFromMail_IsNotifiedAsRaisedFromTheChannel()
    {
        var (handler, _, notifier, _) = Build();

        var outcome = await handler.HandleAsync(NewThread());

        notifier.Received(1).Notify(Arg.Is<SupportCaseUpdatedEventArgs>(x =>
            x.CaseId == outcome.CaseId && x.TeamKey == null && x.FromChannel && x.Change == SupportCaseChange.Raised));
    }

    [Fact]
    public async Task AMailWithNoSubject_TakesOneFromWhatWasWritten()
    {
        var (handler, store, _, _) = Build();

        await handler.HandleAsync(NewThread() with { Subject = "  ", Body = "The export button does nothing at all." });

        await store.Received(1).AddCaseAsync(
            Arg.Is<SupportCase>(x => x.Subject == "The export button does nothing at all."),
            Arg.Any<SupportMessage>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// <b>Trimmed rather than refused.</b> The service throws past the limit, which is right for somebody
    /// typing into a form; here there is nobody to tell and the ledger has already recorded the message id,
    /// so refusing would discard what a customer sent and never ask again.
    /// </summary>
    [Fact]
    public async Task AMailLongerThanTheLimit_IsTrimmedRatherThanRefused()
    {
        var (handler, store, _, _) = Build();

        var outcome = await handler.HandleAsync(Mail() with { Body = new string('x', SupportCaseLimits.MaxMessageLength * 2) });

        Assert.True(outcome.WasApplied);
        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.Body.Length == SupportCaseLimits.MaxMessageLength && x.Body.EndsWith("[trimmed]")),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A stranger gets their own case; the refusal above is about writing into somebody else's.
    /// </summary>
    [Fact]
    public async Task AStrangerOnAKnownThread_IsStillRefused()
    {
        var (handler, store, _, _) = Build();

        var outcome = await handler.HandleAsync(Mail() with { From = "stranger@example.com" });

        Assert.False(outcome.WasApplied);
        await store.DidNotReceive().AddCaseAsync(Arg.Any<SupportCase>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
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

    /// <summary>A first mail: no thread, from an address the product has never seen.</summary>
    private static InboundMail NewThread() => Mail() with
    {
        MessageId = "first-1@example.com",
        From = "stranger@example.com",
        Subject = "Export is empty",
        Body = "Nothing comes out.",
        InReplyTo = null,
        References = []
    };

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
