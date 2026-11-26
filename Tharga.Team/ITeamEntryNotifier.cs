namespace Tharga.Team;

/// <summary>
/// Tells the host when the team entry guard's answers for a person were forgotten, so a live circuit re-evaluates at once (Tharga/Team#471).
/// </summary>
/// <remarks>
/// <b>A singleton</b>, for the reasons <see cref="IUserSessionNotifier"/> gives, and with the same rules: handlers run on the thread that raised it,
/// subscribers unsubscribe, and it reaches this instance only. On several instances a host relays: it publishes each notification whose
/// <see cref="TeamEntryInvalidatedEventArgs.FromRemote"/> is false, and calls <see cref="Notify"/> with <c>FromRemote</c> set for what arrives from the others.
/// A relayed notification also makes this instance forget the answers it had remembered for that person.
/// </remarks>
public interface ITeamEntryNotifier
{
    /// <summary>Raised after answers were forgotten for a person.</summary>
    event EventHandler<TeamEntryInvalidatedEventArgs> Invalidated;

    /// <summary>Announces that answers were forgotten. Called by the toolkit, and by a host relaying a change made on another instance with <see cref="TeamEntryInvalidatedEventArgs.FromRemote"/> set.</summary>
    void Notify(TeamEntryInvalidatedEventArgs args);
}
