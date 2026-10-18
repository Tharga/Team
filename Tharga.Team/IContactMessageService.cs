namespace Tharga.Team;

/// <summary>Mail the product's staff write to contacts — people who are not users, listed by the host from its own records — and the log of every message sent; every member requires the system scope <see cref="SystemContactScopes.Send"/>.</summary>
/// <remarks>
/// <b>The host chooses the addressees</b> and hands them over; this service mails each distinct address once, leaves out
/// those that opted out of the category or of all contact mail, and puts a footer saying who sends it, why it came and how
/// to stop it, with an unsubscribe link, in every mail.
/// <para>
/// <b>Gated</b>: registered as a system service, so a call without the scope is refused before it reaches the
/// implementation and the refusal is audited. Sending, reading the log and opting an address out are audited by the
/// service itself, with counts and never an address, so the per-call access trace is turned off.
/// </para>
/// </remarks>
public interface IContactMessageService
{
    /// <summary>The contact categories a message may be written in, and the limits it is held to.</summary>
    [RequireScope(SystemContactScopes.Send, Audit = AuditMode.None)]
    Task<ContactMessageChoices> GetChoicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Each distinct address of the list in the order first listed, with whether it will be mailed in the category: reached, opted out of the category or of all contact mail, or left out as no address or for the host's reason.</summary>
    /// <remarks>Not audited, as reading counts is not; the same check <see cref="SendAsync"/> makes.</remarks>
    /// <exception cref="ArgumentException">The category is not a contact category, or the list holds more rows than allowed.</exception>
    [RequireScope(SystemContactScopes.Send, Audit = AuditMode.None)]
    Task<ContactAddresseeList> GetAddresseesAsync(ContactAddressee[] addressees, string category, CancellationToken cancellationToken = default);

    /// <summary>The message as a contact would receive it — sender, subject, the body in the host's template, and the footer — rendered without sending or storing anything.</summary>
    /// <remarks>The list is not read, and the unsubscribe link in it opts nobody out; files are not rendered.</remarks>
    /// <exception cref="ArgumentException">The category is not a contact category.</exception>
    [RequireScope(SystemContactScopes.Send, Audit = AuditMode.None)]
    Task<MailPreview> PreviewMailAsync(ContactMessage message, CancellationToken cancellationToken = default);

    /// <summary>Queues one mail per distinct address that has not opted out, stores the content once, returns at once with every listed address and where it stands — with the host's references — and audits the send.</summary>
    /// <exception cref="ArgumentException">The category is not a contact category, the list holds more rows than allowed or no address that can be mailed, or the message has no subject or body or exceeds the limits.</exception>
    [RequireScope(SystemContactScopes.Send, Audit = AuditMode.None)]
    Task<SentContactMessage> SendAsync(ContactMessage message, CancellationToken cancellationToken = default);

    /// <summary>One page of the log of messages to contacts, newest first, with each message's counts.</summary>
    [RequireScope(SystemContactScopes.Send, Audit = AuditMode.None)]
    Task<SentContactMessagePage> GetSentAsync(string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>One sent message with its counts, or null when there is none with this id.</summary>
    [RequireScope(SystemContactScopes.Send, Audit = AuditMode.None)]
    Task<SentContactMessage> GetSentMessageAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>One page of a sent message's addresses — those queued in the order they were, then those never queued — each with what became of the mail and the host's references, only those in <paramref name="status"/> when one is given; null when there is no message with this id.</summary>
    /// <exception cref="ArgumentException">The cursor was not issued for this list.</exception>
    [RequireScope(SystemContactScopes.Send, Audit = AuditMode.None)]
    Task<ContactMessageRecipientPage> GetRecipientsAsync(string messageId, ContactRecipientStatus? status = null, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default);

    /// <summary>Opts an address out of one contact category, or with no category of all contact mail, as asked by the person some other way than the link; true when it was not opted out so before.</summary>
    /// <exception cref="ArgumentException">The address is not a mail address, or the category is not a contact category.</exception>
    [RequireScope(SystemContactScopes.Send, Audit = AuditMode.None)]
    Task<bool> SuppressAsync(string address, string category = null, CancellationToken cancellationToken = default);
}
