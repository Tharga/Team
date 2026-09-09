using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The failure counter behind the invitation throttle (Tharga/Team#256).
/// </summary>
/// <remarks>
/// Delay lengths are asserted here rather than through the service, so nothing in the suite has to actually
/// wait for one.
/// </remarks>
public class InvitationThrottleTests
{
    private static InvitationOptions Options(int threshold = 3, int maxDelayMs = 2000) =>
        new()
        {
            ThrottleFailureThreshold = threshold,
            ThrottleWindow = TimeSpan.FromMinutes(5),
            MaxThrottleDelay = TimeSpan.FromMilliseconds(maxDelayMs)
        };

    [Fact]
    public void FailuresUpToTheThreshold_AreNotDelayed()
    {
        var sut = new InvitationThrottle(TimeProvider.System);
        var options = Options(threshold: 3);

        for (var i = 0; i < 3; i++)
        {
            var (delay, tripped) = sut.RecordFailure("1.2.3.4", options);

            Assert.Equal(TimeSpan.Zero, delay);
            Assert.False(tripped);
        }
    }

    /// <summary>The crossing is reported once, so the audit log gets one entry rather than thousands.</summary>
    [Fact]
    public void TheFailureThatCrossesTheThreshold_IsReportedOnce()
    {
        var sut = new InvitationThrottle(TimeProvider.System);
        var options = Options(threshold: 3);
        for (var i = 0; i < 3; i++) sut.RecordFailure("1.2.3.4", options);

        var crossing = sut.RecordFailure("1.2.3.4", options);
        var after = sut.RecordFailure("1.2.3.4", options);

        Assert.True(crossing.JustTripped);
        Assert.True(crossing.Delay > TimeSpan.Zero);
        Assert.False(after.JustTripped);
        Assert.True(after.Delay > TimeSpan.Zero);
    }

    [Fact]
    public void TheDelayGrows_AndIsCapped()
    {
        var sut = new InvitationThrottle(TimeProvider.System);
        var options = Options(threshold: 1, maxDelayMs: 1000);
        sut.RecordFailure("1.2.3.4", options);

        var first = sut.RecordFailure("1.2.3.4", options).Delay;
        var second = sut.RecordFailure("1.2.3.4", options).Delay;

        Assert.True(second > first);

        for (var i = 0; i < 20; i++) sut.RecordFailure("1.2.3.4", options);
        Assert.Equal(TimeSpan.FromSeconds(1), sut.RecordFailure("1.2.3.4", options).Delay);
    }

    /// <summary>
    /// Counting is per source, so one attacker's budget is not spent on everybody else — which is what
    /// keeps the delay off legitimate invitees whose address is known.
    /// </summary>
    [Fact]
    public void SourcesAreCountedSeparately()
    {
        var sut = new InvitationThrottle(TimeProvider.System);
        var options = Options(threshold: 1);
        sut.RecordFailure("1.2.3.4", options);
        sut.RecordFailure("1.2.3.4", options);

        Assert.Equal(TimeSpan.Zero, sut.RecordFailure("5.6.7.8", options).Delay);
    }

    /// <summary>A quiet period costs an attacker their progress; it must not cost a person theirs.</summary>
    [Fact]
    public void TheWindowExpires()
    {
        var time = new MovableTime(DateTimeOffset.UnixEpoch);
        var sut = new InvitationThrottle(time);
        var options = Options(threshold: 1);
        sut.RecordFailure("1.2.3.4", options);
        sut.RecordFailure("1.2.3.4", options);

        time.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(TimeSpan.Zero, sut.RecordFailure("1.2.3.4", options).Delay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveThreshold_TurnsThrottlingOff(int threshold)
    {
        var sut = new InvitationThrottle(TimeProvider.System);
        var options = Options(threshold);

        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(TimeSpan.Zero, sut.RecordFailure("1.2.3.4", options).Delay);
        }
    }

    [Fact]
    public void AZeroMaxDelay_TurnsTheWaitOff_ButStillReportsTheCrossing()
    {
        var sut = new InvitationThrottle(TimeProvider.System);
        var options = Options(threshold: 1, maxDelayMs: 0);
        sut.RecordFailure("1.2.3.4", options);

        var crossing = sut.RecordFailure("1.2.3.4", options);

        Assert.Equal(TimeSpan.Zero, crossing.Delay);
        Assert.True(crossing.JustTripped);
    }

    private sealed class MovableTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
