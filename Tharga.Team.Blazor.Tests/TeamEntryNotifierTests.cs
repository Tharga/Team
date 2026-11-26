using System.Security.Claims;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Telling the host when the team entry guard's answers for a person were forgotten (Tharga/Team#471), in the shape of the support case notifier: the same rules
/// for a throwing handler, an unsubscribed handler and a relay between instances. A relayed notification also makes this instance forget its own remembered
/// answers, or the circuit that re-evaluates because of it would read the old one.
/// </summary>
public class TeamEntryNotifierTests
{
    private const string UserKey = "user-1";
    private const string TeamKey = "acme";

    private readonly SessionClock _clock = new();
    private readonly TeamEntryCache _cache;

    public TeamEntryNotifierTests() => _cache = new TeamEntryCache(_clock);

    private ITeamEntryNotifier Notifier => _cache;

    private static TeamEntryInvalidatedEventArgs Args(string team = null, bool fromRemote = false) => new() { UserKey = UserKey, TeamKey = team, FromRemote = fromRemote };

    [Fact]
    public void TheCache_IsTheNotifier_AndStillTheGuardState()
    {
        Assert.IsAssignableFrom<ITeamEntryNotifier>(_cache);
        Assert.IsAssignableFrom<ITeamEntryGuardState>(_cache);
    }

    [Fact]
    public void ASubscriber_IsToldWhoseAnswersWereForgotten_AndInWhichTeam()
    {
        TeamEntryInvalidatedEventArgs seen = null;
        Notifier.Invalidated += (_, e) => seen = e;

        Notifier.Notify(Args(TeamKey));

        Assert.Equal((UserKey, TeamKey, false), (seen.UserKey, seen.TeamKey, seen.FromRemote));
    }

    [Fact]
    public async Task InvalidatingAnswers_NotifiesAsNotRemote_SoTheHostsRelayPublishesIt()
    {
        var heard = new List<TeamEntryInvalidatedEventArgs>();
        Notifier.Invalidated += (_, e) => heard.Add(e);

        await _cache.InvalidateAsync(UserKey, TeamKey);
        await _cache.InvalidateAsync(UserKey);

        Assert.Equal([(UserKey, TeamKey, false), (UserKey, null, false)], heard.Select(x => (x.UserKey, x.TeamKey, x.FromRemote)));
    }

    [Fact]
    public void AThrowingSubscriber_DoesNotBreakTheNotification_OrKeepOthersFromHearingIt()
    {
        var reachedSecond = false;
        Notifier.Invalidated += (_, _) => throw new InvalidOperationException("host handler is broken");
        Notifier.Invalidated += (_, _) => reachedSecond = true;

        var exception = Record.Exception(() => Notifier.Notify(Args()));

        Assert.Null(exception);
        Assert.True(reachedSecond);
    }

    [Fact]
    public async Task AThrowingSubscriber_DoesNotBreakInvalidating()
    {
        Notifier.Invalidated += (_, _) => throw new InvalidOperationException("host handler is broken");

        var exception = await Record.ExceptionAsync(() => _cache.InvalidateAsync(UserKey));

        Assert.Null(exception);
    }

    [Fact]
    public void WithNoSubscribers_NotifyingIsHarmless()
    {
        var exception = Record.Exception(() => Notifier.Notify(Args()));

        Assert.Null(exception);
    }

    [Fact]
    public void AnUnsubscribedHandler_IsNotCalled()
    {
        var calls = 0;
        void Handler(object sender, TeamEntryInvalidatedEventArgs e) => calls++;

        Notifier.Invalidated += Handler;
        Notifier.Notify(Args());

        Notifier.Invalidated -= Handler;
        Notifier.Notify(Args());

        Assert.Equal(1, calls);
    }

    [Fact]
    public void ANotification_IsNotRemote_UnlessARelaySaysSo()
    {
        Assert.False(Args().FromRemote);
    }

    [Fact]
    public void TwoInstancesRelayingEachOther_DoNotEcho()
    {
        var a = new TeamEntryCache(_clock);
        var b = new TeamEntryCache(_clock);
        var published = 0;
        TeamEntryInvalidatedEventArgs seenOnB = null;

        void Relay(ITeamEntryNotifier to, TeamEntryInvalidatedEventArgs e)
        {
            if (e.FromRemote) return;

            published++;
            to.Notify(new TeamEntryInvalidatedEventArgs { UserKey = e.UserKey, TeamKey = e.TeamKey, FromRemote = true });
        }

        ((ITeamEntryNotifier)a).Invalidated += (_, e) => Relay(b, e);
        ((ITeamEntryNotifier)b).Invalidated += (_, e) => Relay(a, e);
        ((ITeamEntryNotifier)b).Invalidated += (_, e) => seenOnB = e;

        ((ITeamEntryNotifier)a).Notify(Args(TeamKey));

        Assert.Equal(1, published);
        Assert.True(seenOnB.FromRemote);
        Assert.Equal(TeamKey, seenOnB.TeamKey);
    }

    // ---- forgetting what a relayed change made stale

    /// <summary>The guard's answer changed on another instance; this one still holds the old answer until it is told, and the re-evaluation it triggers must ask again.</summary>
    [Fact]
    public async Task ARelayedNotification_MakesTheGuardBeAskedAgain()
    {
        var guard = new Mock<ITeamEntryGuard>();
        guard.Setup(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(TeamEntryDecision.Allow);
        var evaluator = new TeamEntryEvaluator(_cache, Options.Create(new ThargaBlazorOptions()), guard.Object, null,
            new CompositeAuditLogger([new CollectingAuditBackend([])], Options.Create(new AuditOptions())), new AuditEntryFactory(null));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserKey), new Claim("sid", "session-1")], "Test"));

        await evaluator.EvaluateAsync(principal, UserKey, TeamKey);
        await evaluator.EvaluateAsync(principal, UserKey, TeamKey);
        guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        Notifier.Notify(Args(TeamKey, fromRemote: true));
        await evaluator.EvaluateAsync(principal, UserKey, TeamKey);

        guard.Verify(x => x.CheckAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void TheCachedAnswersAreForgotten_BeforeAnyHandlerRuns()
    {
        _cache.Set(UserKey, "session-1", TeamKey, TeamEntryDecision.Allow, TimeSpan.FromMinutes(5));
        var remembered = new List<bool>();
        Notifier.Invalidated += (_, _) => remembered.Add(_cache.TryGet(UserKey, "session-1", TeamKey, out _));

        Notifier.Notify(Args(TeamKey, fromRemote: true));

        Assert.Equal([false], remembered);
    }

    [Fact]
    public void ARelayedNotificationForOneTeam_LeavesTheOtherTeamsAnswersAlone()
    {
        _cache.Set(UserKey, "session-1", TeamKey, TeamEntryDecision.Allow, TimeSpan.FromMinutes(5));
        _cache.Set(UserKey, "session-1", "globex", TeamEntryDecision.Allow, TimeSpan.FromMinutes(5));

        Notifier.Notify(Args(TeamKey, fromRemote: true));

        Assert.False(_cache.TryGet(UserKey, "session-1", TeamKey, out _));
        Assert.True(_cache.TryGet(UserKey, "session-1", "globex", out _));
    }

    [Fact]
    public void ARelayedNotificationForNoTeam_ForgetsEveryTeam()
    {
        _cache.Set(UserKey, "session-1", TeamKey, TeamEntryDecision.Allow, TimeSpan.FromMinutes(5));
        _cache.Set(UserKey, "session-1", "globex", TeamEntryDecision.Allow, TimeSpan.FromMinutes(5));
        _cache.Set("user-2", "session-2", TeamKey, TeamEntryDecision.Allow, TimeSpan.FromMinutes(5));

        Notifier.Notify(Args(fromRemote: true));

        Assert.False(_cache.TryGet(UserKey, "session-1", TeamKey, out _));
        Assert.False(_cache.TryGet(UserKey, "session-1", "globex", out _));
        Assert.True(_cache.TryGet("user-2", "session-2", TeamKey, out _));
    }
}
