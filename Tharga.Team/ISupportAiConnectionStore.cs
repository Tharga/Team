namespace Tharga.Team;

/// <summary>
/// Keeps each team's AI connection. The persistence port behind <see cref="ISupportAiConnectionService"/>.
/// </summary>
/// <remarks>
/// <b>It never sees a key in clear.</b> The service encrypts the key before handing the record over, so an adapter
/// stores, and a database dump holds, only what Data Protection produced.
/// <para>
/// One record per team. <see cref="SetAsync"/> replaces the team's record whole, because a connection is one fact:
/// a model kept from one provider beside an address from another would answer from neither.
/// </para>
/// </remarks>
public interface ISupportAiConnectionStore
{
    /// <summary>The team's record, or null when the team never chose.</summary>
    Task<StoredSupportAiConnection> GetAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>Replaces the team's record with <paramref name="connection"/>.</summary>
    Task SetAsync(StoredSupportAiConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Removes the team's record, when the team itself is removed; true when there was one.</summary>
    Task<bool> DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default);
}

/// <summary>A team's AI connection as stored: the key only as Data Protection protected it.</summary>
public sealed record StoredSupportAiConnection
{
    /// <summary>The team.</summary>
    public required string TeamKey { get; init; }

    /// <summary>What the assistant answers the team with.</summary>
    public SupportAiConnectionKind Kind { get; init; }

    /// <summary>The provider's address; null for the platform's model and off.</summary>
    public string BaseAddress { get; init; }

    /// <summary>The model at the provider; null for the platform's model and off.</summary>
    public string Model { get; init; }

    /// <summary>The key as Data Protection protected it; never the key itself. Null when no key is stored.</summary>
    public string ProtectedApiKey { get; init; }

    /// <summary>When the connection was set.</summary>
    public DateTime ChangedAt { get; init; }

    /// <summary>Who set it, as the stable subject.</summary>
    public string ChangedByIdentity { get; init; }

    /// <summary>Who set it, by name when it was set.</summary>
    public string ChangedByName { get; init; }
}
