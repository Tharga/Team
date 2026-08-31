using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Unread state, and the two counts a host renders an indicator from.
/// </summary>
/// <remarks>
/// <b>Two of these are the mistakes that make an indicator useless rather than wrong.</b> An indicator that
/// never lights again after the first read is ignored because it is stale; one that lights every time the
/// user types is ignored because it is noise. Both compile perfectly.
/// </remarks>
public class SupportCaseReadStateTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice";
    private const string Bob = "bob";

    [Fact]
    public async Task ANewCase_IsUnreadToItsAuthorUntilTheyOpenIt()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Build(TeamA, Alice, store);

        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");
        Assert.Equal(1, await alice.GetMyUnreadCountAsync(TeamA));

        await alice.MarkReadAsync(TeamA, raised.Id);

        Assert.Equal(0, await alice.GetMyUnreadCountAsync(TeamA));
    }

    /// <summary>
    /// The case a "read once" flag gets wrong: reading is not a permanent state, it is a position in a
    /// conversation that keeps moving.
    /// </summary>
    [Fact]
    public async Task ARelyArrivingAfterIRead_MakesTheCaseUnreadAgain()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Build(TeamA, Alice, store);
        var agent = Build(TeamA, Bob, store, [SupportScopes.Read, SupportScopes.Manage]);

        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");
        await alice.MarkReadAsync(TeamA, raised.Id);
        Assert.Equal(0, await alice.GetMyUnreadCountAsync(TeamA));

        await agent.ReplyToCaseAsync(TeamA, raised.Id, "Looking into it.");

        Assert.Equal(1, await alice.GetMyUnreadCountAsync(TeamA));
    }

    /// <summary>
    /// The mistake that lights an indicator every time somebody types. Replying is reading — the user wrote
    /// the newest entry themselves.
    /// </summary>
    [Fact]
    public async Task MyOwnReply_DoesNotMakeTheCaseUnreadToMe()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Build(TeamA, Alice, store);

        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");
        await alice.MarkReadAsync(TeamA, raised.Id);

        await alice.ReplyToCaseAsync(TeamA, raised.Id, "Any news?");
        await alice.MarkReadAsync(TeamA, raised.Id);

        Assert.Equal(0, await alice.GetMyUnreadCountAsync(TeamA));
    }

    [Fact]
    public async Task MarkingReadTwice_ChangesNothing()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Build(TeamA, Alice, store);

        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");

        await alice.MarkReadAsync(TeamA, raised.Id);
        await alice.MarkReadAsync(TeamA, raised.Id);

        Assert.Equal(0, await alice.GetMyUnreadCountAsync(TeamA));
    }

    /// <summary>
    /// Two people on one case must not share a marker, or one reading it would clear the other's indicator.
    /// </summary>
    [Fact]
    public async Task TwoPeopleOnOneCase_HaveIndependentUnreadState()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Build(TeamA, Alice, store);
        var bob = Build(TeamA, Bob, store, [SupportScopes.Read]);

        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");

        await bob.MarkReadAsync(TeamA, raised.Id);

        // Bob reading it must not clear Alice's.
        Assert.Equal(1, await alice.GetMyUnreadCountAsync(TeamA));
    }

    [Fact]
    public async Task ACaseAwaitsSupport_WhileItsNewestEntryIsFromItsAuthor()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Build(TeamA, Alice, store);
        var agent = Build(TeamA, Bob, store, [SupportScopes.Read, SupportScopes.Manage]);

        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");
        Assert.Equal(1, await agent.GetAwaitingSupportCountAsync(TeamA));

        await agent.ReplyToCaseAsync(TeamA, raised.Id, "Looking into it.");
        Assert.Equal(0, await agent.GetAwaitingSupportCountAsync(TeamA));

        await alice.ReplyToCaseAsync(TeamA, raised.Id, "Still broken.");
        Assert.Equal(1, await agent.GetAwaitingSupportCountAsync(TeamA));
    }

    [Fact]
    public async Task AClosedCase_DoesNotAwaitSupport()
    {
        var store = new InMemorySupportCaseStore();
        var alice = Build(TeamA, Alice, store);
        var agent = Build(TeamA, Bob, store, [SupportScopes.Read, SupportScopes.Manage]);

        var raised = await alice.RaiseCaseAsync(TeamA, "Subject", "Body");
        await alice.CloseCaseAsync(TeamA, raised.Id);

        Assert.Equal(0, await agent.GetAwaitingSupportCountAsync(TeamA));
    }

    /// <summary>
    /// The awaiting count spans everybody's cases, so it is exactly as privileged as reading them.
    /// </summary>
    [Fact]
    public async Task WithoutSupportRead_TheAwaitingCountIsRefused()
    {
        var store = new InMemorySupportCaseStore();
        await Build(TeamA, Alice, store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var member = Build(TeamA, Bob, store);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => member.GetAwaitingSupportCountAsync(TeamA));
    }

    /// <summary>
    /// Marking read is a write on a case, so it must be exactly as hard as reading that case.
    /// </summary>
    [Fact]
    public async Task AMemberWhoCouldNotReadACase_CannotMarkItRead()
    {
        var store = new InMemorySupportCaseStore();
        var raised = await Build(TeamA, Alice, store).RaiseCaseAsync(TeamA, "Subject", "Body");

        var bob = Build(TeamA, Bob, store);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bob.MarkReadAsync(TeamA, raised.Id));
    }

    private static ISupportCaseService Build(string teamKey, string subject, InMemorySupportCaseStore store, string[] scopes = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, subject),
            new(TeamClaimTypes.TeamKey, teamKey)
        };

        claims.AddRange((scopes ?? []).Select(s => new Claim(TeamClaimTypes.Scope, s)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(store, authorizer, TimeProvider.System),
            authorizer);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
