using System.Security.Claims;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Blazor.Framework;
using Tharga.Team;

namespace Tharga.Team.Blazor.Features.Team;

/// <summary>
/// Which team the caller currently has selected, and the set they may choose from.
/// </summary>
/// <remarks>
/// <b>Filtered, not gated — deliberately, and the reason has to be written down because it injects the
/// unchecked <see cref="ITeamService"/>.</b> This is a first-level read (a person opening the selector) that
/// names no team, so there is nothing for <c>[RequireScope]</c> to check against: the whole question is
/// <i>which</i> teams the caller may see. It answers by recomputing visibility per item —
/// <see cref="GetVisibleTeamsAsync"/> widens to every team only for a caller holding
/// <c>SystemTeamScopes.Read</c> and otherwise returns their own memberships — which is the "Filtered" row of
/// the service-classification table.
/// <para>
/// Selecting a team carries no access on its own. A non-member gets scopes only where the team has consented
/// to a role they hold, and that is decided by <c>TeamGrantResolver</c> when claims are built, not here.
/// </para>
/// <para>
/// Because it is not a component, the injection guards in <c>Tharga.Team.Blazor.Tests</c> and
/// <c>Tharga.Team.Mcp.Tests</c> do not cover it. <b>Do not follow this as a pattern</b> — a new surface
/// reaching for <see cref="ITeamService"/> almost certainly wants a gated facet instead.
/// </para>
/// </remarks>
internal class TeamStateService : ITeamStateService
{
    private readonly ITeamService _teamService;
    private readonly NavigationManager _navigationManager;
    private readonly ILocalStorageService _localStorageService;
    private readonly IJSRuntime _jSRuntime;
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly ThargaBlazorOptions _options;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private ITeam _selectedTeam;

    public TeamStateService(ITeamService teamService, NavigationManager navigationManager, ILocalStorageService localStorageService, IJSRuntime jSRuntime, AuthenticationStateProvider authenticationStateProvider, IOptions<ThargaBlazorOptions> options)
    {
        _teamService = teamService;
        _navigationManager = navigationManager;
        _localStorageService = localStorageService;
        _jSRuntime = jSRuntime;
        _authenticationStateProvider = authenticationStateProvider;
        _options = options.Value;

        _teamService.TeamsListChangedEvent += (s, e) => { TeamsListChangedEvent?.Invoke(s, e); };

        // Bridged so a component never needs the internal ITeamService to hear it. The service raises this
        // when it picks a team itself — auto-selection at sign-in — and subscribers see an ordinary
        // selection change. Routed through SetSelectedTeamAsync rather than assigning the field, so the
        // last-seen stamp and persistence still happen; that is what the selector used to do by hand, and
        // the same-key guard in there stops a redundant round trip.
        _teamService.SelectTeamEvent += async (_, e) =>
        {
            if (e.Team != null) await SetSelectedTeamAsync(e.Team);
        };
    }

    public event EventHandler<TeamsListChangedEventArgs> TeamsListChangedEvent;
    public event EventHandler<SelectedTeamChangedEventArgs> SelectedTeamChangedEvent;

    public async Task<ITeam> GetSelectedTeamAsync()
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        if (!(authState.User.Identity?.IsAuthenticated ?? false)) return null;

        var (team, notify) = await ResolveAsync(authState.User);

        // Raised outside the lock, and only for a real change. Reading the selection is the natural thing
        // to do from a handler, so a handler calling back in has to be survivable: raising while holding
        // the semaphore queued those calls behind the raiser's own lock, and raising unconditionally made
        // every call produce another one. Together that was unbounded recursion for a caller with no team,
        // where the selection resolves to null and so never settles.
        if (notify) SelectedTeamChangedEvent?.Invoke(this, new SelectedTeamChangedEventArgs(team));

        return team;
    }

    public bool TryGetSelectedTeam(out ITeam team)
    {
        // Deliberately unsynchronised: a reference read is atomic, and taking the semaphore would make this
        // neither cheap nor synchronous — which is the whole point of it.
        team = _selectedTeam;
        return team != null;
    }

    /// <summary>
    /// Resolves the selection under the lock, reporting what it settled on and whether that is worth an
    /// event. The decision is returned rather than acted on, because the raise belongs outside the lock.
    /// </summary>
    private async Task<(ITeam Team, bool Notify)> ResolveAsync(ClaimsPrincipal principal)
    {
        await _semaphore.WaitAsync();

        try
        {
            var previous = _selectedTeam;

            // Two sets, two purposes. `visibleTeams` (widened for a teams:read holder) decides which
            // *chosen* team is still legitimate; `teams` (own memberships) is the only source for the
            // fallback, so nobody is ever defaulted into a tenant they didn't pick.
            var teams = await _teamService.GetTeamsAsync().ToArrayAsync();
            var currentTeamKey = principal.Claims.FirstOrDefault(x => x.Type == Constants.TeamKeyCookie)?.Value;
            var visibleTeams = await WithSimulatedTeamAsync(principal, currentTeamKey, await GetVisibleTeamsAsync(principal, teams));

            if (!NeedsResolution(visibleTeams)) return (_selectedTeam, false);

            var rememberedTeamKey = await _localStorageService.GetItemAsStringAsync(Constants.SelectedTeamLocalStorageKey);
            var team = TeamSelectionResolver.Resolve(currentTeamKey, rememberedTeamKey, visibleTeams, teams);

            if (team == null && !teams.Any() && _options.AutoCreateFirstTeam)
            {
                team = await _teamService.CreateTeamAsync();
            }

            _selectedTeam = team;

            // Refresh only when the cookie doesn't already name this team — otherwise the claims for it
            // have been applied on this request and a reload would be pointless. Nothing is notified on
            // this path: the page is being replaced, so there is no subscriber left to tell.
            if (team != null && team.Key != currentTeamKey)
            {
                await SetTeamCookieAsync(team.Key);
                _navigationManager.Refresh(true);
                return (team, false);
            }

            return (team, HasChanged(previous, team));
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Whether the held selection still stands. It must exist, still be visible, and still carry the same
    /// name — a rename leaves the held instance stale even though its key still matches.
    /// </summary>
    private bool NeedsResolution(ITeam[] visibleTeams)
    {
        if (_selectedTeam == null) return true;

        var visible = visibleTeams.FirstOrDefault(x => x.Key == _selectedTeam.Key);
        return visible == null || visible.Name != _selectedTeam.Name;
    }

    /// <summary>
    /// Whether a resolution is worth an event. Name as well as key, because a rename is a change that
    /// subscribers render even though it is the same team.
    /// </summary>
    private static bool HasChanged(ITeam previous, ITeam current)
    {
        return previous?.Key != current?.Key || previous?.Name != current?.Name;
    }

    /// <summary>
    /// Teams the caller may legitimately have selected: their own, widened to every team when they hold
    /// <see cref="SystemTeamScopes.Read"/>. Falls back to own teams if the widened read is refused, so a
    /// claims/enforcement mismatch degrades to today's behaviour instead of breaking the page.
    /// </summary>
    private async Task<ITeam[]> GetVisibleTeamsAsync(ClaimsPrincipal principal, ITeam[] ownTeams)
    {
        if (!TeamVisibility.CanSeeAllTeams(principal)) return ownTeams;

        try
        {
            return await _teamService.GetAllTeamsAsync().ToArrayAsync();
        }
        catch (UnauthorizedAccessException)
        {
            return ownTeams;
        }
    }

    /// <summary>
    /// Adds the selected team to <paramref name="visibleTeams"/> while a simulation is in force for it.
    /// </summary>
    /// <remarks>
    /// Visibility is otherwise judged from the principal, which a simulation has narrowed — so a team reached by
    /// consent or <c>teams:read</c> would drop out and the caller would be moved to a team they never chose, with
    /// the simulation applied there (Tharga/Team#276). <see cref="SimulatedTeamSelection"/> decides from the
    /// caller's real grant; this only supplies the team.
    /// </remarks>
    private async Task<ITeam[]> WithSimulatedTeamAsync(ClaimsPrincipal principal, string currentTeamKey, ITeam[] visibleTeams)
    {
        if (!SimulatedTeamSelection.KeepsSelection(principal, currentTeamKey)) return visibleTeams;
        if (visibleTeams.Any(x => x.Key == currentTeamKey)) return visibleTeams;

        var team = await _teamService.GetTeamByKeyAsync(currentTeamKey);
        return team == null ? visibleTeams : [.. visibleTeams, team];
    }

    private async Task SetTeamCookieAsync(string teamKey)
    {
        await _jSRuntime.InvokeVoidAsync("eval", $"document.cookie = '{Constants.SelectedTeamKeyCookie}={teamKey}; path=/'");
    }

    public Task SetSelectedTeamAsync(ITeam selectedTeam) => SetSelectedTeamAsync(selectedTeam, true);

    public async Task SetSelectedTeamAsync(ITeam selectedTeam, bool reload)
    {
        await _teamService.SetMemberLastSeenAsync(selectedTeam.Key);

        if (_selectedTeam?.Key == selectedTeam.Key) return;

        _selectedTeam = selectedTeam;

        // Remembered across visits whether or not the caller is a member — an explicit choice is theirs
        // to keep. Selection carries no access on its own: a non-member gets scopes only where the team
        // has consented to a role they hold.
        await _localStorageService.SetItemAsStringAsync(Constants.SelectedTeamLocalStorageKey, selectedTeam.Key);

        await SetTeamCookieAsync(selectedTeam.Key);

        if (reload) _navigationManager.Refresh(true);
    }
}
