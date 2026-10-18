namespace Tharga.Team;

/// <summary>A mail as its recipient would receive it, rendered from a draft without sending or storing anything.</summary>
/// <remarks>
/// <see cref="Html"/> is a whole mail body, the host's template included, and carries the host's own styles; show it
/// isolated from the page, as a sandboxed frame without scripts does, never as markup in the page itself.
/// </remarks>
public sealed record MailPreview
{
    /// <summary>The sender as the recipient sees it, name and address.</summary>
    public string From { get; init; }

    /// <summary>Where a reply goes, when not to the sender.</summary>
    public string ReplyTo { get; init; }

    /// <summary>The address it goes to, when it goes to one person; null for a mail to many.</summary>
    public string To { get; init; }

    /// <summary>The subject line, as it reads in production.</summary>
    public string Subject { get; init; }

    /// <summary>The HTML body, in the host's template; null when the mail is sent as text alone.</summary>
    public string Html { get; init; }

    /// <summary>The text body, which is also the alternative an HTML mail carries.</summary>
    public string Text { get; init; }
}
