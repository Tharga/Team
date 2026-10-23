namespace Tharga.Team;

/// <summary>Which mail the global mail log lists. Every part given narrows the list further; with none given it lists all mail.</summary>
public sealed record UserMailFilter
{
    /// <summary>Only mail of this kind.</summary>
    public UserMailKind? Kind { get; init; }

    /// <summary>Only mail in the category with this key.</summary>
    public string Category { get; init; }

    /// <summary>Only mail that stands here.</summary>
    public UserMailStatus? Status { get; init; }

    /// <summary>Only mail queued at or after this time, in UTC.</summary>
    public DateTime? From { get; init; }

    /// <summary>Only mail queued before this time, in UTC.</summary>
    public DateTime? To { get; init; }

    /// <summary>Only mail for the user with this key.</summary>
    public string RecipientUserKey { get; init; }

    /// <summary>Only mail addressed to this address, compared trimmed and in lower case; a mail to a user is addressed to the address on their record when it is sent.</summary>
    public string RecipientAddress { get; init; }

    /// <summary>Only mail about the team with this key, where the mail records one.</summary>
    public string TeamKey { get; init; }
}
