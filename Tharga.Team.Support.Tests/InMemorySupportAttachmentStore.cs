namespace Tharga.Team.Support.Tests;

/// <summary>
/// An attachment store in memory, keyed by case the way the real adapter is: an id quoted against another case
/// finds nothing.
/// </summary>
internal sealed class InMemorySupportAttachmentStore : ISupportAttachmentStore
{
    private readonly List<(string CaseId, SupportAttachment Attachment, byte[] Content)> _files = [];

    /// <summary>Files whose name matches throw when saved, as a store that is down would.</summary>
    public Func<string, bool> FailsFor { get; init; } = _ => false;

    public IReadOnlyList<(string CaseId, SupportAttachment Attachment, byte[] Content)> Files => _files;

    public List<string> Deleted { get; } = [];

    public async Task<SupportAttachment> SaveAsync(string caseId, SupportAttachmentUpload upload, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, cancellationToken);

        if (FailsFor(upload.FileName)) throw new IOException("The store is down.");

        var attachment = new SupportAttachment
        {
            Id = Guid.NewGuid().ToString("N"),
            FileName = upload.FileName,
            ContentType = upload.ContentType,
            Size = buffer.Length,
            Inline = upload.Inline,
            ContentId = upload.ContentId
        };

        _files.Add((caseId, attachment, buffer.ToArray()));

        return attachment;
    }

    public Task<SupportAttachmentContent> OpenAsync(string caseId, string attachmentId, CancellationToken cancellationToken = default)
    {
        var found = _files.FirstOrDefault(x => x.CaseId == caseId && x.Attachment.Id == attachmentId);

        return Task.FromResult(found.Attachment == null
            ? null
            : new SupportAttachmentContent { Attachment = found.Attachment, Content = new MemoryStream(found.Content, false) });
    }

    public Task DeleteAsync(string caseId, string attachmentId, CancellationToken cancellationToken = default)
    {
        if (_files.RemoveAll(x => x.CaseId == caseId && x.Attachment.Id == attachmentId) > 0) Deleted.Add(attachmentId);

        return Task.CompletedTask;
    }

    public Task<int> DeleteForCaseAsync(string caseId, CancellationToken cancellationToken = default)
    {
        var removed = _files.Where(x => x.CaseId == caseId).ToArray();
        _files.RemoveAll(x => x.CaseId == caseId);
        Deleted.AddRange(removed.Select(x => x.Attachment.Id));

        return Task.FromResult(removed.Length);
    }

    public byte[] ContentOf(string attachmentId) => _files.Single(x => x.Attachment.Id == attachmentId).Content;
}
