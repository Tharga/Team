namespace Tharga.Team;

/// <summary>
/// Tells the host when a person's server-side session ended, whichever way it ended (Tharga/Team#471).
/// </summary>
/// <remarks>
/// <b>A singleton, and it has to be one.</b> A session is ended in a request scope (a sign-out, a refused request) or by a live circuit, and the circuits that
/// should hear of it live in other scopes. An event raised on a scoped service reaches only the instance that raised it, so the two would never meet.
/// <para>
/// <b>Handlers run on whichever thread ended the session.</b> A component must marshal with <c>InvokeAsync</c> before touching its own state.
/// </para>
/// <para>
/// <b>Unsubscribe.</b> This outlives every page that listens to it, so a component that subscribes and never detaches keeps itself alive for the lifetime of the
/// application. Subscribe in <c>OnInitialized</c>, unsubscribe in <c>Dispose</c>.
/// </para>
/// <para>
/// <b>It decides nothing.</b> A notification is a hint to look again: a listener reads the session from the store, which is the only truth, and acts on that.
/// </para>
/// <para>
/// <b>It names the person, never the session.</b> A session id is the credential's key and must not travel to listeners or across a backplane.
/// </para>
/// <para>
/// <b>It reaches this instance only.</b> On several instances a host relays over its own backplane: it publishes each notification whose
/// <see cref="UserSessionEndedEventArgs.FromRemote"/> is false, and calls <see cref="Notify"/> with <c>FromRemote</c> set for what arrives from the other
/// instances. Every open circuit of that person then learns within seconds.
/// </para>
/// </remarks>
public interface IUserSessionNotifier
{
    /// <summary>Raised after a session of a person ended.</summary>
    event EventHandler<UserSessionEndedEventArgs> SessionEnded;

    /// <summary>Announces an end. Called by the toolkit, and by a host relaying an end that happened on another instance with <see cref="UserSessionEndedEventArgs.FromRemote"/> set.</summary>
    void Notify(UserSessionEndedEventArgs args);
}
