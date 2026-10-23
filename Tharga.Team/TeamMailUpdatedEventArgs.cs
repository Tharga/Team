namespace Tharga.Team;

/// <summary>
/// Something happened to a received mail: enough to update a counter or re-read one mail, deliberately not its content.
/// </summary>
public sealed class TeamMailUpdatedEventArgs : EventArgs
{
    /// <summary>The team the mail belongs to now; null while the postmaster holds it.</summary>
    public string TeamKey { get; init; }

    public required string MailId { get; init; }

    public required TeamMailChange Change { get; init; }

    /// <summary>
    /// Whether the change alters what the postmaster holds: mail it took in, archived or restored, which carry no team,
    /// and mail it routed to a team, which carries the team it went to and is how a mail leaves the list.
    /// </summary>
    /// <remarks>
    /// The rule a host relaying changes to several instances applies to decide which to send on to the staff who read the
    /// postmaster. Testing <see cref="TeamKey"/> for null alone misses the routed mail, which would stay in other
    /// instances' lists until they were read again.
    /// </remarks>
    public bool AffectsPostmaster => TeamKey == null || Change == TeamMailChange.Rerouted;
}
