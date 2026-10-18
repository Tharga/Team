using System.ComponentModel;

namespace Tharga.Team;

/// <summary>Keeps support attachment content outside the case document, keyed by case id; internal and unchecked, so read files through <c>ISupportCaseService.OpenAttachmentAsync</c>, never by injecting this.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISupportAttachmentStore
{
    /// <summary>Stores one file for a case, reading the upload's stream to its end, and returns what was stored with its new id and size.</summary>
    Task<SupportAttachment> SaveAsync(string caseId, SupportAttachmentUpload upload, CancellationToken cancellationToken = default);

    /// <summary>Opens a file stored for this case, or returns null when the case holds no attachment with this id.</summary>
    Task<SupportAttachmentContent> OpenAsync(string caseId, string attachmentId, CancellationToken cancellationToken = default);

    /// <summary>Removes one file stored for this case; nothing happens when there is none.</summary>
    Task DeleteAsync(string caseId, string attachmentId, CancellationToken cancellationToken = default);

    /// <summary>Removes every file stored for a case and returns how many there were.</summary>
    Task<int> DeleteForCaseAsync(string caseId, CancellationToken cancellationToken = default);
}
