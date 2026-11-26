namespace Tharga.Team;

/// <summary>Where one file sits in the support cases: the case, the entry that carries it and its id.</summary>
public sealed record SupportAttachmentLocation
{
    /// <summary>The team the case belongs to; null for a case no team owns.</summary>
    public string TeamKey { get; init; }

    /// <summary>The case's id.</summary>
    public required string CaseId { get; init; }

    /// <summary>The <see cref="SupportMessage.Sequence"/> of the entry that carries the file.</summary>
    public required int Sequence { get; init; }

    /// <summary>The file's <see cref="SupportAttachment.Id"/>.</summary>
    public required string AttachmentId { get; init; }
}
