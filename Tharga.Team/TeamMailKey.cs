namespace Tharga.Team;

/// <summary>
/// One of a team's mail keys, and the address it makes.
/// </summary>
public record TeamMailKey
{
    /// <summary>The opaque key; it names no team and cannot be guessed from one.</summary>
    public required string Key { get; init; }

    public required MailKeyRole Role { get; init; }

    /// <summary>The team's address with this key.</summary>
    public required string Address { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>When a secondary key stops being accepted; null for the primary.</summary>
    public DateTime? RetiresAt { get; init; }
}
