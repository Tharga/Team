namespace Tharga.Team.MongoDB;

/// <summary>
/// MongoDB adapter for <see cref="ISupportAttachmentStore"/>: each file in GridFS, described by the case it
/// belongs to.
/// </summary>
/// <remarks>
/// <b>The case is checked on every read and delete, not only by the caller.</b> An id names one file wherever
/// it is, so the stored case id is compared before anything is returned or removed: an attachment id quoted
/// against another case finds nothing.
/// </remarks>
internal sealed class MongoSupportAttachmentStore(ISupportAttachmentBucket bucket) : ISupportAttachmentStore
{
    public async Task<SupportAttachment> SaveAsync(string caseId, SupportAttachmentUpload upload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentNullException.ThrowIfNull(upload);

        var id = await bucket.UploadAsync(new SupportAttachmentFile
        {
            CaseId = caseId,
            FileName = upload.FileName,
            ContentType = upload.ContentType,
            Inline = upload.Inline,
            ContentId = upload.ContentId
        }, upload.Content, cancellationToken);

        var stored = await bucket.FindAsync(id, cancellationToken)
                     ?? throw new InvalidOperationException($"The attachment '{upload.FileName}' was written but could not be read back.");

        return ToAttachment(stored);
    }

    public async Task<SupportAttachmentContent> OpenAsync(string caseId, string attachmentId, CancellationToken cancellationToken = default)
    {
        var stored = await FindForCaseAsync(caseId, attachmentId, cancellationToken);
        if (stored == null) return null;

        return new SupportAttachmentContent
        {
            Attachment = ToAttachment(stored),
            Content = await bucket.OpenAsync(stored.Id, cancellationToken)
        };
    }

    public async Task DeleteAsync(string caseId, string attachmentId, CancellationToken cancellationToken = default)
    {
        var stored = await FindForCaseAsync(caseId, attachmentId, cancellationToken);
        if (stored != null) await bucket.DeleteAsync(stored.Id, cancellationToken);
    }

    public async Task<int> DeleteForCaseAsync(string caseId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caseId)) return 0;

        var files = await bucket.FindByCaseAsync(caseId, cancellationToken);

        foreach (var file in files) await bucket.DeleteAsync(file.Id, cancellationToken);

        return files.Length;
    }

    private async Task<SupportAttachmentFile> FindForCaseAsync(string caseId, string attachmentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(caseId) || string.IsNullOrWhiteSpace(attachmentId)) return null;

        var stored = await bucket.FindAsync(attachmentId, cancellationToken);

        return stored != null && string.Equals(stored.CaseId, caseId, StringComparison.Ordinal) ? stored : null;
    }

    private static SupportAttachment ToAttachment(SupportAttachmentFile file) => new()
    {
        Id = file.Id,
        FileName = file.FileName,
        ContentType = file.ContentType,
        Size = file.Length,
        Inline = file.Inline,
        ContentId = file.ContentId
    };
}
