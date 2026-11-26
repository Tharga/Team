namespace Tharga.Team;

/// <summary>
/// A session of a person ended (Tharga/Team#471).
/// </summary>
/// <remarks>
/// <b>It names the person and why, and nothing that could be used as a credential.</b> There is deliberately no session id here: it would reach every listener
/// and travel over the host's backplane. A listener that needs the session reads it from the store.
/// </remarks>
public sealed class UserSessionEndedEventArgs : EventArgs
{
    /// <summary>The key of the person whose session ended.</summary>
    public required string UserKey { get; init; }

    /// <summary>Why it ended.</summary>
    public required SessionEndReason Reason { get; init; }

    /// <summary>
    /// True when the session ended on another instance and a host relayed it to this one.
    /// </summary>
    /// <remarks>Set by a host's own relay, never by the toolkit. A relay publishes the notifications where this is false and passes on what arrives with it set, so an end is not handed back and forth between instances.</remarks>
    public bool FromRemote { get; init; }
}
