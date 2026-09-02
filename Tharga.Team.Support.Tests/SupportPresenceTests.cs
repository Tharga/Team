using Microsoft.Extensions.Options;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Whether anybody is on support, and what it says when it cannot tell.
/// </summary>
/// <remarks>
/// <b>"Cannot tell" must never render as "away".</b> Telling a customer not to bother when support is in fact
/// there is worse than saying nothing, and unknown is what a rate limit, a network blip and an unconfigured
/// workspace all produce — so most of these tests are about that distinction rather than about the happy
/// path.
/// </remarks>
public class SupportPresenceTests
{
    private const string Channel = "#support";

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OneActiveMemberMakesSupportOnline()
    {
        var (presence, slack, _) = Build();
        slack.GetChannelMembersAsync(Channel, Arg.Any<CancellationToken>()).Returns(["U1", "U2"]);
        slack.IsActiveAsync("U1", Arg.Any<CancellationToken>()).Returns((bool?)false);
        slack.IsActiveAsync("U2", Arg.Any<CancellationToken>()).Returns((bool?)true);

        Assert.Equal(SupportPresenceState.Online, await presence.GetAsync());
    }

    [Fact]
    public async Task EverybodyAwayIsAway()
    {
        var (presence, slack, _) = Build();
        slack.GetChannelMembersAsync(Channel, Arg.Any<CancellationToken>()).Returns(["U1"]);
        slack.IsActiveAsync("U1", Arg.Any<CancellationToken>()).Returns((bool?)false);

        Assert.Equal(SupportPresenceState.Away, await presence.GetAsync());
    }

    /// <summary>
    /// Slack refusing to say is not the same as nobody being there.
    /// </summary>
    [Fact]
    public async Task WhenNobodysPresenceCanBeRead_ItIsUnknownRatherThanAway()
    {
        var (presence, slack, _) = Build();
        slack.GetChannelMembersAsync(Channel, Arg.Any<CancellationToken>()).Returns(["U1", "U2"]);
        slack.IsActiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((bool?)null);

        Assert.Equal(SupportPresenceState.Unknown, await presence.GetAsync());
    }

    /// <summary>
    /// An empty read is a failure, not an empty channel — concluding "support has nobody in it" from a
    /// network blip is the same mistake as reporting away.
    /// </summary>
    [Fact]
    public async Task WhenTheChannelCannotBeRead_ItIsUnknown()
    {
        var (presence, slack, _) = Build();
        slack.GetChannelMembersAsync(Channel, Arg.Any<CancellationToken>()).Returns([]);

        Assert.Equal(SupportPresenceState.Unknown, await presence.GetAsync());
    }

    [Fact]
    public async Task WithNoChannelConfigured_ItIsUnknown_AndSlackIsNotAsked()
    {
        var (presence, slack, _) = Build(channel: null);

        Assert.Equal(SupportPresenceState.Unknown, await presence.GetAsync());
        await slack.DidNotReceive().GetChannelMembersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The reason this is cached at all: Slack rate-limits the presence endpoint per user.
    /// </summary>
    [Fact]
    public async Task RepeatedReads_AskSlackOnce()
    {
        var (presence, slack, _) = Build();
        slack.GetChannelMembersAsync(Channel, Arg.Any<CancellationToken>()).Returns(["U1"]);
        slack.IsActiveAsync("U1", Arg.Any<CancellationToken>()).Returns((bool?)true);

        for (var i = 0; i < 10; i++) await presence.GetAsync();

        await slack.Received(1).IsActiveAsync("U1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnceTheCacheExpires_ItAsksAgain()
    {
        var (presence, slack, time) = Build();
        slack.GetChannelMembersAsync(Channel, Arg.Any<CancellationToken>()).Returns(["U1"]);
        slack.IsActiveAsync("U1", Arg.Any<CancellationToken>()).Returns((bool?)true);

        await presence.GetAsync();
        time.Advance(TimeSpan.FromMinutes(2));
        await presence.GetAsync();

        await slack.Received(2).IsActiveAsync("U1", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The roster is trusted for far longer than presence: joining a team is not a minute-to-minute event,
    /// and re-reading it every minute would spend a rate limit on an answer that does not change.
    /// </summary>
    [Fact]
    public async Task TheChannelRoster_IsReadFarLessOftenThanPresence()
    {
        var (presence, slack, time) = Build();
        slack.GetChannelMembersAsync(Channel, Arg.Any<CancellationToken>()).Returns(["U1"]);
        slack.IsActiveAsync("U1", Arg.Any<CancellationToken>()).Returns((bool?)true);

        for (var i = 0; i < 5; i++)
        {
            await presence.GetAsync();
            time.Advance(TimeSpan.FromMinutes(2));
        }

        await slack.Received(1).GetChannelMembersAsync(Channel, Arg.Any<CancellationToken>());
        await slack.Received(5).IsActiveAsync("U1", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// One active member is the whole answer, so the rest are not worth a rate limit.
    /// </summary>
    [Fact]
    public async Task ItStopsAskingOnceSomebodyIsActive()
    {
        var (presence, slack, _) = Build();
        slack.GetChannelMembersAsync(Channel, Arg.Any<CancellationToken>()).Returns(["U1", "U2", "U3"]);
        slack.IsActiveAsync("U1", Arg.Any<CancellationToken>()).Returns((bool?)true);

        await presence.GetAsync();

        await slack.DidNotReceive().IsActiveAsync("U3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATransportThatThrows_IsUnknownRatherThanAnError()
    {
        var (presence, slack, _) = Build();
        slack.GetChannelMembersAsync(Channel, Arg.Any<CancellationToken>())
            .Returns<string[]>(_ => throw new HttpRequestException("no route to host"));

        Assert.Equal(SupportPresenceState.Unknown, await presence.GetAsync());
    }

    private static (ISupportPresence Presence, ISlackClient Slack, FakeTimeProvider Time) Build(string channel = Channel)
    {
        var slack = Substitute.For<ISlackClient>();
        var time = new FakeTimeProvider(Now);
        var options = Options.Create(new SupportCaseOptions { SlackChannel = channel });

        return (new SlackSupportPresence(slack, options, time), slack, time);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
