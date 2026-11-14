namespace Tharga.Team;

/// <summary>What became of one logged mail that was given up or bounced, thin by design: read the rest from the mail log by its id.</summary>
public sealed record MailOutcome
{
    /// <summary>The id of the mail's row in the mail log; null when the log keeps no row for it.</summary>
    public string Id { get; init; }

    /// <summary>Which kind of mail it was.</summary>
    public required UserMailKind Kind { get; init; }

    /// <summary>What became of it: <see cref="UserMailStatus.Failed"/> or <see cref="UserMailStatus.Bounced"/>.</summary>
    public required UserMailStatus Status { get; init; }

    /// <summary>Why it was given up, or the receiving server's reason it bounced.</summary>
    public string Reason { get; init; }

    /// <summary>The key of the user who caused it to be sent; null for mail the application sent on its own.</summary>
    public string SentByUserKey { get; init; }

    /// <summary>The key of the team it is about; null when it records none.</summary>
    public string TeamKey { get; init; }

    /// <summary>The support case a support answer or receipt belongs to; null for any other mail.</summary>
    public string CaseId { get; init; }

    /// <summary>The entry of the case a support answer mailed; null for a receipt and for any other mail.</summary>
    public int? CaseMessageSequence { get; init; }

    /// <summary>When it was given up or the bounce was read, in UTC.</summary>
    public required DateTime At { get; init; }
}
