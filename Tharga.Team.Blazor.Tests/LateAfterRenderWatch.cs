using Tharga.Team.Blazor.Features.Authentication;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A <see cref="SessionWatch"/> whose first after-render waits for the test, as on a server circuit, where it arrives only once the browser has acknowledged
/// the render.
/// </summary>
public sealed class LateAfterRenderWatch : SessionWatch
{
    private bool _acknowledged;

    public Task AcknowledgeFirstRenderAsync()
    {
        _acknowledged = true;
        return base.OnAfterRenderAsync(true);
    }

    protected override Task OnAfterRenderAsync(bool firstRender) => _acknowledged ? base.OnAfterRenderAsync(false) : Task.CompletedTask;
}
