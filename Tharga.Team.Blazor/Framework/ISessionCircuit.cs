namespace Tharga.Team.Blazor.Framework;

/// <summary>What the circuit's session looks like to the view that shows "Still here?".</summary>
internal interface ISessionCircuit : IDisposable
{
    /// <summary>Whether there is a session to watch: sessions are active and the circuit's caller has one. False means a view renders nothing and loads nothing.</summary>
    bool Active { get; }

    SessionCircuitState State { get; }

    /// <summary>While <see cref="SessionCircuitState.Warning"/>, when the session is signed out as idle (server clock); otherwise null.</summary>
    DateTime? IdleAt { get; }

    /// <summary>While <see cref="SessionCircuitState.Warning"/>, how long is left; for the countdown to start from, never to decide anything.</summary>
    TimeSpan? Remaining { get; }

    /// <summary>Once <see cref="SessionCircuitState.Ended"/>, where the browser goes (<see cref="SessionCookieEvents.EndedLocation(string, string, SessionEndReason)"/>); otherwise null.</summary>
    string EndedLocation { get; }

    /// <summary>Raised when <see cref="State"/> changes. Runs on whichever thread changed it: a view marshals before it touches its own state.</summary>
    event Action Changed;

    /// <summary>Starts watching; fire and forget from the circuit handler, so it runs without any component. Never throws.</summary>
    Task StartAsync();

    /// <summary>A view is on screen and can ask the question. Dispose the result when it goes. Without a view nobody is warned.</summary>
    IDisposable Attach();

    /// <summary>The person said they are still here. Writes only while <see cref="SessionCircuitState.Warning"/>.</summary>
    Task StayAsync();

    /// <summary>The person chose to sign out: ends the session as signed out, once.</summary>
    Task SignOutAsync();

    /// <summary>Something suggests the session changed (another tab was active, another ended): look at the store now. A hint decides nothing.</summary>
    Task CheckAsync();
}
