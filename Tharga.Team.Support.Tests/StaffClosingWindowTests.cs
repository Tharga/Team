using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Who may close a case, and when a closed case opens again by itself.
/// </summary>
/// <remarks>
/// <b>Closing is the customer's word that the problem is solved.</b> With a window set, the product's own staff
/// may close only once they answered last and the customer stayed silent for that long; the author closes
/// whenever they like. And because closing must never swallow what somebody writes afterwards, writing to a
/// closed case opens it again rather than being refused.
/// </remarks>
public class StaffClosingWindowTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Staff = "staff-subject";

    private static readonly TimeSpan Window = TimeSpan.FromDays(14);

    [Fact]
    public async Task TheAuthor_ClosesAtAnyTime()
    {
        var (store, time) = (new InMemorySupportCaseStore(), new MovableTime());
        var alice = Member(store, time);
        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");

        await alice.CloseCaseAsync(TeamA, raised.Id);

        Assert.Equal(SupportCaseStatus.Closed, (await alice.GetCaseAsync(TeamA, raised.Id)).Status);
    }

    [Fact]
    public async Task Staff_CannotClose_WhileTheCustomerWroteLast()
    {
        var (store, time) = (new InMemorySupportCaseStore(), new MovableTime());
        var raised = await Member(store, time).RaiseCaseAsync(TeamA, "Subject", "Body");

        time.Advance(Window + TimeSpan.FromDays(1));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => SupportStaff(store, time).CloseCaseAsync(TeamA, raised.Id));
    }

    [Fact]
    public async Task Staff_CannotClose_BeforeTheWindowHasPassedSinceTheirAnswer()
    {
        var (store, time) = (new InMemorySupportCaseStore(), new MovableTime());
        var raised = await Member(store, time).RaiseCaseAsync(TeamA, "Subject", "Body");
        var staff = SupportStaff(store, time);
        await staff.ReplyToCaseAsync(TeamA, raised.Id, "This is fixed now.");

        time.Advance(Window - TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staff.CloseCaseAsync(TeamA, raised.Id));
    }

    [Fact]
    public async Task Staff_Close_OnceTheWindowHasPassedSinceTheirAnswer()
    {
        var (store, time) = (new InMemorySupportCaseStore(), new MovableTime());
        var raised = await Member(store, time).RaiseCaseAsync(TeamA, "Subject", "Body");
        var staff = SupportStaff(store, time);
        await staff.ReplyToCaseAsync(TeamA, raised.Id, "This is fixed now.");

        time.Advance(Window);

        await staff.CloseCaseAsync(TeamA, raised.Id);

        Assert.Equal(SupportCaseStatus.Closed, (await staff.GetCaseAsync(TeamA, raised.Id)).Status);
    }

    [Fact]
    public async Task WithoutAWindow_StaffCloseAtAnyTime()
    {
        var (store, time) = (new InMemorySupportCaseStore(), new MovableTime());
        var raised = await Member(store, time).RaiseCaseAsync(TeamA, "Subject", "Body");

        await StaffWithWindow(store, time, null).CloseCaseAsync(TeamA, raised.Id);

        Assert.Equal(SupportCaseStatus.Closed, (await store.GetCaseAsync(TeamA, raised.Id)).Status);
    }

    [Fact]
    public async Task WritingToAClosedCase_OpensItAgain()
    {
        var (store, time) = (new InMemorySupportCaseStore(), new MovableTime());
        var alice = Member(store, time);
        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");
        await alice.CloseCaseAsync(TeamA, raised.Id);

        await alice.ReplyToCaseAsync(TeamA, raised.Id, "It broke again.");

        var reopened = await alice.GetCaseAsync(TeamA, raised.Id);
        var messages = await alice.GetMessagesAsync(TeamA, raised.Id);

        Assert.Equal(SupportCaseStatus.Open, reopened.Status);
        Assert.Equal("It broke again.", messages.Items[^1].Body);
        Assert.Contains(messages.Items, x => x.Kind == SupportMessageKind.System && x.Body.Contains("reopened", StringComparison.OrdinalIgnoreCase));
    }

    private static ISupportCaseService Member(InMemorySupportCaseStore store, MovableTime time)
        => Build(store, time, Alice, memberOfTeam: TeamA, window: Window);

    private static ISupportCaseService SupportStaff(InMemorySupportCaseStore store, MovableTime time)
        => StaffWithWindow(store, time, Window);

    private static ISupportCaseService StaffWithWindow(InMemorySupportCaseStore store, MovableTime time, TimeSpan? window)
        => Build(store, time, Staff, systemScopes: [SystemSupportScopes.AllRead, SystemSupportScopes.AllManage], window: window);

    private static ISupportCaseService Build(InMemorySupportCaseStore store, MovableTime time, string subject, string memberOfTeam = null, string[] systemScopes = null, TimeSpan? window = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject), new(ClaimTypes.Name, subject) };

        if (memberOfTeam != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, memberOfTeam));
        claims.AddRange((systemScopes ?? []).Select(s => new Claim(TeamClaimTypes.SystemScope, s)));

        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(store, authorizer, time),
            authorizer,
            staffCloseAfter: window,
            timeProvider: time);
    }

    private sealed class MovableTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
