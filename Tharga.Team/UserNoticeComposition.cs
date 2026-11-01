namespace Tharga.Team;

/// <summary>What a notice's composer is given when one user's mail of the notice is due.</summary>
/// <remarks>
/// <b>An occurrence means "this user may have something to hear about", not "this is what to tell them".</b> What the
/// mail says is read from the host's state now, which is why the occurrences are given here as hints at where to look
/// and not as the content.
/// </remarks>
public sealed record UserNoticeComposition
{
    /// <summary>The key of the notice.</summary>
    public required string NoticeKey { get; init; }

    /// <summary>The key of the user the mail is for.</summary>
    public required string UserKey { get; init; }

    /// <summary>The teams the waiting occurrences belong to, each once: a person in two teams gets one mail covering both.</summary>
    public required string[] TeamKeys { get; init; }

    /// <summary>The occurrences waiting, oldest first, at most <c>MaxItemsPerMail</c>.</summary>
    public required UserNoticeWaiting[] Waiting { get; init; }

    /// <summary>What the last mail of this notice to this user covered and when; empty when there was none.</summary>
    public required UserNoticeCoverage LastMail { get; init; }

    /// <summary>The time the mail is being composed, in UTC.</summary>
    public required DateTime Now { get; init; }
}

/// <summary>An occurrence waiting for a user's mail, and when it was raised.</summary>
/// <param name="Occurrence">The occurrence as it was raised; its recipients are not carried.</param>
/// <param name="RaisedAt">When it was raised, in UTC.</param>
public sealed record UserNoticeWaiting(UserNoticeOccurrence Occurrence, DateTime RaisedAt);

/// <summary>What the last mail of a notice to a user covered, and when each subject was last covered.</summary>
public sealed record UserNoticeCoverage
{
    /// <summary>When the last mail was sent, in UTC; null when none was.</summary>
    public DateTime? LastMailedAt { get; init; }

    /// <summary>
    /// Each subject any earlier mail of the notice covered to this user, and when it last did. A subject that a later
    /// mail did not cover keeps its earlier time, so "still broken four weeks after it was last mentioned" can be asked.
    /// </summary>
    public IReadOnlyDictionary<string, DateTime> Subjects { get; init; } = new Dictionary<string, DateTime>();

    /// <summary>Nothing was ever mailed.</summary>
    public static UserNoticeCoverage None { get; } = new();
}

/// <summary>What a composer decided to tell a user.</summary>
/// <remarks>
/// <b>Return null from the composer, not an empty mail, to send nothing.</b> The waiting occurrences are then dropped and
/// the minimum interval does not start, as nothing was sent.
/// </remarks>
public sealed record UserNoticeComposed
{
    /// <summary>The subject line; made one line, and the notice's category when empty.</summary>
    public required string Subject { get; init; }

    /// <summary>The HTML body. The host encodes whatever it inserts: nothing here is encoded for it.</summary>
    public string BodyHtml { get; init; }

    /// <summary>The subjects the mail covers, recorded on the notice items and as covered now, so the next composition can tell what was said when.</summary>
    public string[] CoveredSubjects { get; init; } = [];
}
