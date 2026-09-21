using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The product's own support staff, answering a case that belongs to a customer's team.
/// </summary>
/// <remarks>
/// <b>The caller here is a member of nothing, and that is the point.</b> Support staff belong to none of
/// the customers' teams; adding them to each one does not scale and would list product staff in tenant
/// membership lists. So every test below builds a principal with no team claim at all — if membership
/// crept back in front of the cross-team check, all of them fail.
/// <para>
/// <b>The two system pairs are kept apart in both directions.</b> Reaching every team and reaching the
/// cases no team owns are different grants over different populations, and a caller holding one must gain
/// nothing of the other. Testing only one direction would miss exactly half of that.
/// </para>
/// </remarks>
public class CrossTeamSupportAccessTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Staff = "staff-subject";

    [Fact]
    public async Task AllManage_ReadsAndAnswersACaseInATeamItIsNoMemberOf()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Cannot sign in", "It says my key expired.");

        var staff = SupportStaff(store, SystemSupportScopes.AllManage);

        Assert.Equal(raised.Id, (await staff.GetCaseAsync(TeamA, raised.Id)).Id);

        await staff.ReplyToCaseAsync(TeamA, raised.Id, "Looking into it.");
        await staff.CloseCaseAsync(TeamA, raised.Id);
        await staff.ReopenCaseAsync(TeamA, raised.Id);
        await staff.RequestHumanAsync(TeamA, raised.Id);
    }

    /// <summary>
    /// The answer is attributable to the person who wrote it — which is why access simulation was not an
    /// acceptable way round this gap.
    /// </summary>
    [Fact]
    public async Task AStaffReply_IsAttributedToTheStaffMember_NotToAMember()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        await StaffNamed(store, SystemSupportScopes.AllManage, "Sam Support")
            .ReplyToCaseAsync(TeamA, raised.Id, "Looking into it.");

        var messages = await Member(store, SupportScopes.Read).GetMessagesAsync(TeamA, raised.Id);

        Assert.Equal(Staff, messages.Items[^1].AuthorIdentity);
        Assert.Equal("Sam Support", messages.Items[^1].AuthorName);
    }

    [Fact]
    public async Task AllRead_Reads_ButIsRefusedEveryWriteVerb()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var staff = SupportStaff(store, SystemSupportScopes.AllRead);

        Assert.Equal(raised.Id, (await staff.GetCaseAsync(TeamA, raised.Id)).Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staff.ReplyToCaseAsync(TeamA, raised.Id, "No."));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staff.CloseCaseAsync(TeamA, raised.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staff.ReopenCaseAsync(TeamA, raised.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staff.RequestHumanAsync(TeamA, raised.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staff.RunAssistantAsync(TeamA, raised.Id));
    }

    /// <summary>
    /// The scope is registered as granting "read <b>and list</b> support cases in any team", so the listing
    /// has to honour it or the catalogue entry a host reads before granting it is untrue.
    /// </summary>
    [Fact]
    public async Task AllRead_ListsATeamsCases_AndCountsThoseAwaitingSupport()
    {
        var store = new InMemorySupportCaseStore();
        await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var staff = SupportStaff(store, SystemSupportScopes.AllRead);

        Assert.Single((await staff.GetCasesAsync(TeamA)).Items);
        Assert.Equal(1, await staff.GetAwaitingSupportCountAsync(TeamA));
    }

    /// <summary>
    /// An unassigned case may concern a tenant this caller has nothing to do with, so the cross-team grant
    /// must stop at the edge of the teams.
    /// </summary>
    [Fact]
    public async Task TheCrossTeamGrant_ReachesNothingInTheUnassignedQueue()
    {
        var store = await StoreWithOneUnassignedCase();

        var staff = SupportStaff(store, SystemSupportScopes.AllManage);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staff.GetUnassignedCasesAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staff.GetCaseAsync(null, "case-unassigned"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staff.ReplyToCaseAsync(null, "case-unassigned", "No."));
    }

    /// <summary>The same separation, from the other side.</summary>
    [Fact]
    public async Task TheUnassignedGrant_ReachesNothingInATeam()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var queueOperator = SupportStaff(store, SystemSupportScopes.Read, SystemSupportScopes.Manage);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => queueOperator.GetCaseAsync(TeamA, raised.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => queueOperator.GetCasesAsync(TeamA));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => queueOperator.ReplyToCaseAsync(TeamA, raised.Id, "No."));
    }

    /// <summary>
    /// Assignment decides which tenant an unassigned case and its whole transcript become part of, so it
    /// stays with the unassigned queue rather than travelling with "may answer anywhere".
    /// </summary>
    [Fact]
    public async Task AllManage_DoesNotGrantAssignment()
    {
        var store = await StoreWithOneUnassignedCase();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => SupportStaff(store, SystemSupportScopes.AllManage).AssignCaseAsync("case-unassigned", TeamA));
    }

    /// <summary>
    /// The reordering put the cross-team check in front of membership. This is the guard that it did not
    /// displace the author, who holds nothing at all.
    /// </summary>
    [Fact]
    public async Task TheAuthor_StillReachesTheirOwnCase_HoldingNoScope()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Member(store);
        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");

        await alice.ReplyToCaseAsync(TeamA, raised.Id, "Any news?");

        Assert.Equal(2, (await alice.GetMessagesAsync(TeamA, raised.Id)).Items.Length);
    }

    private static ISupportCaseService Member(InMemorySupportCaseStore store, params string[] teamScopes)
        => Build(store, Alice, memberOfTeam: TeamA, teamScopes: teamScopes);

    private static ISupportCaseService SupportStaff(InMemorySupportCaseStore store, params string[] systemScopes)
        => Build(store, Staff, systemScopes: systemScopes);

    private static ISupportCaseService StaffNamed(InMemorySupportCaseStore store, string systemScope, string displayName)
        => Build(store, Staff, systemScopes: [systemScope], displayName: displayName);

    private static ISupportCaseService Build(
        InMemorySupportCaseStore store,
        string subject,
        string memberOfTeam = null,
        string[] teamScopes = null,
        string[] systemScopes = null,
        string displayName = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, displayName ?? subject)
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
