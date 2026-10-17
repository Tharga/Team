namespace Tharga.Team;

/// <summary>
/// A received mail as it arrived: the raw MIME message, with its body and attachments.
/// </summary>
public record MailContent
{
    public required string MailId { get; init; }

    /// <summary>The message in RFC 5322 form, as a <c>.eml</c> file holds it.</summary>
    public required byte[] Raw { get; init; }
}
