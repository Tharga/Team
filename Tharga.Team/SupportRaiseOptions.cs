namespace Tharga.Team;

/// <summary>What a host can ask for when it raises a case beyond the subject and message.</summary>
public sealed record SupportRaiseOptions
{
    /// <summary>Whether the assistant answers first; default none.</summary>
    public SupportAssistance Assistance { get; init; } = SupportAssistance.None;

    /// <summary>
    /// An address to answer the case by mail to, so it is bound to the mail channel from the start and support's answers are mailed without anyone
    /// having to switch. Null raises the case as before: answered on the site.
    /// </summary>
    /// <remarks>
    /// <b>For host code only.</b> It is the host that knows the author's address, and whatever it passes here is where answers go, so never pass
    /// what the author typed without checking it belongs to them. A single plain address is required; anything else is refused.
    /// </remarks>
    public string MailAddress { get; init; }

    /// <summary>The name of the support mailbox answers are sent from; null for the primary one. Only meaningful with <see cref="MailAddress"/>.</summary>
    public string Mailbox { get; init; }
}
