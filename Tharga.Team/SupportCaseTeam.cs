namespace Tharga.Team;

/// <summary>
/// A team named on a support case — its key, and its name as it was when it was recorded.
/// </summary>
/// <remarks>
/// <b>The name is a snapshot for showing, the key is the reference.</b> A team may be renamed after the case
/// recorded it, so anything acting on the team goes by <see cref="Key"/> and reads the current name if it
/// needs one.
/// </remarks>
public record SupportCaseTeam
{
    public required string Key { get; init; }

    /// <summary>The team's name when it was recorded; <c>null</c> when it had none.</summary>
    public string Name { get; init; }
}
