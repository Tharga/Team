using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Server auth-state provider that periodically revalidates the caller's team claims for the life of a
/// Blazor Server circuit (#127). On each interval it re-evaluates team membership, access level, tenant
/// scopes, and consent access; if they changed it refreshes the principal <b>in place</b> — the caller is
/// not signed out, their team access is simply brought up to date (including downgrades and removal). This
/// keeps both the UI and service-layer authorization (which reads the auth state when no HttpContext
/// exists) from acting on frozen claims.
/// </summary>
internal sealed class TeamRevalidatingAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval;

    public TeamRevalidatingAuthenticationStateProvider(
        ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<ThargaBlazorOptions> options)
        : base(loggerFactory)
    {
        _scopeFactory = scopeFactory;
        _interval = options.Value.ClaimRevalidation.Interval;
    }

    protected override TimeSpan RevalidationInterval => _interval;

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        // Resolve the team services from a fresh scope rather than the circuit's own: this background
        // loop must not share the circuit's scoped services with an in-flight render (thread-safety), and
        // it breaks the DI cycle in which the auth-state provider would otherwise depend on ITeamService,
        // which depends (via BlazorTeamPrincipalAccessor) back on the auth-state provider.
        await using var scope = _scopeFactory.CreateAsyncScope();

        // A disabled user is the one case that does end the session, so it is checked before the claim
        // refresh: there is no point bringing team access up to date for someone who is being signed out.
        if (await IsDisabledAsync(scope.ServiceProvider, authenticationState.User)) return false;

        var revalidator = scope.ServiceProvider.GetRequiredService<TeamClaimRevalidator>();

        var refreshed = await revalidator.TryRefreshAsync(authenticationState.User);
        if (refreshed != null)
        {
            // Push the refreshed claims and keep the user signed in. Setting a new state cancels this
            // revalidation loop and starts a fresh one bound to the updated principal.
            SetAuthenticationState(Task.FromResult(new AuthenticationState(refreshed)));
        }

        // Team-claim changes never force a sign-out — the app session is still valid, only team access moved.
        return true;
    }

    /// <summary>
    /// Whether the caller has been disabled since they signed in — the eviction half of
    /// <c>IUserManagementService.SetUserDisabledAsync</c>. Disabling refuses future sign-ins by itself;
    /// without this, an already-signed-in user would keep working on claims issued before the decision.
    /// </summary>
    /// <remarks>
    /// <b>Fail-open, deliberately.</b> A store that cannot be reached must not sign out every signed-in
    /// user at once, which is what treating an exception as "disabled" would do — a database blip would
    /// become an outage. The loop runs again next interval, so a genuinely disabled user is still evicted,
    /// only one interval later.
    /// <para>
    /// The principal is passed explicitly: this runs on a background loop with no <c>HttpContext</c>, so
    /// the ambient-caller overload would resolve nobody.
    /// </para>
    /// <para>
    /// It records no activity: the loop runs for an idle tab too, which is not the user doing anything, so it must not
    /// move their <see cref="IUser.LastSeen"/>.
    /// </para>
    /// </remarks>
    internal static async Task<bool> IsDisabledAsync(IServiceProvider services, System.Security.Claims.ClaimsPrincipal principal)
    {
        try
        {
            var userService = services.GetService<IUserService>();
            if (userService == null) return false;

            var user = await userService.GetCurrentUserAsync(principal, recordActivity: false);
            return user?.DisabledAt != null;
        }
        catch
        {
            return false;
        }
    }
}
