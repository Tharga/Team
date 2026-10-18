using Radzen;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Features.Support;

/// <summary>Where support's answer goes and what it looks like when mailed, as every answer box reads and shows them.</summary>
internal static class SupportAnswerPreview
{
    /// <summary>Where the case's answer goes, or null when the reader may not answer it and so is shown nothing.</summary>
    public static async Task<SupportAnswerRoute> RouteAsync(ISupportCaseService cases, SupportCase supportCase)
    {
        if (supportCase == null) return null;

        try
        {
            return await cases.GetAnswerRouteAsync(supportCase.TeamKey, supportCase.Id);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Opens the preview with the names of the files chosen, or nothing when there is no mail to show.</summary>
    public static Task ShowAsync(DialogService dialogs, TextSet text, MailPreview preview, IReadOnlyList<SupportAttachmentUpload> files)
        => preview == null
            ? Task.CompletedTask
            : MailPreviewDialog.ShowAsync(dialogs, text[MailPreviewText.Title], preview, (files ?? []).Select(x => x.FileName));
}
