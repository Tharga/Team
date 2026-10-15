namespace Tharga.Team;

/// <summary>
/// What a received mail says about itself, without its body or attachments.
/// </summary>
/// <remarks>
/// The body and attachments stay in the raw message, which the host keeps through
/// <see cref="IInboundMailContentStore"/> and a caller reads through the mailbox service, where access is checked.
/// </remarks>
public record MailEnvelope
{
    public required string MessageId { get; init; }

    public required string From { get; init; }

    public string FromName { get; init; }

    public string[] To { get; init; } = [];

    public string[] Cc { get; init; } = [];

    /// <summary>The address the mail was actually delivered to, from the envelope headers, which routing used.</summary>
    public required string DeliveredTo { get; init; }

    public string Subject { get; init; }

    public DateTime? SentAt { get; init; }

    public string InReplyTo { get; init; }

    public string[] References { get; init; } = [];

    public bool HasAttachments { get; init; }

    /// <summary>True for an auto-reply, a list mail or a bounce.</summary>
    public bool IsAutomated { get; init; }
}
