namespace Tharga.Team;

/// <summary>
/// An opened attachment: its metadata and a stream over its content, which the caller disposes.
/// </summary>
public sealed class SupportAttachmentContent : IDisposable, IAsyncDisposable
{
    /// <summary>What the attachment is.</summary>
    public required SupportAttachment Attachment { get; init; }

    /// <summary>The stored content, readable once from the start.</summary>
    public required Stream Content { get; init; }

    /// <summary>Releases the content stream.</summary>
    public void Dispose() => Content?.Dispose();

    /// <summary>Releases the content stream.</summary>
    public ValueTask DisposeAsync() => Content?.DisposeAsync() ?? ValueTask.CompletedTask;
}
