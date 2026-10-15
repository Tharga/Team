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
}
