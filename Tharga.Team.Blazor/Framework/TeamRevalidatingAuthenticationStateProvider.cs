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
        IOptions<ThargaBlazorOptions> options,
        ITeamEntryNotifier entryNotifier = null)
        : base(loggerFactory)
    {
        _scopeFactory = scopeFactory;
        _interval = options.Value.ClaimRevalidation.Interval;
        _entryNotifier = entryNotifier;

        // The host tells the library a guard's answer changed: this circuit re-evaluates at once, not at the next interval.
        if (_entryNotifier != null) _entryNotifier.Invalidated += OnEntryInvalidated;
    }

    private readonly ITeamEntryNotifier _entryNotifier;

    /// <summary>
    /// Brings this circuit's team claims up to date now, when the answer that changed is for its own user. Never throws: it runs on whatever thread told the
    /// library, and a failure leaves the next interval to catch up.
    /// </summary>
    private async void OnEntryInvalidated(object sender, TeamEntryInvalidatedEventArgs e)
    {
        try
        {
            // This is the provider: the state it reads is its own, and it is the one that raises the change.
#pragma warning disable BL0013
            var state = await GetAuthenticationStateAsync();
            if (state.User.Identity?.IsAuthenticated != true) return;
#pragma warning restore BL0013

            await using var scope = _scopeFactory.CreateAsyncScope();
            var users = scope.ServiceProvider.GetService<IUserService>();
            var user = users == null ? null : await users.GetCurrentUserAsync(state.User, recordActivity: false);
            if (user?.Key == null || user.Key != e.UserKey) return;

            var refreshed = await scope.ServiceProvider.GetRequiredService<TeamClaimRevalidator>().TryRefreshAsync(state.User);
            if (refreshed != null) SetAuthenticationState(Task.FromResult(new AuthenticationState(refreshed)));
        }
        catch
        {
            // The next interval retries.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _entryNotifier != null) _entryNotifier.Invalidated -= OnEntryInvalidated;

        base.Dispose(disposing);
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

        // So does a server-side session that has ended, here or on another instance: a circuit makes no request for the cookie check to refuse.
        if (await IsSessionEndedAsync(scope.ServiceProvider, authenticationState.User)) return false;

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

    /// <summary>
    /// Whether the caller's server-side session has ended, so the circuit is ended with it (Tharga/Team#471): ended elsewhere, or over the host's limits for
    /// the team the circuit is in, in which case it is ended now and audited once.
    /// </summary>
    /// <remarks>
    /// <b>Fail-open, like <see cref="IsDisabledAsync"/></b>: a store that cannot be reached must not end every open tab at once, whatever
    /// <see cref="SessionOptions.OnError"/> says for requests. It never adopts a cookie with no session and never records activity, because this loop runs
    /// for an idle tab too; the page reports activity itself. Nothing is read when no policy is registered.
    /// <para>
    /// It runs on the revalidation interval (<see cref="ClaimRevalidationOptions.Interval"/>), so a live circuit learns of an ended session at most one
    /// interval late; the next request is refused at once regardless.
    /// </para>
    /// </remarks>
    internal static async Task<bool> IsSessionEndedAsync(IServiceProvider services, System.Security.Claims.ClaimsPrincipal principal)
    {
        try
        {
            var evaluator = services.GetService<SessionEvaluator>();
            if (evaluator?.Active != true) return false;

            var check = await evaluator.EvaluateCircuitAsync(principal, principal?.FindFirst(TeamClaimTypes.SelectedTeamKey)?.Value);
            return check.Outcome == SessionCheckOutcome.Ended;
        }
        catch
        {
            return false;
        }
    }
}
