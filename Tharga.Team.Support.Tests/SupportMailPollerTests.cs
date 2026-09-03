using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Reading the mailbox on an interval: what advances the position, when it is written, and what a failure
/// costs.
/// </summary>
/// <remarks>
/// <b>The key test is the one that earns its place.</b> Two sites sharing one <c>support@</c> address must
/// not share a read position — the first to poll would advance past a message addressed to the second, and
/// the second would never see it. That is the mail-loss failure this whole design was shaped around, and it
/// is invisible until somebody's customer says nobody answered.
/// </remarks>
public class SupportMailPollerTests
{
    [Fact]
    public void TwoSitesSharingAMailbox_DoNotShareAPosition()
    {
        var fortdocs = SupportMailPoller.PositionKey(new MailOptions { Recipients = ["fortdocs.se"] });
        var eplicta = SupportMailPoller.PositionKey(new MailOptions { Recipients = ["eplicta.se"] });

        Assert.NotEqual(fortdocs, eplicta);
    }

    /// <summary>
    /// Two deployments answering for the same recipients share a position on purpose: they would handle the
    /// same mail, and the ledger already stops the second applying it twice.
    /// </summary>
    [Theory]
    [InlineData(new[] { "a.se", "b.se" }, new[] { "b.se", "a.se" })]
    [InlineData(new[] { "A.SE" }, new[] { "a.se" })]
    [InlineData(new[] { " a.se " }, new[] { "a.se" })]
    public void TheKeyIgnoresOrderCaseAndPadding(string[] left, string[] right)
    {
        Assert.Equal(
            SupportMailPoller.PositionKey(new MailOptions { Recipients = left }),
            SupportMailPoller.PositionKey(new MailOptions { Recipients = right }));
    }

    [Fact]
    public void ReadingADifferentFolder_IsADifferentPosition()
    {
        Assert.NotEqual(
            SupportMailPoller.PositionKey(new MailOptions { Folder = "INBOX" }),
            SupportMailPoller.PositionKey(new MailOptions { Folder = "Support" }));
    }

    [Fact]
    public void NoRecipientFilter_IsItsOwnKeyRatherThanAnEmptyOne()
    {
        var key = SupportMailPoller.PositionKey(new MailOptions());

        Assert.Contains("all", key);
        Assert.NotEqual(key, SupportMailPoller.PositionKey(new MailOptions { Recipients = ["fortdocs.se"] }));
    }

    [Fact]
    public async Task APoll_AppliesWhatArrivedAndRecordsWhereItGotTo()
    {
        var (poller, store, positions, _) = Build(Mail("first-1@example.com"));

        var applied = await poller.PollAsync(CancellationToken.None);

        Assert.Equal(1, applied);
        await store.Received(1).AddCaseAsync(Arg.Any<SupportCase>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
        await positions.Received(1).SetAsync(Arg.Any<string>(), new SupportMailPosition(7, 42), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The position resumes from the store, so a restart does not re-read the mailbox.
    /// </summary>
    [Fact]
    public async Task TheStoredPosition_IsWhereTheFetchStartsFrom()
    {
        var (poller, _, positions, client) = Build();
        positions.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new SupportMailPosition(7, 30));

        await poller.PollAsync(CancellationToken.None);

        await client.Received(1).FetchAsync(new MailFetchPosition(7, 30), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An idle deployment must stop writing, or it writes to the database once a minute forever.
    /// </summary>
    /// <remarks>
    /// The first poll of an empty mailbox does write, and should: it records the UID generation, which is
    /// what a later change of generation is recognised against. What must not happen is a write on every
    /// tick after that, when nothing has moved.
    /// </remarks>
    [Fact]
    public async Task RepeatedIdlePolls_WriteThePositionOnlyOnce()
    {
        var (poller, _, positions, _) = Build();

        await poller.PollAsync(CancellationToken.None);
        await poller.PollAsync(CancellationToken.None);
        await poller.PollAsync(CancellationToken.None);

        await positions.Received(1).SetAsync(Arg.Any<string>(), Arg.Any<SupportMailPosition>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A mailbox whose UID generation changed is read again, and the mail it re-reads must not land twice.
    /// </summary>
    /// <remarks>
    /// <b>The ledger is what makes the re-read safe</b>, not any care taken here — so this asserts the two
    /// halves together: the rescan is applied, and a message the ledger has already seen is not applied
    /// again. Discarding a position that may point at different messages is always the right trade, because
    /// re-reading costs a pass and skipping loses mail.
    /// </remarks>
    [Fact]
    public async Task ARescannedMailbox_IsReadAgainWithoutAppendingTwice()
    {
        var (poller, store, _, client) = Build([Mail("first-1@example.com")], rescanned: true);

        Assert.Equal(1, await poller.PollAsync(CancellationToken.None));

        // The second pass sees the same mail; the ledger refuses it, so nothing is written.
        Refuse(client);

        Assert.Equal(0, await poller.PollAsync(CancellationToken.None));

        await store.Received(1).AddCaseAsync(Arg.Any<SupportCase>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The same message polled twice reaches a case once. Two instances handed it together are the same
    /// question, decided by the ledger rather than by either of them.
    /// </summary>
    [Fact]
    public async Task TheSameMessagePolledTwice_ReachesACaseOnce()
    {
        var (poller, store, _, client) = Build(Mail("first-1@example.com"));

        Assert.Equal(1, await poller.PollAsync(CancellationToken.None));

        Refuse(client);

        Assert.Equal(0, await poller.PollAsync(CancellationToken.None));

        await store.Received(1).AddCaseAsync(Arg.Any<SupportCase>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// One mail that cannot be handled must not hold back the ones behind it.
    /// </summary>
    [Fact]
    public async Task AMailThatThrows_DoesNotStopTheRest()
    {
        var (poller, store, positions, _) = Build(Mail("bad-1@example.com"), Mail("good-1@example.com"));

        store.AddCaseAsync(
                Arg.Is<SupportCase>(x => x.AuthorName == "bad-1@example.com"),
                Arg.Any<SupportMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new TimeoutException("the database blinked")));

        var applied = await poller.PollAsync(CancellationToken.None);

        Assert.Equal(1, applied);
        await positions.Received(1).SetAsync(Arg.Any<string>(), Arg.Any<SupportMailPosition>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Without a position store the poller still works — it keeps its position in memory, and a restart
    /// re-reads what the ledger then recognises.
    /// </summary>
    [Fact]
    public async Task WithNoPositionStore_ItStillReadsAndApplies()
    {
        var (poller, store, _, _) = Build([Mail("first-1@example.com")], withPositionStore: false);

        Assert.Equal(1, await poller.PollAsync(CancellationToken.None));
        await store.Received(1).AddCaseAsync(Arg.Any<SupportCase>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A mailbox that cannot be read is not a failure to report upwards: the next tick tries again.
    /// </summary>
    [Fact]
    public async Task AMailboxThatCannotBeRead_PollsToNothing()
    {
        var (poller, store, _, _) = Build([], canRead: false);

        Assert.Equal(0, await poller.PollAsync(CancellationToken.None));
        await store.DidNotReceive().AddCaseAsync(Arg.Any<SupportCase>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    private static InboundMail Mail(string messageId) => new(
        MessageId: messageId,
        From: messageId,
        DeliveredTo: ["support@fortdocs.se"],
        Subject: "Export is empty",
        Body: "Nothing comes out.",
        SentAt: new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.Zero));

    /// <summary>Makes this instance's ledger report every id as already handled, as a second pass would.</summary>
    private void Refuse(ISupportMailClient client)
        => _ledgers[client].TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

    private readonly Dictionary<ISupportMailClient, ISupportEventLedger> _ledgers = [];

    private (SupportMailPoller Poller, ISupportCaseStore Store, ISupportMailPositionStore Positions, ISupportMailClient Client) Build(
        params InboundMail[] mails)
        => Build(mails, true, true);

    private (SupportMailPoller Poller, ISupportCaseStore Store, ISupportMailPositionStore Positions, ISupportMailClient Client) Build(
        InboundMail[] mails,
        bool withPositionStore = true,
        bool canRead = true,
        bool rescanned = false)
    {
        var store = Substitute.For<ISupportCaseStore>();

        var ledger = Substitute.For<ISupportEventLedger>();
        ledger.TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var client = Substitute.For<ISupportMailClient>();
        client.CanRead.Returns(canRead);
        // Faithful to the real client on the part that matters: a fetch that finds nothing reports the
        // mailbox's UID generation with the position it was given, rather than rewinding it to zero.
        client.FetchAsync(Arg.Any<MailFetchPosition>(), Arg.Any<CancellationToken>())
            .Returns(call => new MailFetchResult(
                new MailFetchPosition(7, mails.Length == 0 ? call.Arg<MailFetchPosition>().LastUid : 42u),
                mails,
                rescanned));

        _ledgers[client] = ledger;

        var positions = Substitute.For<ISupportMailPositionStore>();
        positions.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SupportMailPosition.Start);

        var options = Options.Create(new MailOptions { FromAddress = "support@fortdocs.se", Recipients = ["fortdocs.se"] });

        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(ledger);
        services.AddSingleton(client);
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<EmailEventHandler>();

        if (withPositionStore) services.AddSingleton(positions);

        var provider = services.BuildServiceProvider();

        var poller = new SupportMailPoller(provider.GetRequiredService<IServiceScopeFactory>(), options);

        return (poller, store, positions, client);
    }
}
