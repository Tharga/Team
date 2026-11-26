namespace Tharga.Team;

/// <summary>The team entry guard's answers for a person were forgotten.</summary>
public sealed class TeamEntryInvalidatedEventArgs : EventArgs
{
    /// <summary>The key of the person the answers were forgotten for.</summary>
    public required string UserKey { get; init; }

    /// <summary>The team only that team's answers were forgotten for, or null for every team.</summary>
    public string TeamKey { get; init; }

    /// <summary>True when the change was made on another instance and a host relayed it to this one; set by the host's relay, never by the toolkit.</summary>
    public bool FromRemote { get; init; }
}
