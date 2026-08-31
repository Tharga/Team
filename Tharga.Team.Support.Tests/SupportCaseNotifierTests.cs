using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Telling the host that a case changed.
/// </summary>
/// <remarks>
/// <b>The <c>FromChannel</c> distinction is the one a UI is actually built on.</b> A reply the user just
/// typed needs no notification — they are looking at it. A reply that arrived from Slack is the thing worth
/// lighting a chip for, because nobody inside the application knows about it yet.
/// </remarks>
public class SupportCaseNotifierTests
{
    [Fact]
    public void ASubscriber_IsToldWhatChanged()
    {
        var notifier = new SupportCaseNotifier();
        SupportCaseUpdatedEventArgs seen = null;
        notifier.CaseUpdated += (_, e) => seen = e;

        notifier.Notify(Args(SupportCaseChange.Replied, fromChannel: true));

        Assert.Equal("acme", seen.TeamKey);
        Assert.Equal("case-1", seen.CaseId);
        Assert.Equal(SupportCaseChange.Replied, seen.Change);
        Assert.True(seen.FromChannel);
    }

    /// <summary>
    /// The case is already written when the notification goes out, so a host handler that throws must not
    /// take the operation with it — the same rule a throwing audit enricher follows.
    /// </summary>
    [Fact]
    public void AThrowingSubscriber_DoesNotBreakTheNotification()
    {
        var notifier = new SupportCaseNotifier();
        var reachedSecond = false;

        notifier.CaseUpdated += (_, _) => throw new InvalidOperationException("host handler is broken");
        notifier.CaseUpdated += (_, _) => reachedSecond = true;

        var exception = Record.Exception(() => notifier.Notify(Args(SupportCaseChange.Raised, false)));

        Assert.Null(exception);
        Assert.True(reachedSecond);
    }

    [Fact]
    public void WithNoSubscribers_NotifyingIsHarmless()
    {
        var exception = Record.Exception(() => new SupportCaseNotifier().Notify(Args(SupportCaseChange.Closed, false)));

        Assert.Null(exception);
    }

    /// <summary>
    /// A component that unsubscribes must stop hearing about cases — the notifier outlives every page that
    /// listens to it, so a subscription that cannot be detached is a leak.
    /// </summary>
    [Fact]
    public void AnUnsubscribedHandler_IsNotCalled()
    {
        var notifier = new SupportCaseNotifier();
        var calls = 0;
        void Handler(object sender, SupportCaseUpdatedEventArgs e) => calls++;

        notifier.CaseUpdated += Handler;
        notifier.Notify(Args(SupportCaseChange.Raised, false));

        notifier.CaseUpdated -= Handler;
        notifier.Notify(Args(SupportCaseChange.Replied, false));

        Assert.Equal(1, calls);
    }

    private static SupportCaseUpdatedEventArgs Args(SupportCaseChange change, bool fromChannel) => new()
    {
        TeamKey = "acme",
        CaseId = "case-1",
        Change = change,
        FromChannel = fromChannel
    };
}
