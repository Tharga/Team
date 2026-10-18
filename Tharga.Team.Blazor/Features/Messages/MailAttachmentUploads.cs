using Tharga.Team.Blazor.Features.Support;

namespace Tharga.Team.Blazor.Features.Messages;

/// <summary>The files chosen in a <see cref="SupportAttachmentPicker"/> as the bytes a message carries; one way for every compose view.</summary>
internal static class MailAttachmentUploads
{
    /// <summary>Each chosen file read from its start, with its name, type and size.</summary>
    public static async Task<UserMailAttachment[]> ReadAsync(IReadOnlyList<SupportAttachmentUpload> files)
    {
        var attachments = new List<UserMailAttachment>();

        foreach (var file in SupportAttachmentPicker.Rewound(files))
        {
            using var content = new MemoryStream();
            await file.Content.CopyToAsync(content);

            attachments.Add(new UserMailAttachment { FileName = file.FileName, ContentType = file.ContentType, Content = content.ToArray(), Size = content.Length });
        }

        return [.. attachments];
    }
}
