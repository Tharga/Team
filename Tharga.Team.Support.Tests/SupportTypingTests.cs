using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Saying that somebody is writing in a case: who may, what the other side is told, and what is kept.
/// </summary>
/// <remarks>
/// <b>Authorized as the write it announces.</b> The dots tell a customer an answer is coming, so somebody who could
/// not answer must not be able to show them.
/// </remarks>
public class SupportTypingTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Bob = "bob-subject";
    private const string Staff = "staff-subject";

    [Fact]
    public async Task TheAuthor_WritingInTheirCase_IsAnnouncedAsAMember()
    {
        var world = new World();
        var raised = await world.Member(Alice, "Alice Andersson").RaiseCaseAsync(TeamA, null, "Body");

        await world.Member(Alice, "Alice Andersson").ReportTypingAsync(TeamA, raised.Id, asSupport: false);

        var seen = Assert.Single(world.Seen);
        Assert.Equal(TeamA, seen.TeamKey);
        Assert.Equal(raised.Id, seen.CaseId);
        Assert.Equal(Alice, seen.Identity);
        Assert.Equal("Alice Andersson", seen.Name);
        Assert.False(seen.AsSupport);
    }

    [Fact]
    public async Task SupportStaff_AnsweringAcrossTeams_IsAnnouncedAsSupport()
    {
        var world = new World();
        var raised = await world.Member(Alice).RaiseCaseAsync(TeamA, null, "Body");

        await world.SupportStaff(SystemSupportScopes.AllManage).ReportTypingAsync(TeamA, raised.Id, asSupport: true);

        var seen = Assert.Single(world.Seen);
        Assert.Equal(Staff, seen.Identity);
        Assert.True(seen.AsSupport);
    }

    [Fact]
    public async Task AnotherMember_CannotAnnounceWritingInSomeoneElsesCase()
    {
        var world = new World();
        var raised = await world.Member(Alice).RaiseCaseAsync(TeamA, null, "Body");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => world.Member(Bob).ReportTypingAsync(TeamA, raised.Id, asSupport: false));

        Assert.Empty(world.Seen);
    }

    [Fact]
    public async Task TheAuthor_CannotAnnounceWritingAsSupport_WithoutTheGrantToAnswer()
    {
        var world = new World();
        var raised = await world.Member(Alice).RaiseCaseAsync(TeamA, null, "Body");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => world.Member(Alice).ReportTypingAsync(TeamA, raised.Id, asSupport: true));

        Assert.Empty(world.Seen);
    }

    [Fact]
    public async Task StaffWhoMayOnlyRead_CannotAnnounceAnAnswer()
    {
        var world = new World();
        var raised = await world.Member(Alice).RaiseCaseAsync(TeamA, null, "Body");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => world.SupportStaff(SystemSupportScopes.AllRead).ReportTypingAsync(TeamA, raised.Id, asSupport: true));
    }

    [Fact]
    public async Task Writing_IsNeitherStoredNorAudited()
    {
        var world = new World();
        var alice = world.Member(Alice);
        var raised = await alice.RaiseCaseAsync(TeamA, null, "Body");
        var entriesBefore = world.Audit.Entries.Count;

        await alice.ReportTypingAsync(TeamA, raised.Id, asSupport: false);

        Assert.Equal(entriesBefore, world.Audit.Entries.Count);
        var read = await alice.GetCaseAsync(TeamA, raised.Id);
        Assert.Equal(1, read.MessageCount);
        Assert.Single((await alice.GetMessagesAsync(TeamA, raised.Id)).Items);
    }

    [Fact]
    public async Task WithoutANotifier_WritingIsAccepted_AndGoesNowhere()
    {
        var store = new InMemorySupportCaseStore();
        var authorizer = Authorizer(Alice, null, TeamA, [], []);
        var service = new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(store, authorizer, TimeProvider.System), authorizer);
        var raised = await service.RaiseCaseAsync(TeamA, null, "Body");

        var exception = await Record.ExceptionAsync(() => service.ReportTypingAsync(TeamA, raised.Id, asSupport: false));

        Assert.Null(exception);
    }

    [Fact]
    public void AThrowingSubscriber_DoesNotStopTheOthers()
    {
        var notifier = new SupportTypingNotifier();
        var reachedSecond = false;
        notifier.Typing += (_, _) => throw new InvalidOperationException("host handler is broken");
        notifier.Typing += (_, _) => reachedSecond = true;

        var exception = Record.Exception(() => notifier.Notify(new SupportCaseTypingEventArgs { TeamKey = TeamA, CaseId = "case-1", Identity = Alice, AsSupport = false }));

        Assert.Null(exception);
        Assert.True(reachedSecond);
    }

    [Fact]
    public void AnUnsubscribedHandler_IsNotCalled()
    {
        var notifier = new SupportTypingNotifier();
        var calls = 0;
        void Handler(object sender, SupportCaseTypingEventArgs e) => calls++;

        notifier.Typing += Handler;
        notifier.Notify(new SupportCaseTypingEventArgs { TeamKey = TeamA, CaseId = "case-1", Identity = Alice, AsSupport = false });
        notifier.Typing -= Handler;
        notifier.Notify(new SupportCaseTypingEventArgs { TeamKey = TeamA, CaseId = "case-1", Identity = Alice, AsSupport = false });

        Assert.Equal(1, calls);
    }

    private sealed class World
    {
        private readonly InMemorySupportCaseStore _store = new();
        private readonly SupportTypingNotifier _notifier = new();

        public World() => _notifier.Typing += (_, e) => Seen.Add(e);

        public List<SupportCaseTypingEventArgs> Seen { get; } = [];

        public CollectingAuditLogger Audit { get; } = new();

        public ISupportCaseService Member(string subject, string displayName = null) => Build(Authorizer(subject, displayName, TeamA, [], []));

        public ISupportCaseService SupportStaff(params string[] systemScopes) => Build(Authorizer(Staff, null, null, [], systemScopes));

        private ISupportCaseService Build(TeamAuthorizer authorizer) => new AuditingSupportCaseServiceDecorator(
            new AuthorizationSupportCaseServiceDecorator(
                new SupportCaseService(_store, authorizer, TimeProvider.System, typingNotifier: _notifier),
                authorizer),
            new CompositeAuditLogger([Audit], Options.Create(new AuditOptions())),
            new AuditEntryFactory(null));
    }

    private static TeamAuthorizer Authorizer(string subject, string displayName, string memberOfTeam, string[] teamScopes, string[] systemScopes)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, displayName ?? subject)
        };

        if (memberOfTeam != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, memberOfTeam));
        claims.AddRange(teamScopes.Select(s => new Claim(TeamClaimTypes.Scope, s)));
        claims.AddRange(systemScopes.Select(s => new Claim(TeamClaimTypes.SystemScope, s)));

        return new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
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
