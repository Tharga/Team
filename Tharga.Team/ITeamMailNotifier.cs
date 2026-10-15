namespace Tharga.Team;

/// <summary>
/// Tells the host when mail arrived, was handled or was routed.
/// </summary>
/// <remarks>
/// <b>A singleton, and in-process.</b> Mail is read by a background poller, so the event is raised outside any
/// request or circuit; a handler marshals before touching component state. It reaches subscribers in this
/// process only — a host with several instances relays it over its own backplane.
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
