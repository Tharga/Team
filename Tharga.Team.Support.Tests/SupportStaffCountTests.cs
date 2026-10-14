using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// What a host shows support staff as waiting for them: cases across every team, and the unassigned queue.
/// </summary>
public class SupportStaffCountTests
{
    private const string TeamA = "team-a";
    private const string TeamB = "team-b";
    private const string Alice = "alice-subject";
    private const string Staff = "staff-subject";

    [Fact]
    public async Task AllRead_CountsCasesAwaitingSupportInEveryTeam()
    {
        var store = new InMemorySupportCaseStore();
        await Member(store, TeamA).RaiseCaseAsync(TeamA, "From A", "Body");
        await Member(store, TeamB).RaiseCaseAsync(TeamB, "From B", "Body");

        Assert.Equal(2, await SupportStaff(store, SystemSupportScopes.AllRead).GetAwaitingSupportCountAcrossTeamsAsync());
    }

    [Fact]
    public async Task AllManage_CountsCasesAwaitingSupportInEveryTeam()
    {
        var store = new InMemorySupportCaseStore();
        await Member(store, TeamA).RaiseCaseAsync(TeamA, "From A", "Body");

        Assert.Equal(1, await SupportStaff(store, SystemSupportScopes.AllManage).GetAwaitingSupportCountAcrossTeamsAsync());
    }

    [Fact]
    public async Task TheCountAcrossTeams_LeavesOutAnsweredClosedAndUnassignedCases()
    {
        var store = await StoreWithOneUnassignedCase();
        var alice = Member(store, TeamA);
        var answered = await alice.RaiseCaseAsync(TeamA, "Answered", "Body");
        var closed = await alice.RaiseCaseAsync(TeamA, "Closed", "Body");
        await alice.RaiseCaseAsync(TeamA, "Waiting", "Body");

        var staff = SupportStaff(store, SystemSupportScopes.AllManage);
        await staff.ReplyToCaseAsync(TeamA, answered.Id, "Done.");
        await staff.CloseCaseAsync(TeamA, closed.Id);

        Assert.Equal(1, await staff.GetAwaitingSupportCountAcrossTeamsAsync());
    }

    [Fact]
    public async Task TheCountAcrossTeams_IsRefusedWithoutTheCrossTeamGrant()
    {
        var store = new InMemorySupportCaseStore();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Member(store, TeamA, SupportScopes.Read, SupportScopes.Manage).GetAwaitingSupportCountAcrossTeamsAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => SupportStaff(store, SystemSupportScopes.Read, SystemSupportScopes.Manage).GetAwaitingSupportCountAcrossTeamsAsync());
    }

    [Fact]
    public async Task UnassignedRead_CountsTheUnassignedQueue_AndNothingInATeam()
    {
        var store = await StoreWithOneUnassignedCase();
        await Member(store, TeamA).RaiseCaseAsync(TeamA, "From A", "Body");

        Assert.Equal(1, await SupportStaff(store, SystemSupportScopes.Read).GetAwaitingUnassignedCountAsync());
        Assert.Equal(1, await SupportStaff(store, SystemSupportScopes.Manage).GetAwaitingUnassignedCountAsync());
    }

    [Fact]
    public async Task TheUnassignedCount_IsRefusedToTheCrossTeamGrant()
    {
        var store = await StoreWithOneUnassignedCase();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => SupportStaff(store, SystemSupportScopes.AllRead, SystemSupportScopes.AllManage).GetAwaitingUnassignedCountAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Member(store, TeamA, SupportScopes.Read, SupportScopes.Manage).GetAwaitingUnassignedCountAsync());
    }

    [Fact]
    public async Task AStoreWithoutTheCounts_AnswersZero()
    {
        var inner = await StoreWithOneUnassignedCase();
        await Member(inner, TeamA).RaiseCaseAsync(TeamA, "Subject", "Body");

        ISupportCaseStore written = new StoreWithoutReopen(inner);

        Assert.Equal(1, await inner.GetAwaitingSupportCountAcrossTeamsAsync());
        Assert.Equal(1, await inner.GetAwaitingUnassignedCountAsync());
        Assert.Equal(0, await written.GetAwaitingSupportCountAcrossTeamsAsync());
        Assert.Equal(0, await written.GetAwaitingUnassignedCountAsync());
    }

    private static ISupportCaseService Member(InMemorySupportCaseStore store, string team, params string[] teamScopes)
        => Build(store, Alice, memberOfTeam: team, teamScopes: teamScopes);

    private static ISupportCaseService SupportStaff(InMemorySupportCaseStore store, params string[] systemScopes)
        => Build(store, Staff, systemScopes: systemScopes);

    private static ISupportCaseService Build(
        InMemorySupportCaseStore store,
        string subject,
        string memberOfTeam = null,
        string[] teamScopes = null,
        string[] systemScopes = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, subject)
        };

        if (memberOfTeam != null)
            claims.Add(new Claim(TeamClaimTypes.TeamKey, memberOfTeam));

        claims.AddRange((teamScopes ?? []).Select(s => new Claim(TeamClaimTypes.Scope, s)));
        claims.AddRange((systemScopes ?? []).Select(s => new Claim(TeamClaimTypes.SystemScope, s)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(store, authorizer, TimeProvider.System),
            authorizer);
    }

    private static async Task<InMemorySupportCaseStore> StoreWithOneUnassignedCase()
    {
        var store = new InMemorySupportCaseStore();

        await store.AddCaseAsync(new SupportCase
        {
            Id = "case-unassigned",
            TeamKey = null,
            AuthorIdentity = null,
            Subject = "Cannot sign in",
            AuthorName = "stranger@example.com",
            Status = SupportCaseStatus.Open,
            CreatedAt = DateTime.UtcNow,
            MessageCount = 1
        }, new SupportMessage
        {
            Sequence = 1,
            Kind = SupportMessageKind.User,
            AuthorName = "stranger@example.com",
            Body = "It says my key expired.",
            SentAt = DateTime.UtcNow
        });

        return store;
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
