namespace Tharga.Team;

/// <summary>Told once when a mail ends badly: given up after its retries, refused by the queue, or returned by the receiving server; for a host that wants to tell whoever sent it.</summary>
/// <remarks>
/// <b>Called once per mail, on the transition only.</b> A bounce that is read again, by another instance or after a restart, and a mail
/// another instance gave up first, do not call it a second time. Every registered sink is told; one that throws is logged and the others
/// still run, and a sink that throws never changes what is recorded for the mail.
/// <para>
/// The outcome names the mail by id and does not carry its address or the server's words, which often quote it. A host that wants to show
/// what failed reads the mail by <see cref="MailOutcome.MailId"/> through <c>IUserMailHistoryService.GetMailAsync</c>, which the person who
/// sent it, or staff, may do without being given the whole log.
/// </para>
/// </remarks>
public interface IMailOutcomeSink
{
    /// <summary>The mail ended as <paramref name="outcome"/> describes.</summary>
    Task OnMailOutcomeAsync(MailOutcome outcome, CancellationToken cancellationToken = default);
}

/// <summary>How a mail ended badly.</summary>
public enum MailOutcomeKind
{
    /// <summary>It was tried and failed for good: the retries ran out, or it could not be sent at all.</summary>
    GivenUp,

    /// <summary>The queue refused it when it was handed in, so it was never attempted.</summary>
    Refused,

    /// <summary>The mail server accepted it and the receiving server returned it as undeliverable.</summary>
    Bounced
}

/// <summary>What <see cref="IMailOutcomeSink"/> is told about a mail that ended badly.</summary>
public sealed record MailOutcome
{
    /// <summary>The mail's id, which <c>IUserMailHistoryService.GetMailAsync</c> reads it by; null for a refusal that stored nothing.</summary>
    public string MailId { get; init; }

    /// <summary>How it ended.</summary>
    public required MailOutcomeKind Outcome { get; init; }

    /// <summary>Which kind of mail it was.</summary>
    public UserMailKind Kind { get; init; }

    /// <summary>The subject line.</summary>
    public string Subject { get; init; }

    /// <summary>Why, in the words recorded for the mail.</summary>
    public string Reason { get; init; }

    /// <summary>The key of the user who sent it, or null when the application did; who the host may tell.</summary>
    public string SentByUserKey { get; init; }

    /// <summary>The key of the user it was for, or null for a mail to an address alone.</summary>
    public string RecipientUserKey { get; init; }

    /// <summary>The key of the team it is about, or null.</summary>
    public string TeamKey { get; init; }

    /// <summary>The support case it is part of, or null.</summary>
    public string CaseId { get; init; }

    /// <summary>When it ended, in UTC.</summary>
    public required DateTime At { get; init; }
}
