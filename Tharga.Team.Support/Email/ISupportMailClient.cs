namespace Tharga.Team.Support.Email;

/// <summary>
/// Sends and reads mail for the support module.
/// </summary>
/// <remarks>
/// Deliberately knows nothing about teams, users or cases. Everything in this namespace is about mail and
/// only mail, so it can be lifted into a standalone package as a move rather than a rewrite.
/// <c>TransportNamespaceIsolationTests</c> enforces that.
/// <para>
/// <b>Never throws.</b> Every failure — no configuration, no network, a server rejection — comes back as a
/// failed result or an empty fetch, matching <c>ISlackClient</c>. A support case is written before anything
/// is sent and is authoritative, so a mail server being down must not become the caller's problem.
/// </para>
/// </remarks>
public interface ISupportMailClient
{
    /// <summary>Whether enough is configured to send. False leaves the channel dormant.</summary>
    bool CanSend { get; }

    /// <summary>Whether enough is configured to read.</summary>
    bool CanRead { get; }

    /// <summary>Sends one mail.</summary>
    /// <returns>The outcome, carrying the <c>Message-ID</c> that identifies the thread.</returns>
    Task<MailSendResult> SendAsync(OutboundMail mail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads mail that arrived after <paramref name="position"/>.
    /// </summary>
    /// <remarks>
    /// <b>Nothing in the mailbox is modified.</b> No flag is set and no message is moved: the mailbox may be
    /// shared with another application reading it for its own domain, and a flag meaning "handled" to one of
    /// them hides the message from the other. Progress is the returned position, kept by the caller.
    /// </remarks>
    Task<MailFetchResult> FetchAsync(MailFetchPosition position, CancellationToken cancellationToken = default);
}
