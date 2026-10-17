using Microsoft.JSInterop;

namespace Tharga.Team.Blazor.Features.Support;

/// <summary>
/// Hands an opened attachment to the browser: a viewable one in a new tab, any other saved as a download.
/// </summary>
/// <remarks>
/// <b>The choice is made from the declared type, and only the safe list opens</b> — see
/// <see cref="SupportAttachmentContentTypes"/>. A blob opened from the application runs in its origin, so an
/// HTML page or an SVG opened that way could act with the reader's session. The script checks the same list
/// again before it opens anything.
/// </remarks>
internal sealed class SupportAttachmentFiles(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tharga.Team.Blazor/support-attachments.js";

    private IJSObjectReference _module;

    /// <summary>Opens the file in a new tab when its type may open in the browser, otherwise saves it.</summary>
    public async Task OpenOrSaveAsync(SupportAttachmentContent content)
    {
        var attachment = content.Attachment;
        var module = await ModuleAsync();
        using var reference = new DotNetStreamReference(content.Content, leaveOpen: true);

        if (SupportAttachmentContentTypes.OpensInBrowser(attachment.ContentType))
            await module.InvokeAsync<bool>("open", reference, SupportAttachmentContentTypes.ServedAs(attachment.ContentType), attachment.FileName);
        else
            await module.InvokeVoidAsync("save", reference, attachment.FileName);
    }

    private async Task<IJSObjectReference> ModuleAsync()
        => _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public async ValueTask DisposeAsync()
    {
        if (_module == null) return;

        try
        {
            await _module.DisposeAsync();
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException)
        {
        }
    }
}
