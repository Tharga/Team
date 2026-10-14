using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// What a member writes never counts as unread for them; only somebody else's answer does.
/// </summary>
public class SupportOwnMessageReadTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Staff = "staff-subject";

    [Fact]
    public async Task RaisingACase_LeavesNothingUnreadForItsAuthor()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Member(store);

        await alice.RaiseCaseAsync(TeamA, "Subject", "Body");

        Assert.Equal(0, await alice.GetMyUnreadCountAsync(TeamA));
    }

    [Fact]
    public async Task ReplyingToYourOwnCase_LeavesNothingUnread()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Member(store);
        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");

        await alice.ReplyToCaseAsync(TeamA, raised.Id, "Any news?");

        Assert.Equal(0, await alice.GetMyUnreadCountAsync(TeamA));
    }

    [Fact]
    public async Task AnAnswerFromSupport_IsUnreadUntilTheAuthorReadsIt()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Member(store);
        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");

        await SupportStaff(store).ReplyToCaseAsync(TeamA, raised.Id, "Looking into it.");

        Assert.Equal(1, await alice.GetMyUnreadCountAsync(TeamA));

        await alice.MarkReadAsync(TeamA, raised.Id);

        Assert.Equal(0, await alice.GetMyUnreadCountAsync(TeamA));
    }

    private static ISupportCaseService Member(InMemorySupportCaseStore store)
        => Build(store, Alice, memberOfTeam: TeamA);

    private static ISupportCaseService SupportStaff(InMemorySupportCaseStore store)
        => Build(store, Staff, systemScopes: [SystemSupportScopes.AllManage]);

    private static ISupportCaseService Build(InMemorySupportCaseStore store, string subject, string memberOfTeam = null, string[] systemScopes = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, subject)
        };

        if (memberOfTeam != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, memberOfTeam));
        claims.AddRange((systemScopes ?? []).Select(s => new Claim(TeamClaimTypes.SystemScope, s)));

        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        return new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(store, authorizer, TimeProvider.System), authorizer);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
