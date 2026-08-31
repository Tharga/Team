namespace Tharga.Team;

/// <summary>
/// Something happened to a support case.
/// </summary>
/// <remarks>
/// <b>Carries what a caller needs to decide whether to react, and nothing more.</b> Enough to update a
/// counter or re-read one case; deliberately not the message body. A handler is host code, and a support
/// message is free-form text somebody typed - it is where a password gets pasted. Anything wanting the
/// content reads the case through the service, where authorization applies.
/// </remarks>
public sealed class SupportCaseUpdatedEventArgs : EventArgs
{
    public required string TeamKey { get; init; }

    public required string CaseId { get; init; }

    public required SupportCaseChange Change { get; init; }

    /// <summary>
    /// True when this came from an external channel rather than from the application.
    /// </summary>
    /// <remarks>
    /// The distinction a UI usually wants: a reply the user just typed needs no notification, while one that
    /// arrived from Slack is the thing worth lighting a chip for.
    /// </remarks>
    public required bool FromChannel { get; init; }
}
