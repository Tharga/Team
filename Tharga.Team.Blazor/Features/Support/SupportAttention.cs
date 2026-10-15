using Microsoft.JSInterop;

namespace Tharga.Team.Blazor.Features.Support;

/// <summary>
/// Whether somebody is looking at the page, so a conversation on screen marks an answer read only when it is
/// actually seen — not while the tab sits in the background or another window has focus.
/// </summary>
/// <remarks>
/// Where the browser cannot be asked — prerendering, or a test without a browser — the page counts as looked at,
/// which is how it behaved before this existed.
/// </remarks>
public sealed class SupportAttention(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tharga.Team.Blazor/support-attention.js";

    private readonly string _id = Guid.NewGuid().ToString("N");
    private IJSObjectReference _module;
    private DotNetObjectReference<SupportAttention> _self;
    private Func<Task> _then;

    /// <summary>Whether the page is the visible tab and its window has focus.</summary>
    public async Task<bool> IsAttendedAsync()
    {
        try
        {
            var module = await ModuleAsync();
            return module == null || await module.InvokeAsync<bool>("isAttended");
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or JSDisconnectedException)
        {
            return true;
        }
    }

    /// <summary>Runs <paramref name="then"/> now if the page is looked at, otherwise once it is; a later call replaces an earlier one.</summary>
    public async Task WhenAttendedAsync(Func<Task> then)
    {
        if (await IsAttendedAsync())
        {
            _then = null;
            await then();
            return;
        }

        _then = then;
        _self ??= DotNetObjectReference.Create(this);

        try
        {
            await _module.InvokeVoidAsync("whenAttended", _id, _self);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or JSDisconnectedException)
        {
            _then = null;
            await then();
        }
    }

    /// <summary>Forgets what was waiting to be seen, for a conversation that closed before anybody looked.</summary>
    public async Task CancelAsync()
    {
        _then = null;
        if (_module == null) return;

        try
        {
            await _module.InvokeVoidAsync("cancel", _id);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or JSDisconnectedException)
        {
        }
    }

    [JSInvokable]
    public async Task OnAttended()
    {
        var then = _then;
        _then = null;
        if (then != null) await then();
    }

    private async Task<IJSObjectReference> ModuleAsync()
        => _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public async ValueTask DisposeAsync()
    {
        await CancelAsync();
        _self?.Dispose();

        if (_module != null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (Exception exception) when (exception is JSException or JSDisconnectedException)
            {
            }
        }
    }
}
