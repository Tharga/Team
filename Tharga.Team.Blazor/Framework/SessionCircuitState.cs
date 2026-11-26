namespace Tharga.Team.Blazor.Framework;

/// <summary>Where a circuit's session stands.</summary>
internal enum SessionCircuitState
{
    /// <summary>Nothing to say: the session lasts, or sessions are not active.</summary>
    Watching,

    /// <summary>The person is about to be signed out for being idle, and a view is attached to ask whether they are still there.</summary>
    Warning,

    /// <summary>The session ended; final. <see cref="ISessionCircuit.EndedLocation"/> says where the browser goes.</summary>
    Ended
}
