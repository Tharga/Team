using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// An answer written from the support side is support's whoever writes it — which is what a person testing
/// their own case from the support page, or a colleague who also raised one, both need.
/// </summary>
public class SupportAnswerTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice";

    [Fact]
    public async Task AnAnswer_TakesTheCaseOutOfTheWaitingCount_EvenWrittenByItsAuthor()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");
        var aliceAsStaff = Build(store, Alice, TeamA, systemScopes: [SystemSupportScopes.AllManage]);

        Assert.Equal(1, await aliceAsStaff.GetAwaitingSupportCountAcrossTeamsAsync());

        await aliceAsStaff.AnswerCaseAsync(TeamA, raised.Id, "Answered from the support page.");

        Assert.Equal(0, await aliceAsStaff.GetAwaitingSupportCountAcrossTeamsAsync());
    }

    [Fact]
    public async Task AnAnswer_IsUnreadForWhoRaisedTheCase_EvenWhenTheyWroteIt()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        await Build(store, Alice, TeamA, systemScopes: [SystemSupportScopes.AllManage]).AnswerCaseAsync(TeamA, raised.Id, "Answer.");

        Assert.Equal(1, await Member(store).GetMyUnreadCountAsync(TeamA));
    }

    [Fact]
    public async Task AnAnswer_IsMarkedAsSupports()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        await Build(store, "sam", systemScopes: [SystemSupportScopes.AllManage]).AnswerCaseAsync(TeamA, raised.Id, "Answer.");

        var messages = await store.GetMessagesAsync(TeamA, raised.Id, null, 50);
        Assert.True(messages.Items[^1].AsSupport);
        Assert.False(messages.Items[0].AsSupport);
    }

    [Fact]
    public async Task HavingRaisedTheCase_DoesNotLetYouAnswerItAsSupport()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Member(store);
        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => alice.AnswerCaseAsync(TeamA, raised.Id, "Clearing my own case."));
    }

    [Fact]
    public async Task ATeamManager_AnswersASharedCase()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Member(store).RaiseCaseAsync(TeamA, "Subject", "Body");

        await Build(store, "bob", TeamA, teamScopes: [SupportScopes.Manage]).AnswerCaseAsync(TeamA, raised.Id, "Answer.");

        Assert.Equal(2, (await store.GetMessagesAsync(TeamA, raised.Id, null, 50)).Items.Length);
    }

    [Fact]
    public async Task EachOfMyCases_ReportsHowManyEntriesIHaveNotRead()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Member(store);
        var first = await alice.RaiseCaseAsync(TeamA, "First", "Body");
        var second = await alice.RaiseCaseAsync(TeamA, "Second", "Body");
        var staff = Build(store, "sam", systemScopes: [SystemSupportScopes.AllManage]);

        await staff.AnswerCaseAsync(TeamA, first.Id, "One.");
        await staff.AnswerCaseAsync(TeamA, first.Id, "Two.");

        var unread = await alice.GetMyUnreadByCaseAsync(TeamA);

        Assert.Equal(2, Assert.Single(unread, x => x.CaseId == first.Id).Count);
        Assert.DoesNotContain(unread, x => x.CaseId == second.Id);
    }

    private static ISupportCaseService Member(InMemorySupportCaseStore store) => Build(store, Alice, TeamA);

    private static ISupportCaseService Build(InMemorySupportCaseStore store, string subject, string memberOfTeam = null, string[] teamScopes = null, string[] systemScopes = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, subject)
        };

        if (memberOfTeam != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, memberOfTeam));
        claims.AddRange((teamScopes ?? []).Select(s => new Claim(TeamClaimTypes.Scope, s)));
        claims.AddRange((systemScopes ?? []).Select(s => new Claim(TeamClaimTypes.SystemScope, s)));

        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        return new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(store, authorizer, TimeProvider.System), authorizer);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
