using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;

namespace Tharga.Team.Blazor.Features.Simulation;

/// <summary>
/// Starting, describing and ending an access simulation from the UI.
/// </summary>
/// <remarks>
/// <b>The real grant is re-resolved here rather than read from the principal.</b> Once a simulation is
/// active the principal carries the reduced set, so a picker built from it would offer only what is left
/// — and a caller who simulated away <see cref="SimulationScopes.Simulate"/> could neither change nor
/// inspect their own simulation. Re-resolving costs a lookup on an administration screen and avoids
/// parking the removed scopes on the principal as shadow claims, which is the shape a later reader
/// mistakes for a grant.
/// <para>
/// <b>It injects the unchecked <see cref="Tharga.Team.ITeamService"/>, and that needs justifying.</b>
/// Re-resolving the real grant is exactly the case the gated facets cannot serve: a caller who has simulated
/// away their scopes would be refused by <c>[RequireScope]</c> on the very read that tells them what they
/// simulated. Every entry point that reaches this class is itself gated by
/// <see cref="SimulationScopes.Simulate"/>, so the check happens above rather than being absent.
/// </para>
/// <para>
/// Because it is not a component, the injection guards in the test projects do not cover it. <b>Do not follow
/// this as a pattern</b> — a new surface reaching for <c>ITeamService</c> almost certainly wants a gated facet.
/// </para>
/// </remarks>
public sealed class AccessSimulationState
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly ITeamService _teamService;
    private readonly IUserService _userService;
    private readonly IScopeRegistry _scopeRegistry;
    private readonly ITenantRoleService _tenantRoleService;
    private readonly NavigationManager _navigationManager;
    private readonly IJSRuntime _jsRuntime;
    private readonly AccessSimulationOptions _options;
    private readonly AccessLevel _consentAccessLevel;

    public AccessSimulationState(
        AuthenticationStateProvider authenticationStateProvider,
        ITeamService teamService,
        IUserService userService,
        NavigationManager navigationManager,
        IJSRuntime jsRuntime,
        IOptions<ThargaBlazorOptions> options,
        IScopeRegistry scopeRegistry = null,
        ITenantRoleService tenantRoleService = null)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _teamService = teamService;
        _userService = userService;
        _navigationManager = navigationManager;
        _jsRuntime = jsRuntime;
        _scopeRegistry = scopeRegistry;
        _tenantRoleService = tenantRoleService;
        _options = options.Value.Simulation;
        _consentAccessLevel = options.Value.Consent.AccessLevel;
    }

    /// <summary>Whether the host turned the feature on.</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>The simulation currently in force, or null.</summary>
    public async Task<AccessSimulation> GetActiveAsync()
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        return AccessSimulationCookie.Read(state.User.FindFirst(AccessSimulationCookie.ClaimType)?.Value);
    }

    /// <summary>
    /// Whether the caller may start one.
    /// </summary>
    /// <remarks>
    /// Checked against the caller's <b>real</b> grant, so simulating the scope away does not lock them
    /// out of the picker. <see cref="StopAsync"/> deliberately has no equivalent check.
    /// <para>
    /// <b>Answered from the principal where the principal can answer it.</b> The scope claims are built by
    /// <c>TeamMembershipClaimsBuilder</c> from this same resolver, with the same arguments, so a claim is
    /// the resolver's own answer already carried on the caller — reading it is a cache hit rather than a
    /// second place restating the rule. It matters because this runs on every render of the card and the
    /// bar, and a host with no team cache pays a database round trip for each one (Tharga/Team#219).
    /// </para>
    /// <para>
    /// The grant is still resolved whenever the claims cannot answer — see
    /// <see cref="ClaimsCanAnswer"/>. What the fast path trades away is freshness: a grant changed
    /// mid-session reaches the principal at the next claim revalidation, exactly as it does for every other
    /// scope-gated surface in the toolkit.
    /// </para>
    /// </remarks>
    public async Task<bool> CanSimulateAsync()
    {
        if (!Enabled) return false;

        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();

        if (ClaimsCanAnswer(state.User))
            return state.User.HasClaim(TeamClaimTypes.Scope, SimulationScopes.Simulate);

        var grant = await ResolveRealGrantAsync();
        return grant != null && grant.Scopes.Contains(SimulationScopes.Simulate, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether <paramref name="principal"/>'s scope claims describe what the caller really holds.
    /// </summary>
    /// <remarks>
    /// Two conditions, both load-bearing.
    /// <para>
    /// <b>No simulation may be in force.</b> <c>AccessSimulationFilter</c> removes scope claims, so a
    /// filtered principal cannot say what was removed — and a caller who simulated
    /// <see cref="SimulationScopes.Simulate"/> away would be refused the picker that undoes it.
    /// </para>
    /// <para>
    /// <b>Claims must have been issued for the team that is selected.</b> <see cref="TeamClaimTypes.TeamKey"/>
    /// is issued only when the builder resolved a grant, so its presence is what separates "holds no scopes"
    /// from "claims were never issued" — an absent scope claim means nothing without it. Matching it against
    /// the selected team covers the moment between choosing a team and the reload that re-issues claims for
    /// it.
    /// </para>
    /// </remarks>
    private static bool ClaimsCanAnswer(ClaimsPrincipal principal)
    {
        if (AccessSimulationCookie.IsActive(principal)) return false;

        var selectedTeamKey = principal?.FindFirst(Constants.TeamKeyCookie)?.Value;

        return !string.IsNullOrEmpty(selectedTeamKey)
               && principal.HasClaim(TeamClaimTypes.TeamKey, selectedTeamKey);
    }

    /// <summary>The members of the selected team that can be simulated.</summary>
    /// <remarks>
    /// Members only. Someone reaching the team through consent rather than membership has a grant that
    /// depends on their app roles, which the toolkit does not store, so their access cannot be resolved.
    /// </remarks>
    public async Task<IReadOnlyList<AccessSimulationCandidate>> GetMemberTargetsAsync()
    {
        var teamKey = await SelectedTeamKeyAsync();
        if (teamKey == null) return [];

        var self = await _userService.GetCurrentUserAsync();
        var candidates = new List<AccessSimulationCandidate>();

        await foreach (var member in _teamService.GetMembersAsync(teamKey))
        {
            if (member?.Key == null) continue;
            if (member.State != MembershipState.Member) continue;

            var scopes = await EffectiveScopesAsync(teamKey, member);
            candidates.Add(new AccessSimulationCandidate(
                member.Key,
                await DisplayNameAsync(member),
                member.AccessLevel,
                scopes));
        }

        // Simulating yourself is a no-op that looks like a feature, so it is not offered.
        var selfKey = self?.Key;
        return selfKey == null ? candidates : [.. candidates.Where(c => c.Key != selfKey)];
    }

    /// <summary>The tenant roles that can be simulated.</summary>
    public async Task<IReadOnlyList<AccessSimulationCandidate>> GetRoleTargetsAsync()
    {
        var teamKey = await SelectedTeamKeyAsync();
        if (teamKey == null || _tenantRoleService == null) return [];

        var roles = await _tenantRoleService.GetRolesAsync(teamKey);

        return
        [
            .. roles.Select(role => new AccessSimulationCandidate(
                role.Name,
                role.Name,
                AccessLevel: null,
                role.Scopes ?? []))
        ];
    }

    /// <summary>
    /// The scopes the caller actually holds on the selected team, for picking a set by hand.
    /// </summary>
    /// <remarks>
    /// Their own, not the whole registry. Offering a scope they do not hold would invite choosing one
    /// the filter cannot keep, and the result would read as the picker ignoring the choice rather than
    /// as the guarantee working.
    /// </remarks>
    public async Task<IReadOnlyList<string>> GetOwnScopesAsync()
    {
        var grant = await ResolveRealGrantAsync();
        return [.. (grant?.Scopes ?? []).OrderBy(s => s, StringComparer.Ordinal)];
    }

    /// <summary>The access levels that can be simulated.</summary>
    public IReadOnlyList<AccessSimulationCandidate> GetAccessLevelTargets()
        =>
        [
            .. Enum.GetValues<AccessLevel>().Select(level => new AccessSimulationCandidate(
                level.ToString(),
                level.ToString(),
                level,
                _scopeRegistry?.GetScopesForAccessLevel(level) ?? []))
        ];

    /// <summary>
    /// What <paramref name="simulation"/> will not be able to show, given what the caller really holds.
    /// </summary>
    public async Task<AccessSimulationGap> DescribeGapAsync(AccessSimulation simulation)
    {
        var grant = await ResolveRealGrantAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            (grant?.Scopes ?? []).Select(s => new Claim(TeamClaimTypes.Scope, s)), "RealGrant"));

        return AccessSimulationDifference.Compare(principal, simulation);
    }

    /// <summary>Starts a simulation. Replaces any that is already active.</summary>
    /// <remarks>
    /// Replaces rather than composes. Stacking would be safe — removal composes — but "return to my
    /// normal access" would then have to unwind steps, and an indicator naming only the innermost would
    /// understate what is in force.
    /// </remarks>
    public Task StartAsync(AccessSimulation simulation) => WriteAndReloadAsync(AccessSimulationCookie.Write(simulation));

    /// <summary>
    /// Starts demo mode: keeps the caller's team access exactly as it is and drops their system-wide
    /// access, so a demonstration shows the product rather than the administrative surface.
    /// </summary>
    /// <remarks>
    /// Reads the caller's own scopes rather than taking a target, because the target *is* their own
    /// access — see <see cref="AccessSimulationTargets.FromDemo"/>. Ending it is the ordinary
    /// <see cref="StopAsync"/>, which restores the system scopes by re-issuing claims through the normal
    /// request path.
    /// </remarks>
    public async Task StartDemoAsync()
    {
        var scopes = await GetOwnScopesAsync();
        await StartAsync(AccessSimulationTargets.FromDemo(scopes));
    }

    /// <summary>
    /// Ends the simulation and returns the caller to their real access.
    /// </summary>
    /// <remarks>
    /// <b>Never gated.</b> A simulation can remove <see cref="SimulationScopes.Simulate"/>, and this only
    /// restores what the caller genuinely holds, so there is nothing here to authorize.
    /// </remarks>
    public Task StopAsync() => WriteAndReloadAsync(string.Empty);

    /// <remarks>
    /// A circuit cannot set a cookie, so the value is written from script and the page is reloaded. The
    /// reload is what makes the new claims take effect: the request runs
    /// <see cref="TeamServerClaimsTransformation"/> again, which is where the simulation is read and
    /// applied. It also means this does not depend on the stale-claims problem in #127.
    /// </remarks>
    private async Task WriteAndReloadAsync(string value)
    {
        var cookie = string.IsNullOrEmpty(value)
            ? $"{AccessSimulationCookie.Name}=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT"
            : $"{AccessSimulationCookie.Name}={value}; path=/";

        await _jsRuntime.InvokeVoidAsync("eval", $"document.cookie = '{cookie}'");
        _navigationManager.NavigateTo(_navigationManager.Uri, forceLoad: true);
    }

    private async Task<string> SelectedTeamKeyAsync()
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        return state.User.FindFirst(Constants.TeamKeyCookie)?.Value;
    }

    /// <summary>
    /// The caller's real access in the selected team.
    /// </summary>
    /// <remarks>
    /// <b>Through <see cref="TeamGrantResolver"/>, not a membership lookup.</b> A caller can hold access
    /// without being a member — a global role the team consented to grants one — and an earlier version
    /// of this method returned null for exactly that caller, so the whole feature was invisible to a
    /// Developer reaching a team by consent. That is the same defect the toolkit already carries a scar
    /// from: a second place restating a rule the resolver owns. This asks the resolver instead, which is
    /// also what issues the caller's claims, so the two cannot disagree about what someone holds.
    /// </remarks>
    private async Task<TeamGrant> ResolveRealGrantAsync()
    {
        var teamKey = await SelectedTeamKeyAsync();
        if (teamKey == null) return null;

        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var user = await _userService.GetCurrentUserAsync(state.User);

        return await new TeamGrantResolver(_teamService, _scopeRegistry, _tenantRoleService)
            .ResolveAsync(state.User, user?.Key, teamKey, _consentAccessLevel);
    }

    /// <summary>
    /// What to call a member: their per-team display name, else their own name, else whatever the
    /// toolkit can make of their identity.
    /// </summary>
    /// <remarks>
    /// <see cref="ITeamMember.Name"/> is a per-team <i>override</i> and is usually null, so using it
    /// alone showed a raw key — which is exactly what the picker must not do, since choosing who to view
    /// as is the one place a person has to be recognisable. Resolved the way
    /// <c>TeamComponent</c> already does it: <see cref="ITeamMember.Key"/> is the user key, so the user
    /// record carries the name and <c>TeamServiceBase.ResolveDisplayName</c> supplies the email-or-identity
    /// fallback.
    /// </remarks>
    private async Task<string> DisplayNameAsync(ITeamMember member)
    {
        if (!string.IsNullOrEmpty(member.Name)) return member.Name;

        var user = await _userService.GetUserByKeyAsync(member.Key);
        if (user == null) return member.Key;

        return !string.IsNullOrEmpty(user.Name)
            ? user.Name
            : TeamServiceBase.ResolveDisplayName(user) ?? member.Key;
    }

    /// <remarks>
    /// Mirrors <see cref="TeamGrantResolver"/>'s member branch rather than calling it, because
    /// <see cref="ITeamMember"/> carries no user key and the resolver is keyed by one. The two must stay
    /// in step; if a third copy of this ever appears, give <c>ITeamMember</c> the key instead.
    /// </remarks>
    private async Task<IReadOnlyList<string>> EffectiveScopesAsync(string teamKey, ITeamMember member)
        => _tenantRoleService != null
            ? await _tenantRoleService.GetEffectiveScopesAsync(teamKey, member.AccessLevel, member.TenantRoles, member.ScopeOverrides)
            : _scopeRegistry?.GetEffectiveScopes(member.AccessLevel, member.TenantRoles, member.ScopeOverrides) ?? [];
}

/// <summary>Something that can be simulated, and the access it carries.</summary>
/// <param name="Key">Stable identifier — a member key, a role name, or a level name.</param>
/// <param name="Name">What to show.</param>
/// <param name="AccessLevel">The level it implies, where it implies one.</param>
/// <param name="Scopes">The scopes it grants.</param>
public sealed record AccessSimulationCandidate(
    string Key,
    string Name,
    AccessLevel? AccessLevel,
    IReadOnlyList<string> Scopes);
