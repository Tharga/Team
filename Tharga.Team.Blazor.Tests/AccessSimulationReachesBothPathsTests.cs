using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// That the simulation is <i>reached</i> by both claim-issuance paths — not that the filter is correct.
/// </summary>
/// <remarks>
/// Claims are issued twice by design: <see cref="TeamServerClaimsTransformation"/> on the HTTP path and
/// <see cref="TeamClaimRevalidator"/> inside the circuit. They share
/// <see cref="TeamMembershipClaimsBuilder"/> so the team half cannot drift, but the filter cannot live
/// there — system scopes and app roles are added outside it, and those are exactly what a simulation
/// drops.
/// <para>
/// So it is applied twice, and this asserts both. <b>The revalidator is the dangerous one:</b> it rebuilds
/// the identity from fresh membership claims, which restores everything the simulation removed. Missing
/// there, a simulation would expire on its own — silently, up to <c>ClaimRevalidation.Interval</c> after
/// the caller started it, and looking exactly like the feature working until it suddenly was not.
/// </para>
/// <para>
/// This is the shape Tharga/Team#175 had: the rule was right and one of five callers skipped it.
/// </para>
/// </remarks>
public class AccessSimulationReachesBothPathsTests
{
    private const string TeamKey = "team-1";
    private const string UserKey = "user-1";

    private static AccessSimulation KeepOnly(params string[] scopes)
        => new() { Kind = AccessSimulationKind.Scopes, Label = "test", Scopes = scopes, TeamKey = TeamKey };

    private static string[] ScopesOf(ClaimsPrincipal p)
        => p.FindAll(TeamClaimTypes.Scope).Select(c => c.Value).ToArray();

    // --- shared fakes ---

    private sealed record FakeMember(string Key, AccessLevel AccessLevel) : ITeamMember
    {
        public string Name => "Alice";
        public Invitation Invitation => null;
        public DateTime? LastSeen => null;
        public MembershipState? State => MembershipState.Member;
        public string[] TenantRoles => [];
        public string[] ScopeOverrides => [];
    }

    private static TeamMembershipClaimsBuilder Builder()
    {
        var teamService = new Mock<ITeamService>();
        teamService.Setup(x => x.GetTeamMemberAsync(TeamKey, UserKey))
            .ReturnsAsync(new FakeMember("member-1", AccessLevel.Owner));

        var userService = new Mock<IUserService>();
        userService.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(Mock.Of<IUser>(u => u.Key == UserKey));

        var scopeRegistry = new Mock<IScopeRegistry>();
        scopeRegistry.Setup(x => x.GetEffectiveScopes(It.IsAny<AccessLevel>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns(["orders:read", "orders:write", "billing:manage"]);

        return new TeamMembershipClaimsBuilder(
            teamService.Object,
            userService.Object,
            Options.Create(new ThargaBlazorOptions()),
            scopeRegistry.Object);
    }

    // --- path 1: the HTTP transformation ---

    [Fact]
    public async Task TheHttpTransformation_AppliesTheSimulation()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie =
            $"{Constants.SelectedTeamKeyCookie}={TeamKey}; " +
            $"{AccessSimulationCookie.Name}={AccessSimulationCookie.Write(KeepOnly("orders:read"))}";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(x => x.HttpContext).Returns(httpContext);

        var transformation = new TeamServerClaimsTransformation(accessor.Object, Builder());

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "alice")], "Test"));

        var result = await transformation.TransformAsync(principal);

        Assert.Equal(["orders:read"], ScopesOf(result));
    }

    /// <summary>The self-check: without a simulation the same setup issues the full set.</summary>
    [Fact]
    public async Task TheHttpTransformation_IssuesEverythingWhenNoSimulationIsActive()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = $"{Constants.SelectedTeamKeyCookie}={TeamKey}";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(x => x.HttpContext).Returns(httpContext);

        var transformation = new TeamServerClaimsTransformation(accessor.Object, Builder());

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "alice")], "Test"));

        var result = await transformation.TransformAsync(principal);

        Assert.Equal(["orders:read", "orders:write", "billing:manage"], ScopesOf(result));
    }

    // --- path 2: the in-circuit revalidator ---

    /// <summary>
    /// The one that would fail silently. The revalidator rebuilds the identity from fresh claims, so
    /// without re-applying the filter the caller's full access returns at the next interval.
    /// </summary>
    [Fact]
    public async Task TheRevalidator_DoesNotRestoreWhatTheSimulationRemoved()
    {
        var revalidator = new TeamClaimRevalidator(Builder());

        // A circuit mid-simulation: reduced scopes, and the marker claim carrying it.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "alice"),
            new Claim(Constants.TeamKeyCookie, TeamKey),
            new Claim(TeamClaimTypes.TeamKey, TeamKey),
            new Claim(TeamClaimTypes.AccessLevel, nameof(AccessLevel.Owner)),
            new Claim(TeamClaimTypes.Scope, "orders:read"),
            new Claim(AccessSimulationCookie.ClaimType, AccessSimulationCookie.Write(KeepOnly("orders:read")))
        ], "Test"));

        var refreshed = await revalidator.TryRefreshAsync(principal);

        // Either it reports no change, or it reports one that is still narrowed. Both are acceptable;
        // returning the full set is not.
        Assert.Equal(["orders:read"], ScopesOf(refreshed ?? principal));
    }

    /// <summary>
    /// The self-check for the test above: the same revalidator, same fakes, no simulation — and the full
    /// set comes back. Without this, "still one scope" could mean the builder was never reached.
    /// </summary>
    [Fact]
    public async Task TheRevalidator_RestoresTheFullSetWhenNoSimulationIsActive()
    {
        var revalidator = new TeamClaimRevalidator(Builder());

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "alice"),
            new Claim(Constants.TeamKeyCookie, TeamKey),
            new Claim(TeamClaimTypes.TeamKey, TeamKey),
            new Claim(TeamClaimTypes.AccessLevel, nameof(AccessLevel.Owner)),
            new Claim(TeamClaimTypes.Scope, "orders:read")
        ], "Test"));

        var refreshed = await revalidator.TryRefreshAsync(principal);

        Assert.NotNull(refreshed);
        Assert.Equal(["orders:read", "orders:write", "billing:manage"], ScopesOf(refreshed));
    }

    /// <summary>
    /// With a simulation active and nothing else changed, the revalidator reports no change — otherwise
    /// every interval would look like a claims change for as long as simulation was on, and the circuit
    /// would re-render on a timer.
    /// </summary>
    [Fact]
    public async Task AnUnchangedSimulation_IsNotReportedAsAChange()
    {
        var revalidator = new TeamClaimRevalidator(Builder());

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "alice"),
            new Claim(Constants.TeamKeyCookie, TeamKey),
            new Claim(TeamClaimTypes.TeamKey, TeamKey),
            new Claim(TeamClaimTypes.AccessLevel, nameof(AccessLevel.Owner)),
            new Claim(ClaimTypes.Role, Roles.TeamMember),
            new Claim(ClaimTypes.Role, "TeamOwner"),
            new Claim(TeamClaimTypes.MemberKey, "member-1"),
            new Claim(TeamClaimTypes.Scope, "orders:read"),
            new Claim(AccessSimulationCookie.ClaimType, AccessSimulationCookie.Write(KeepOnly("orders:read")))
        ], "Test"));

        Assert.Null(await revalidator.TryRefreshAsync(principal));
    }
}
