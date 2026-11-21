using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Team;

/// <inheritdoc />
/// <remarks>
/// <b>Asks as the builder of the claims does</b>, through the same evaluator and its cache, so the browser and the server agree and a page that asks
/// again within the cache window costs nothing. It is not where entry is enforced.
/// </remarks>
internal sealed class TeamEntryService(
    TeamEntryEvaluator evaluator,
    AuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigationManager,
    IUserService userService = null) : ITeamEntryService
{
    public const string ReturnUrlPlaceholder = "{returnUrl}";

    public TeamEntryRefusal Refusal { get; private set; }

    public event EventHandler<TeamEntryRefusal> RefusalChanged;

    public async Task<TeamEntryDecision> CheckAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        if (!evaluator.Active || string.IsNullOrEmpty(teamKey)) return TeamEntryDecision.Allow;

        // Read at the moment of each check and never kept, so there is nothing to go stale: a later change is read by the next check.
#pragma warning disable BL0013
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true) return TeamEntryDecision.Allow;
#pragma warning restore BL0013

        var user = userService == null ? null : await userService.GetCurrentUserAsync(principal, recordActivity: false);
        var decision = await evaluator.EvaluateAsync(principal, user?.Key, teamKey, cancellationToken);

        // Whatever the circuit is let into clears an earlier refusal: the caller is working in a team, so there is nothing left to explain.
        Set(decision.Allowed ? null : new TeamEntryRefusal(teamKey, decision));

        return decision;
    }

    public void StepUp(TeamEntryRefusal refusal)
    {
        var url = refusal?.Decision.StepUpUrl;
        if (refusal?.Decision.Outcome != TeamEntryOutcome.StepUp || string.IsNullOrWhiteSpace(url)) return;

        // The page the caller is on, as a path on this site that the library made, so the host's step-up page can send them back.
        var back = "/" + navigationManager.ToBaseRelativePath(navigationManager.Uri);

        navigationManager.NavigateTo(url.Replace(ReturnUrlPlaceholder, Uri.EscapeDataString(back), StringComparison.Ordinal), forceLoad: true);
    }

    private void Set(TeamEntryRefusal refusal)
    {
        if (Equals(Refusal, refusal)) return;

        Refusal = refusal;
        RefusalChanged?.Invoke(this, refusal);
    }
}
