namespace Tharga.Team;

/// <summary>A mail sent outside the user mail queue, such as a support answer or a receipt, as <see cref="IUserMailOutbox.RecordAsync"/> keeps it so the mail log shows it.</summary>
/// <remarks>It is recorded already done, sent or given up, so no sender ever claims it; its body and files are not kept.</remarks>
public sealed record UserMailRecord
{
    /// <summary>Which kind of mail it is.</summary>
    public required UserMailKind Kind { get; init; }

    /// <summary>The address it was addressed to.</summary>
    public required string RecipientAddress { get; init; }

    /// <summary>The name it was addressed to, or null.</summary>
    public string RecipientName { get; init; }

    /// <summary>The subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>The key of a category it was sent in, or null for a mail outside the categories, as support mail is.</summary>
    public string Category { get; init; }

    /// <summary>The key of the team it is about, or null when it belongs to none.</summary>
    public string TeamKey { get; init; }

    /// <summary>The key of the user who caused it to be sent, such as the staff member who wrote an answer; null for mail the application sent on its own.</summary>
    public string SentByUserKey { get; init; }

    /// <summary>The name of the user who caused it to be sent; null for mail the application sent on its own.</summary>
    public string SentByName { get; init; }

    /// <summary>The From and Reply-To it was sent with, or would have been when it was not; null when not known.</summary>
    public UserMailHeaders Headers { get; init; }

    /// <summary>Whether it was sent or given up; no other status can be recorded.</summary>
    public required UserMailStatus Status { get; init; }

    /// <summary>The <c>Message-ID</c> it was sent with, by which a bounce names it; null when it was not sent.</summary>
    public string MessageId { get; init; }

    /// <summary>Why it was not sent; null when it was.</summary>
    public string Reason { get; init; }

    /// <summary>When it was sent or given up, in UTC.</summary>
    public required DateTime At { get; init; }

    /// <summary>The support case it belongs to, or null; a second record naming the same kind, case and entry replaces the first's outcome.</summary>
    public string CaseId { get; init; }

    /// <summary>The entry of the case it mailed, or null for a mail that mailed no entry, such as a receipt.</summary>
    public int? CaseMessageSequence { get; init; }
}
