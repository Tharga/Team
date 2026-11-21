using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Gives a component the signed-in caller, and tells it when the caller's claims change (Tharga/Team#505, BL0013).
/// </summary>
/// <remarks>
/// <b>Why a component must follow, and not only read.</b> The toolkit changes a circuit's claims while it runs: the team revalidation brings membership,
/// access level and scopes up to date, and a step-up or a host's guard answer does the same at once. A component that decides what to offer from claims read
/// at start keeps offering what the caller has since lost, or hides what they have since been given, until the page is reloaded. The framework raises
/// <see cref="AuthenticationStateProvider.AuthenticationStateChanged"/> when that happens; this is the one place that listens, so the fifteen or so
/// components that gate on the caller do it the same way and none has to remember to unsubscribe in its own way.
/// <para>
/// <b>Read from the provider rather than the cascading state</b> on purpose: a host is not required to cascade it, several components of the toolkit wrap
/// themselves in <c>CascadingAuthenticationState</c> because of that, and the provider is there wherever the circuit is.
/// </para>
/// <para>
/// <b>The callback is the component's to run on its own renderer</b>: it is handed the new principal and is expected to dispatch with
/// <c>InvokeAsync</c>. An exception from it is swallowed, since it runs from an event nobody awaits and one that escaped would end the circuit; the next
/// change, or the next revalidation, tries again.
/// </para>
/// </remarks>
internal sealed class CallerFollower : IDisposable
{
    private readonly AuthenticationStateProvider _provider;
    private readonly Func<ClaimsPrincipal, Task> _changed;
    private bool _disposed;

    /// <param name="provider">The circuit's auth-state provider.</param>
    /// <param name="changed">Called with the new principal when the claims change. Not called for the first read, which <see cref="StartAsync"/> returns.</param>
    public CallerFollower(AuthenticationStateProvider provider, Func<ClaimsPrincipal, Task> changed)
    {
        _provider = provider;
        _changed = changed;
    }

    /// <summary>Starts listening, then reads the caller. Listening first means a change between the two is delivered rather than lost.</summary>
    public async Task<ClaimsPrincipal> StartAsync()
    {
        _provider.AuthenticationStateChanged += OnChanged;
        return (await _provider.GetAuthenticationStateAsync()).User;
    }

    private async void OnChanged(Task<AuthenticationState> state)
    {
        try
        {
            var user = (await state).User;
            if (!_disposed) await _changed(user);
        }
        catch
        {
            // Runs from an event: see the remarks.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _provider.AuthenticationStateChanged -= OnChanged;
    }
}
