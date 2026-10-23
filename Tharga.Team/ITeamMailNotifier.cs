namespace Tharga.Team;

/// <summary>
/// Tells the host when mail arrived, was handled or was routed.
/// </summary>
/// <remarks>
/// <b>A singleton, and in-process.</b> Mail is read by a background poller, so the event is raised outside any
/// request or circuit; a handler marshals before touching component state. It reaches subscribers in this
/// process only — a host with several instances relays it over its own backplane.
/// <para>
/// <b>Relaying the postmaster's changes.</b> The mail the postmaster holds belongs to no team, so there is no team group to
/// send a change to; relay the events whose <see cref="TeamMailUpdatedEventArgs.AffectsPostmaster"/> is true to the staff who
/// may read the postmaster, and have each of their pages call <c>PostmasterView.RefreshAsync</c>. Do not answer a relayed
/// event with <see cref="Notify"/> while also relaying from <see cref="MailUpdated"/>: it would be relayed again, and again.
/// </para>
/// <para>
/// <b>It carries no authorization.</b> Every subscriber hears about every team's mail; a handler that wants the
/// mail reads it back through <see cref="ITeamMailboxService"/>, where the checks are.
/// </para>
/// </remarks>
public interface ITeamMailNotifier
{
    /// <summary>Raised after a mail is received, handled or routed.</summary>
    event EventHandler<TeamMailUpdatedEventArgs> MailUpdated;

    /// <summary>Announces a change. Called by the toolkit; a host subscribes rather than raises.</summary>
    void Notify(TeamMailUpdatedEventArgs args);
}
