using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Whether the rest of a team may read a member's support case: always, as today, or as its author decides.
/// </summary>
/// <remarks>
/// <b>Team readers are the only population this changes.</b> The author keeps reaching their own case, and
/// the product's own support staff keep reaching every case, whatever the case says — so each test that
/// takes access away from a team reader also shows who still has it.
/// </remarks>
public class SupportCaseVisibilityTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Bob = "bob-subject";
    private const string Staff = "staff-subject";

    [Fact]
    public async Task TeamWide_ATeamReaderReadsListsAndCountsEveryCase_AsBefore()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Alice_(store, SupportCaseVisibilityMode.TeamWide).RaiseCaseAsync(TeamA, "Subject", "Body");

        var reader = TeamReader(store, SupportCaseVisibilityMode.TeamWide);

        Assert.Null(raised.Visibility);
        Assert.Equal(raised.Id, (await reader.GetCaseAsync(TeamA, raised.Id)).Id);
        Assert.Single((await reader.GetCasesAsync(TeamA)).Items);
        Assert.Equal(1, await reader.GetAwaitingSupportCountAsync(TeamA));
    }

    [Fact]
    public async Task TeamWide_SettingVisibilityIsRefused()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Alice_(store, SupportCaseVisibilityMode.TeamWide);
        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => alice.SetCaseVisibilityAsync(TeamA, raised.Id, SupportCaseVisibility.Private));
    }

    [Fact]
    public async Task PerCase_ANewCaseStartsPrivate()
    {
        var store = new InMemorySupportCaseStore();

        var raised = await Alice_(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        Assert.Equal(SupportCaseVisibility.Private, raised.Visibility);
        Assert.Equal(SupportCaseVisibility.Private, (await Alice_(store).GetCaseAsync(TeamA, raised.Id)).Visibility);
    }

    [Fact]
    public async Task PerCase_AHostCanStartEveryCaseSharedWithTheTeam()
    {
        var store = new InMemorySupportCaseStore();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Alice), new(ClaimTypes.Name, Alice), new(TeamClaimTypes.TeamKey, TeamA) };
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
        var service = new SupportCaseService(store, authorizer, TimeProvider.System,
            visibilityMode: SupportCaseVisibilityMode.PerCase, defaultVisibility: SupportCaseVisibility.Team);

        var raised = await service.RaiseCaseAsync(TeamA, "Subject", "Body");

        Assert.Equal(SupportCaseVisibility.Team, raised.Visibility);
        Assert.Equal(raised.Id, (await TeamReader(store).GetCaseAsync(TeamA, raised.Id)).Id);
    }

    [Fact]
    public async Task PerCase_ATeamReaderCannotReadListOrCountAPrivateCase()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Alice_(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var reader = TeamReader(store);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetCaseAsync(TeamA, raised.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetMessagesAsync(TeamA, raised.Id));
        Assert.Empty((await reader.GetCasesAsync(TeamA)).Items);
        Assert.Equal(0, await reader.GetAwaitingSupportCountAsync(TeamA));
    }

    [Fact]
    public async Task PerCase_ATeamManagerCannotAnswerOrCloseAPrivateCase()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Alice_(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var manager = Member(store, Bob, SupportCaseVisibilityMode.PerCase, SupportScopes.Manage);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.ReplyToCaseAsync(TeamA, raised.Id, "Hi."));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.CloseCaseAsync(TeamA, raised.Id));
    }

    [Fact]
    public async Task PerCase_ATeamReaderReadsOnceShared_AndNotAfterItIsMadePrivateAgain()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Alice_(store);
        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");
        var reader = TeamReader(store);

        await alice.SetCaseVisibilityAsync(TeamA, raised.Id, SupportCaseVisibility.Team);

        Assert.Equal(raised.Id, (await reader.GetCaseAsync(TeamA, raised.Id)).Id);
        Assert.Single((await reader.GetCasesAsync(TeamA)).Items);
        Assert.Equal(1, await reader.GetAwaitingSupportCountAsync(TeamA));

        await alice.SetCaseVisibilityAsync(TeamA, raised.Id, SupportCaseVisibility.Private);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetCaseAsync(TeamA, raised.Id));
        Assert.Empty((await reader.GetCasesAsync(TeamA)).Items);
        Assert.Equal(0, await reader.GetAwaitingSupportCountAsync(TeamA));
    }

    [Fact]
    public async Task PerCase_TheAuthorKeepsTheirPrivateCase_InTheirOwnListAndInTheTeamList()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Alice_(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var aliceAsReader = Member(store, Alice, SupportCaseVisibilityMode.PerCase, SupportScopes.Read);

        Assert.Single((await aliceAsReader.GetMyCasesAsync(TeamA)).Items);
        Assert.Equal(raised.Id, Assert.Single((await aliceAsReader.GetCasesAsync(TeamA)).Items).Id);
    }

    [Fact]
    public async Task PerCase_SupportStaffReachAPrivateCase()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Alice_(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var staff = SupportStaff(store, SystemSupportScopes.AllManage);

        Assert.Equal(raised.Id, (await staff.GetCaseAsync(TeamA, raised.Id)).Id);
        Assert.Single((await staff.GetCasesAsync(TeamA)).Items);
        Assert.Single((await staff.GetCasesAcrossTeamsAsync()).Items);
        Assert.Equal(1, await staff.GetAwaitingSupportCountAsync(TeamA));
        await staff.ReplyToCaseAsync(TeamA, raised.Id, "Looking into it.");
    }

    [Fact]
    public async Task PerCase_NobodyButTheAuthorChangesIt()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Alice_(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var manager = Member(store, Bob, SupportCaseVisibilityMode.PerCase, SupportScopes.Read, SupportScopes.Manage);
        var staff = SupportStaff(store, SystemSupportScopes.AllManage);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => manager.SetCaseVisibilityAsync(TeamA, raised.Id, SupportCaseVisibility.Team));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => staff.SetCaseVisibilityAsync(TeamA, raised.Id, SupportCaseVisibility.Team));

        Assert.Equal(SupportCaseVisibility.Private, (await Alice_(store).GetCaseAsync(TeamA, raised.Id)).Visibility);
    }

    [Fact]
    public async Task PerCase_AClosedCasesVisibilityCannotChange()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Alice_(store);
        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");
        await alice.CloseCaseAsync(TeamA, raised.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => alice.SetCaseVisibilityAsync(TeamA, raised.Id, SupportCaseVisibility.Team));

        Assert.Equal(SupportCaseVisibility.Private, (await alice.GetCaseAsync(TeamA, raised.Id)).Visibility);
    }

    /// <summary>
    /// A case written before visibility existed carries none. Under per-case visibility it reads as private,
    /// because nobody ever chose to share it.
    /// </summary>
    [Fact]
    public async Task PerCase_ACaseWithNoVisibilityReadsAsPrivate()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Alice_(store, SupportCaseVisibilityMode.TeamWide).RaiseCaseAsync(TeamA, "Subject", "Body");

        var reader = TeamReader(store);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetCaseAsync(TeamA, raised.Id));
        Assert.Empty((await reader.GetCasesAsync(TeamA)).Items);
    }

    [Fact]
    public async Task PerCase_AnotherMemberWithoutAScope_IsRefusedWhateverTheVisibility()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Alice_(store);
        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");
        await alice.SetCaseVisibilityAsync(TeamA, raised.Id, SupportCaseVisibility.Team);

        var bob = Member(store, Bob, SupportCaseVisibilityMode.PerCase);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bob.GetCaseAsync(TeamA, raised.Id));
    }

    [Fact]
    public async Task AStoreWithoutVisibility_RefusesTheChange_AndListsNothingToTeamReaders()
    {
        var inner = new InMemorySupportCaseStore();
        var raised = await Alice_(inner).RaiseCaseAsync(TeamA, "Subject", "Body");

        ISupportCaseStore written = new StoreWithoutReopen(inner);

        Assert.False(await written.TrySetVisibilityAsync(TeamA, raised.Id, SupportCaseVisibility.Team));
        Assert.Empty((await written.GetCasesVisibleToTeamAsync(TeamA, Bob, null, 20)).Items);
        Assert.Equal(0, await written.GetAwaitingSupportCountVisibleToTeamAsync(TeamA, Bob));
    }

    private static ISupportCaseService Alice_(InMemorySupportCaseStore store, SupportCaseVisibilityMode mode = SupportCaseVisibilityMode.PerCase)
        => Member(store, Alice, mode);

    private static ISupportCaseService TeamReader(InMemorySupportCaseStore store, SupportCaseVisibilityMode mode = SupportCaseVisibilityMode.PerCase)
        => Member(store, Bob, mode, SupportScopes.Read);

    private static ISupportCaseService Member(InMemorySupportCaseStore store, string subject, SupportCaseVisibilityMode mode, params string[] teamScopes)
        => Build(store, subject, mode, memberOfTeam: TeamA, teamScopes: teamScopes);

    private static ISupportCaseService SupportStaff(InMemorySupportCaseStore store, params string[] systemScopes)
        => Build(store, Staff, SupportCaseVisibilityMode.PerCase, systemScopes: systemScopes);

    private static ISupportCaseService Build(
        InMemorySupportCaseStore store,
        string subject,
        SupportCaseVisibilityMode mode,
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
            new SupportCaseService(store, authorizer, TimeProvider.System, visibilityMode: mode),
            authorizer,
            mode);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
