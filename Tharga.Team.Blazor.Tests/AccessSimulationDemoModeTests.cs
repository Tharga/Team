using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Demo mode — <see cref="AccessSimulationTargets.FromDemo"/>: keep the caller's team access exactly as it
/// is, drop their system-wide access.
/// </summary>
/// <remarks>
/// Built for a system user (e.g. a Developer) demonstrating the product: they select a team, toggle demo
/// mode, and the audience sees what an ordinary member of that team sees instead of the administrative
/// surface. Toggling it off restores what they really hold.
/// <para>
/// It is the one target that is not a replacement — the other four name someone else's access, this one
/// names the caller's own — so the assertions worth having are that nothing team-side moves and everything
/// system-side goes.
/// </para>
/// </remarks>
public class AccessSimulationDemoModeTests
{
    private const string TeamScope = "orders:read";
    private const string OtherTeamScope = "orders:write";
    private const string SystemScope = "teams:read";

    private static ClaimsIdentity DeveloperOnATeam()
        => new(
        [
            new Claim(TeamClaimTypes.Scope, TeamScope),
            new Claim(TeamClaimTypes.Scope, OtherTeamScope),
            new Claim(TeamClaimTypes.SystemScope, SystemScope),
            new Claim(TeamClaimTypes.AccessLevel, nameof(AccessLevel.Administrator)),
            new Claim(ClaimTypes.Role, Roles.Developer),
            new Claim(ClaimTypes.Role, $"Team{nameof(AccessLevel.Administrator)}")
        ], "Test");

    private static ClaimsIdentity ApplyDemo(ClaimsIdentity identity, params string[] ownScopes)
    {
        AccessSimulationFilter.Apply(new ClaimsPrincipal(identity), AccessSimulationTargets.FromDemo(ownScopes));
        return identity;
    }

    /// <summary>The point of the feature: the system half goes.</summary>
    [Fact]
    public void Demo_DropsSystemScopes()
    {
        var identity = ApplyDemo(DeveloperOnATeam(), TeamScope, OtherTeamScope);

        Assert.Empty(identity.FindAll(TeamClaimTypes.SystemScope));
    }

    /// <summary>
    /// The application role goes too. Without this a Developer keeps whatever the host gates on the role
    /// name, and the demo still shows the developer surface — the visible half of the complaint.
    /// </summary>
    [Fact]
    public void Demo_DropsTheApplicationRole()
    {
        var identity = ApplyDemo(DeveloperOnATeam(), TeamScope, OtherTeamScope);

        Assert.DoesNotContain(identity.FindAll(ClaimTypes.Role), c => c.Value == Roles.Developer);
    }

    /// <summary>
    /// <b>And nothing team-side moves.</b> This is what separates demo mode from the other four targets:
    /// it is not a de-escalation within the team, so every team scope the caller holds must survive. A
    /// target set narrower than their own access would quietly reduce the team access too, and the demo
    /// would show less than a real member sees — which is the failure that makes a demo misleading.
    /// </summary>
    [Fact]
    public void Demo_KeepsEveryTeamScope()
    {
        var identity = ApplyDemo(DeveloperOnATeam(), TeamScope, OtherTeamScope);

        var kept = identity.FindAll(TeamClaimTypes.Scope).Select(c => c.Value).ToArray();
        Assert.Contains(TeamScope, kept);
        Assert.Contains(OtherTeamScope, kept);
    }

    /// <summary>
    /// The team role and access level are untouched. <c>FromDemo</c> sets no access level precisely so the
    /// clamp never runs — passing one could lower the level the caller holds on the team, which is the
    /// opposite of showing what an ordinary member of *this* team sees.
    /// </summary>
    [Fact]
    public void Demo_LeavesTheTeamAccessLevelAlone()
    {
        var identity = ApplyDemo(DeveloperOnATeam(), TeamScope, OtherTeamScope);

        Assert.Equal(nameof(AccessLevel.Administrator), identity.FindFirst(TeamClaimTypes.AccessLevel)?.Value);
        Assert.Contains(identity.FindAll(ClaimTypes.Role), c => c.Value == $"Team{nameof(AccessLevel.Administrator)}");
        Assert.Null(AccessSimulationTargets.FromDemo([TeamScope]).AccessLevel);
    }

    /// <summary>
    /// <b>Consent-derived team access survives losing the application role.</b> This is the assumption the
    /// whole feature rests on for the user it was built for: a Developer who is not a member reaches the
    /// team through consent, and consent is resolved *from their roles* — so if the role were dropped
    /// before the team claims were built, demo mode would leave them with no team access at all rather
    /// than with a member's view.
    /// <para>
    /// It holds because of ordering, not because of a special case:
    /// <c>TeamServerClaimsTransformation.ApplySimulation</c> runs last, after the membership/consent claims
    /// are built, and each request re-issues from the identity provider's claims so it cannot compound.
    /// Asserted here because nothing else pins that ordering for the consent case, and a refactor that
    /// moved the filter earlier would break exactly this caller while every other test stayed green.
    /// </para>
    /// </summary>
    [Fact]
    public void Demo_KeepsTeamScopesThatArrivedViaConsent()
    {
        // A consented, non-member Developer: team scopes present, no membership, role present.
        var identity = new ClaimsIdentity(
        [
            new Claim(TeamClaimTypes.Scope, TeamScope),
            new Claim(TeamClaimTypes.SystemScope, SystemScope),
            new Claim(ClaimTypes.Role, Roles.Developer)
        ], "Test");

        ApplyDemo(identity, TeamScope);

        Assert.Contains(identity.FindAll(TeamClaimTypes.Scope), c => c.Value == TeamScope);
        Assert.DoesNotContain(identity.FindAll(ClaimTypes.Role), c => c.Value == Roles.Developer);
        Assert.Empty(identity.FindAll(TeamClaimTypes.SystemScope));
    }

    /// <summary>
    /// Recorded as its own <see cref="AccessSimulationKind.Demo"/> kind, and still carrying the demo label.
    /// </summary>
    /// <remarks>
    /// <b>Changed in Tharga/Team#223, and the reason matters more than the value.</b> A demo used to be
    /// <c>Scopes</c> plus the label <c>"Demo mode"</c> — a shape chosen to add no public API — so the only
    /// way to tell a demo from any other scope-set simulation was to match that string. The navigation bar
    /// now has to tell them apart, because a demo deliberately shows nothing there, and matching a label
    /// would break the moment a host translates it.
    /// <para>
    /// The label is still pinned: <c>AccessSimulationAuditEnricher</c> writes it to
    /// <c>simulation.target</c>, so it remains what distinguishes a demo in the audit log.
    /// </para>
    /// </remarks>
    [Fact]
    public void Demo_IsRecordedAsItsOwnKind()
    {
        var simulation = AccessSimulationTargets.FromDemo([TeamScope]);

        Assert.Equal(AccessSimulationKind.Demo, simulation.Kind);
        Assert.Equal(AccessSimulationTargets.DemoLabel, simulation.Label);
    }

    /// <summary>
    /// The de-escalation guarantee still holds. Demo mode names the caller's own scopes, so there is
    /// nothing to escalate — but the filter only ever removes, and asserting that here means a future
    /// change to <c>FromDemo</c> cannot turn it into a grant.
    /// </summary>
    [Fact]
    public void Demo_CannotAddAScopeTheCallerDoesNotHold()
    {
        var identity = DeveloperOnATeam();

        ApplyDemo(identity, TeamScope, OtherTeamScope, "billing:manage");

        Assert.DoesNotContain(identity.FindAll(TeamClaimTypes.Scope), c => c.Value == "billing:manage");
    }

    /// <summary>
    /// The self-check. Every assertion above would still pass if the filter did nothing at all to a
    /// caller with no system access — so this proves the system-side removal is what is being observed.
    /// </summary>
    [Fact]
    public void WithoutDemo_TheSystemAccessIsStillThere()
    {
        var identity = DeveloperOnATeam();

        Assert.NotEmpty(identity.FindAll(TeamClaimTypes.SystemScope));
        Assert.Contains(identity.FindAll(ClaimTypes.Role), c => c.Value == Roles.Developer);
    }
}
