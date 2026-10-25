using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Whether anybody is on the support desk: what each support person chose, adjusted by what they do.
/// </summary>
/// <remarks>
/// <b>Nobody known is unknown, never away.</b> A host that has never had anybody on the desk shows members nothing,
/// exactly as before the desk existed.
/// </remarks>
public class SupportDeskPresenceTests
{
    private const string Ann = "ann-subject";
    private const string Ben = "ben-subject";
    private static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(10);

    [Fact]
    public void WithNobodyOnTheDesk_ItIsUnknown()
    {
        var (desk, _) = Build();

        Assert.Equal(SupportPresenceState.Unknown, desk.State);
    }

    [Fact]
    public void SomebodyWhoChoseOnline_MakesSupportOnline()
    {
        var (desk, _) = Build();

        desk.Set(Ann, SupportStaffStatus.Online);

        Assert.Equal(SupportPresenceState.Online, desk.State);
        Assert.Equal(SupportStaffStatus.Online, desk.StatusOf(Ann));
    }

    [Fact]
    public void SomebodyWhoChoseAway_MakesSupportAway()
    {
        var (desk, _) = Build();

        desk.Set(Ann, SupportStaffStatus.Away);

        Assert.Equal(SupportPresenceState.Away, desk.State);
    }

    /// <summary>Hidden answers unseen: members are told nothing about support, not even that it will reply later.</summary>
    [Fact]
    public void Hidden_IsNotCountedAtAll()
    {
        var (desk, _) = Build();

        desk.Set(Ann, SupportStaffStatus.Hidden);
        desk.Acted(Ann);

        Assert.Equal(SupportPresenceState.Unknown, desk.State);
    }

    [Fact]
    public void SomebodyAway_BesideSomebodyHidden_IsAway()
    {
        var (desk, _) = Build();

        desk.Set(Ann, SupportStaffStatus.Hidden);
        desk.Set(Ben, SupportStaffStatus.Away);

        Assert.Equal(SupportPresenceState.Away, desk.State);
    }

    [Fact]
    public void GoingHidden_TakesSupportOffTheIndicator_AndSaysSo()
    {
        var (desk, _) = Build();
        desk.Set(Ann, SupportStaffStatus.Online);
        var changes = 0;
        desk.Changed += (_, _) => changes++;

        desk.Set(Ann, SupportStaffStatus.Hidden);

        Assert.Equal(SupportPresenceState.Unknown, desk.State);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Online_CountsAsAwayOnceIdle_AndSaysSo()
    {
        var (desk, time) = Build();
        desk.Set(Ann, SupportStaffStatus.Online);
        var changes = 0;
        desk.Changed += (_, _) => changes++;

        time.Advance(IdleAfter - TimeSpan.FromSeconds(1));
        Assert.Equal(SupportPresenceState.Online, desk.State);
        Assert.Equal(0, changes);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(SupportPresenceState.Away, desk.State);
        Assert.Equal(1, changes);
        Assert.Equal(SupportStaffStatus.Online, desk.StatusOf(Ann));
    }

    [Fact]
    public void Acting_KeepsOnlineCounting_AndBringsSomebodyIdleBack()
    {
        var (desk, time) = Build();
        desk.Set(Ann, SupportStaffStatus.Online);

        time.Advance(TimeSpan.FromMinutes(8));
        desk.Acted(Ann);
        time.Advance(TimeSpan.FromMinutes(8));
        Assert.Equal(SupportPresenceState.Online, desk.State);

        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(SupportPresenceState.Away, desk.State);

        var changes = 0;
        desk.Changed += (_, _) => changes++;
        desk.Acted(Ann);

        Assert.Equal(SupportPresenceState.Online, desk.State);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void OnePersonOnline_IsEnough_UntilTheLastGoesIdle()
    {
        var (desk, time) = Build();
        desk.Set(Ann, SupportStaffStatus.Online);
        time.Advance(TimeSpan.FromMinutes(5));
        desk.Set(Ben, SupportStaffStatus.Online);

        time.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(SupportPresenceState.Online, desk.State);

        time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(SupportPresenceState.Away, desk.State);
    }

    /// <summary>Acting is not choosing: somebody who never set a status is not shown, whatever they do.</summary>
    [Fact]
    public void ActingWithoutAStatus_ChangesNothing()
    {
        var (desk, _) = Build();

        desk.Acted(Ann);

        Assert.Equal(SupportPresenceState.Unknown, desk.State);
        Assert.Null(desk.StatusOf(Ann));
    }

    /// <summary>Another page of the same person shows the choice, even when support as a whole looks the same.</summary>
    [Fact]
    public void SettingAStatus_AlwaysSaysSo()
    {
        var (desk, _) = Build();
        desk.Set(Ann, SupportStaffStatus.Away);
        var changes = 0;
        desk.Changed += (_, _) => changes++;

        desk.Set(Ann, SupportStaffStatus.Hidden);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void AThrowingListener_DoesNotStopTheOthers()
    {
        var (desk, _) = Build();
        var reached = false;
        desk.Changed += (_, _) => throw new InvalidOperationException("host handler is broken");
        desk.Changed += (_, _) => reached = true;

        var exception = Record.Exception(() => desk.Set(Ann, SupportStaffStatus.Online));

        Assert.Null(exception);
        Assert.True(reached);
    }

    [Fact]
    public async Task TheDeskOnline_IsOnline_WithoutAskingSlack()
    {
        var (desk, time) = Build();
        var slack = Substitute.For<ISlackClient>();
        var presence = new SupportPresence(desk, Slack(slack, time));
        desk.Set(Ann, SupportStaffStatus.Online);

        Assert.Equal(SupportPresenceState.Online, await presence.GetAsync());
        await slack.DidNotReceive().GetChannelMembersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, true, SupportPresenceState.Online)]
    [InlineData(SupportStaffStatus.Away, true, SupportPresenceState.Online)]
    [InlineData(SupportStaffStatus.Away, null, SupportPresenceState.Away)]
    [InlineData(null, false, SupportPresenceState.Away)]
    [InlineData(null, null, SupportPresenceState.Unknown)]
    public async Task TheDeskAndSlack_OnlineWhenEitherIs_AwayWhenEitherKnows(SupportStaffStatus? onDesk, bool? activeInSlack, SupportPresenceState expected)
    {
        var (desk, time) = Build();
        var slack = Substitute.For<ISlackClient>();
        slack.GetChannelMembersAsync("#support", Arg.Any<CancellationToken>()).Returns(["U1"]);
        slack.IsActiveAsync("U1", Arg.Any<CancellationToken>()).Returns(activeInSlack);
        if (onDesk != null) desk.Set(Ann, onDesk.Value);

        Assert.Equal(expected, await new SupportPresence(desk, Slack(slack, time)).GetAsync());
    }

    [Fact]
    public async Task WithoutSlack_TheDeskIsTheAnswer()
    {
        var (desk, _) = Build();
        var presence = new SupportPresence(desk);

        Assert.Equal(SupportPresenceState.Unknown, await presence.GetAsync());

        desk.Set(Ann, SupportStaffStatus.Away);
        Assert.Equal(SupportPresenceState.Away, await presence.GetAsync());
    }

    [Fact]
    public void ListeningToPresence_HearsTheDesk()
    {
        var (desk, _) = Build();
        ISupportPresence presence = new SupportPresence(desk);
        var changes = 0;
        void Handler(object sender, EventArgs e) => changes++;

        presence.Changed += Handler;
        desk.Set(Ann, SupportStaffStatus.Online);
        presence.Changed -= Handler;
        desk.Set(Ann, SupportStaffStatus.Away);

        Assert.Equal(1, changes);
    }

    internal static (SupportDeskPresence Desk, ManualClock Time) Build()
    {
        var time = new ManualClock();
        return (new SupportDeskPresence(Options.Create(new SupportCaseOptions { StaffIdleAfter = IdleAfter }), time), time);
    }

    private static SlackSupportPresence Slack(ISlackClient slack, TimeProvider time)
        => new(slack, Options.Create(new SupportCaseOptions { SlackChannel = "#support" }), time);
}

/// <summary>
/// Setting a status on the support desk through the service: who may, and what counts as acting.
/// </summary>
/// <remarks>
/// <b>Only the product's support staff.</b> Members are told support is there on the strength of it, so somebody who
/// could not answer them must not be able to say so.
/// </remarks>
public class SupportStaffStatusTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Staff = "staff-subject";

    [Theory]
    [InlineData(SystemSupportScopes.AllManage)]
    [InlineData(SystemSupportScopes.Manage)]
    public async Task SupportStaff_SetTheirStatus_AndReadItBack(string scope)
    {
        var world = new World();
        var staff = world.SupportStaff(scope);

        Assert.Null(await staff.GetMyStaffStatusAsync());

        await staff.SetMyStaffStatusAsync(SupportStaffStatus.Online);

        Assert.Equal(SupportStaffStatus.Online, await staff.GetMyStaffStatusAsync());
        Assert.Equal(SupportPresenceState.Online, world.Desk.State);
    }

    [Fact]
    public async Task AMember_CannotSayTheyAreOnTheDesk()
    {
        var world = new World();
        var alice = world.Member(Alice);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => alice.SetMyStaffStatusAsync(SupportStaffStatus.Online));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => alice.GetMyStaffStatusAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => alice.ReportStaffActivityAsync());

        Assert.Equal(SupportPresenceState.Unknown, world.Desk.State);
    }

    [Theory]
    [InlineData(SystemSupportScopes.AllRead)]
    [InlineData(SystemSupportScopes.Read)]
    public async Task StaffWhoMayOnlyRead_AreNotOnTheDesk(string scope)
    {
        var world = new World();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => world.SupportStaff(scope).SetMyStaffStatusAsync(SupportStaffStatus.Online));
    }

    [Fact]
    public async Task ReportedActivity_BringsAnIdlePersonBack()
    {
        var world = new World();
        var staff = world.SupportStaff(SystemSupportScopes.AllManage);
        await staff.SetMyStaffStatusAsync(SupportStaffStatus.Online);
        world.Time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(SupportPresenceState.Away, world.Desk.State);

        await staff.ReportStaffActivityAsync();

        Assert.Equal(SupportPresenceState.Online, world.Desk.State);
    }

    [Fact]
    public async Task Answering_AndWritingAnAnswer_CountAsActing()
    {
        var world = new World();
        var raised = await world.Member(Alice).RaiseCaseAsync(TeamA, null, "Body");
        var staff = world.SupportStaff(SystemSupportScopes.AllManage);
        await staff.SetMyStaffStatusAsync(SupportStaffStatus.Online);

        world.Time.Advance(TimeSpan.FromMinutes(11));
        await staff.ReportTypingAsync(TeamA, raised.Id, asSupport: true);
        Assert.Equal(SupportPresenceState.Online, world.Desk.State);

        world.Time.Advance(TimeSpan.FromMinutes(11));
        await staff.AnswerCaseAsync(TeamA, raised.Id, "Answer");
        Assert.Equal(SupportPresenceState.Online, world.Desk.State);
    }

    [Fact]
    public async Task AStatus_IsNeitherStoredNorAudited()
    {
        var world = new World();
        var entriesBefore = world.Audit.Entries.Count;

        await world.SupportStaff(SystemSupportScopes.AllManage).SetMyStaffStatusAsync(SupportStaffStatus.Hidden);
        await world.SupportStaff(SystemSupportScopes.AllManage).ReportStaffActivityAsync();

        Assert.Equal(entriesBefore, world.Audit.Entries.Count);
    }

    /// <summary>Through the real registration: presence is there without Slack, and it is the desk.</summary>
    [Fact]
    public async Task Registered_PresenceIsTheDesk_WithoutSlack()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISupportCaseStore>());
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(ValueTask.FromResult(Principal(Staff, null, [SystemSupportScopes.AllManage])));
        services.AddSingleton(accessor);
        services.AddSingleton<TeamAuthorizer>();
        services.Configure<AuditOptions>(_ => { });
        services.AddSingleton<CompositeAuditLogger>();
        services.AddSingleton(Substitute.For<IAuditEntryFactory>());
        services.AddThargaSupportCases();
        using var provider = services.BuildServiceProvider();
        var presence = provider.GetRequiredService<ISupportPresence>();

        Assert.Equal(SupportPresenceState.Unknown, await presence.GetAsync());

        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISupportCaseService>().SetMyStaffStatusAsync(SupportStaffStatus.Online);

        Assert.Equal(SupportPresenceState.Online, await presence.GetAsync());
    }

    private sealed class World
    {
        private readonly InMemorySupportCaseStore _store = new();

        public World() => (Desk, Time) = SupportDeskPresenceTests.Build();

        public SupportDeskPresence Desk { get; }

        public ManualClock Time { get; }

        public CollectingAuditLogger Audit { get; } = new();

        public ISupportCaseService Member(string subject) => Build(Principal(subject, TeamA, []));

        public ISupportCaseService SupportStaff(params string[] systemScopes) => Build(Principal(Staff, null, systemScopes));

        private ISupportCaseService Build(ClaimsPrincipal principal)
        {
            var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

            return new AuditingSupportCaseServiceDecorator(
                new AuthorizationSupportCaseServiceDecorator(
                    new SupportCaseService(_store, authorizer, Time, deskPresence: Desk),
                    authorizer),
                new CompositeAuditLogger([Audit], Options.Create(new AuditOptions())),
                new AuditEntryFactory(null));
        }
    }

    private static ClaimsPrincipal Principal(string subject, string memberOfTeam, string[] systemScopes)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject), new(ClaimTypes.Name, subject) };
        if (memberOfTeam != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, memberOfTeam));
        claims.AddRange(systemScopes.Select(s => new Claim(TeamClaimTypes.SystemScope, s)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private sealed class CollectingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Log(AuditEntry entry) => Entries.Add(entry);

        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}

/// <summary>A clock that moves only when told to, firing the timers that fall due.</summary>
internal sealed class ManualClock : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state, _now + dueTime);
        lock (_timers) _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        _now += by;

        ManualTimer[] due;
        lock (_timers) due = [.. _timers.Where(x => !x.Done && x.Due <= _now)];
        foreach (var timer in due) timer.Fire();
    }

    private sealed class ManualTimer(ManualClock time, TimerCallback callback, object state, DateTimeOffset due) : ITimer
    {
        public DateTimeOffset Due { get; private set; } = due;

        public bool Done { get; private set; }

        public void Fire()
        {
            if (Done) return;
            Done = true;
            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = time.GetUtcNow() + dueTime;
            Done = false;
            return true;
        }

        public void Dispose() => Done = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
