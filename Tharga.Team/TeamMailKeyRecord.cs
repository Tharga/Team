namespace Tharga.Team;

/// <summary>
/// A team's mail key as it is kept, retired keys included, so mail to a retired key is known to be retired rather than unknown.
/// </summary>
public record TeamMailKeyRecord
{
    public required string TeamKey { get; init; }

    public required string Key { get; init; }

    public required MailKeyRole Role { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>When a replaced key stops being accepted; null for the primary.</summary>
    public DateTime? RetiresAt { get; init; }

    /// <summary>A replaced key stops being accepted at the moment it retires, not a moment after.</summary>
    public bool HasRetired(DateTime now) => RetiresAt is { } retires && now >= retires;
}
