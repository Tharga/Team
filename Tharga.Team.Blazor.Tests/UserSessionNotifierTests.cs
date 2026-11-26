using Microsoft.Extensions.Logging;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;
using static Tharga.Team.Blazor.Tests.SessionTestData;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Telling the host that a person's session ended (Tharga/Team#471), in the shape of the support case notifier: the same rules for a throwing handler, an
/// unsubscribed handler and a relay between instances, plus the one that is this notifier's own: what the instance remembers about the person's sessions is
/// forgotten before anybody is told, so whoever is told and looks cannot read the old answer.
/// </summary>
/// <remarks>The notification names the person and the reason; it never carries a session id, which is a credential's key.</remarks>
public class UserSessionNotifierTests
{
    private readonly SessionClock _clock = new();
    private readonly SessionCache _cache;

    public UserSessionNotifierTests() => _cache = new SessionCache(_clock);

    private static UserSessionEndedEventArgs Args(string user = UserKey, SessionEndReason reason = SessionEndReason.Idle, bool fromRemote = false)
        => new() { UserKey = user, Reason = reason, FromRemote = fromRemote };

    [Fact]
    public void ASubscriber_IsToldWhoseSessionEnded_AndWhy()
    {
        UserSessionEndedEventArgs seen = null;
        _cache.SessionEnded += (_, e) => seen = e;

        _cache.Notify(Args(reason: SessionEndReason.NewerSignIn));

        Assert.Equal(UserKey, seen.UserKey);
        Assert.Equal(SessionEndReason.NewerSignIn, seen.Reason);
        Assert.False(seen.FromRemote);
    }

    [Fact]
    public void TheNotification_HasNoSessionInIt()
    {
        var names = typeof(UserSessionEndedEventArgs).GetProperties().Select(x => x.Name).Order().ToArray();

        Assert.Equal(["FromRemote", "Reason", "UserKey"], names);
    }

    [Fact]
    public void TheCache_IsTheNotifier()
    {
        Assert.IsAssignableFrom<IUserSessionNotifier>(_cache);
    }

    [Fact]
    public void AThrowingSubscriber_DoesNotBreakTheNotification_OrKeepOthersFromHearingIt()
    {
        var reachedSecond = false;
        _cache.SessionEnded += (_, _) => throw new InvalidOperationException("host handler is broken");
        _cache.SessionEnded += (_, _) => reachedSecond = true;

        var exception = Record.Exception(() => _cache.Notify(Args()));

        Assert.Null(exception);
        Assert.True(reachedSecond);
    }

    [Fact]
    public void AThrowingSubscriber_IsLogged()
    {
        var logger = new CapturingLogger<SessionCache>();
        var cache = new SessionCache(_clock, logger);
        cache.SessionEnded += (_, _) => throw new InvalidOperationException("host handler is broken");

        cache.Notify(Args());

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public void WithNoSubscribers_NotifyingIsHarmless()
    {
        var exception = Record.Exception(() => _cache.Notify(Args()));

        Assert.Null(exception);
    }

    [Fact]
    public void AnUnsubscribedHandler_IsNotCalled()
    {
        var calls = 0;
        void Handler(object sender, UserSessionEndedEventArgs e) => calls++;

        _cache.SessionEnded += Handler;
        _cache.Notify(Args());

        _cache.SessionEnded -= Handler;
        _cache.Notify(Args());

        Assert.Equal(1, calls);
    }

    /// <summary>What the toolkit raises itself is never remote, so a host's relay publishes it.</summary>
    [Fact]
    public void ANotification_IsNotRemote_UnlessARelaySaysSo()
    {
        Assert.False(Args().FromRemote);
    }

    /// <summary>The relay a host with several instances builds: each publishes its own ends and passes on what arrives marked as remote, which it does not publish again.</summary>
    [Fact]
    public void TwoInstancesRelayingEachOthersEnds_DoNotEcho()
    {
        var a = new SessionCache(_clock);
        var b = new SessionCache(_clock);
        var published = 0;
        UserSessionEndedEventArgs seenOnB = null;

        void Relay(SessionCache to, UserSessionEndedEventArgs e)
        {
            if (e.FromRemote) return;

            published++;
            to.Notify(new UserSessionEndedEventArgs { UserKey = e.UserKey, Reason = e.Reason, FromRemote = true });
        }

        a.SessionEnded += (_, e) => Relay(b, e);
        b.SessionEnded += (_, e) => Relay(a, e);
        b.SessionEnded += (_, e) => seenOnB = e;

        a.Notify(Args(reason: SessionEndReason.SignedOut));

        Assert.Equal(1, published);
        Assert.True(seenOnB.FromRemote);
        Assert.Equal(SessionEndReason.SignedOut, seenOnB.Reason);
    }

    // ---- forget first

    [Fact]
    public void TheSessionsOfThatPerson_AreForgotten_BeforeAnyHandlerRuns()
    {
        _cache.SetSession(Session("s1", _clock.Now), TimeSpan.FromMinutes(5));
        _cache.SetSession(Session("s2", _clock.Now), TimeSpan.FromMinutes(5));
        var remembered = new List<bool>();
        _cache.SessionEnded += (_, _) =>
        {
            remembered.Add(_cache.TryGetSession("s1", out _));
            remembered.Add(_cache.TryGetSession("s2", out _));
        };

        _cache.Notify(Args());

        Assert.Equal([false, false], remembered);
    }

    [Fact]
    public void AnotherPersonsSessions_AreRemembered()
    {
        _cache.SetSession(Session("s1", _clock.Now), TimeSpan.FromMinutes(5));
        _cache.SetSession(Session("s3", _clock.Now, user: "user-2"), TimeSpan.FromMinutes(5));

        _cache.Notify(Args());

        Assert.False(_cache.TryGetSession("s1", out _));
        Assert.True(_cache.TryGetSession("s3", out _));
    }

    /// <summary>A handler that throws must not leave the remembered sessions in place: they are forgotten first, so the failure cannot cost that.</summary>
    [Fact]
    public void ASessionIsForgotten_EvenWhenAHandlerThrows()
    {
        _cache.SetSession(Session("s1", _clock.Now), TimeSpan.FromMinutes(5));
        _cache.SessionEnded += (_, _) => throw new InvalidOperationException("host handler is broken");

        _cache.Notify(Args());

        Assert.False(_cache.TryGetSession("s1", out _));
    }

    [Fact]
    public void ARelayedEnd_ForgetsTheSessionsToo()
    {
        _cache.SetSession(Session("s1", _clock.Now), TimeSpan.FromMinutes(5));

        _cache.Notify(Args(fromRemote: true));

        Assert.False(_cache.TryGetSession("s1", out _));
    }

    // ---- the effect on the next request

    /// <summary>
    /// Instance B remembers the session as going; it ended on instance A, which relayed it. Without the relay B lets the next request through on its remembered
    /// answer; with it, B has forgotten, reads the store and refuses.
    /// </summary>
    [Fact]
    public async Task ARelayedEnd_MakesTheNextRequestOnThatInstanceRefuse()
    {
        var store = new InMemorySessionStore();
        store.Put(Session("s1", _clock.Now));
        var policy = new TestSessionPolicy { Default = new SessionLimits { IdleTimeout = TimeSpan.FromMinutes(10) } };
        var evaluator = new SessionEvaluator(_cache, Wrap(Options()), _clock, policy, Sessions(store, _clock, notifier: _cache));
        var principal = WithSession("s1");

        Assert.Equal(SessionCheckOutcome.Valid, (await evaluator.EvaluateAsync(principal, null, null)).Outcome);

        store.Put(Session("s1", _clock.Now) with { EndedAt = _clock.Now, EndReason = SessionEndReason.Ended });
        Assert.Equal(SessionCheckOutcome.Valid, (await evaluator.EvaluateAsync(principal, null, null)).Outcome);

        _cache.Notify(Args(reason: SessionEndReason.Ended, fromRemote: true));

        var check = await evaluator.EvaluateAsync(principal, null, null);

        Assert.Equal(SessionCheckOutcome.Ended, check.Outcome);
        Assert.Equal(SessionEndReason.Ended, check.Reason);
    }
}
