namespace Tharga.Team;

/// <summary>
/// What a support person writes to start a conversation by mail: to whom, about what, and from where.
/// </summary>
/// <remarks>
/// For the person who has to write first, to somebody who filled in a contact form days ago or to a prospect. The mail
/// goes out as an answer does, so the reply threads into the case that is opened for it.
/// </remarks>
public sealed record StartMailCaseRequest
{
    /// <summary>The address the mail goes to, and that the case keeps as its contact. Required; a single plain address.</summary>
    public required string To { get; init; }

    /// <summary>The recipient's name, when known. Optional: the address names them when it is empty.</summary>
    public string ToName { get; init; }

    /// <summary>
    /// What else is known about the recipient, kept on the case as its contact. <see cref="To"/> and <see cref="ToName"/>
    /// are what the case is started with; anything set here beyond them is kept as given.
    /// </summary>
    public ExternalSupportContact Contact { get; init; }

    /// <summary>The team the case belongs to, or null for an unassigned case.</summary>
    public string TeamKey { get; init; }

    /// <summary>The subject of the mail. Derived from the message when empty.</summary>
    public string Subject { get; init; }

    /// <summary>The message as text.</summary>
    public string Body { get; init; }

    /// <summary>The message as HTML, when it is formatted. Sanitized as an answer is; the text is derived from it.</summary>
    public string BodyHtml { get; init; }

    /// <summary>Files to send with it.</summary>
    public IReadOnlyList<SupportAttachmentUpload> Attachments { get; init; }

    /// <summary>The address it is sent from and whether it is signed with the sender's name, as when answering.</summary>
    public SupportAnswerChoice Choice { get; init; }
}
