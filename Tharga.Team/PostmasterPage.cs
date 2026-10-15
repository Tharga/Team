namespace Tharga.Team;

/// <summary>
/// A page of unrouted mail, with an explicit cursor for the next one; <see cref="NextCursor"/> is <c>null</c> on the last page.
/// </summary>
public record PostmasterPage
{
    public required PostmasterMailSummary[] Items { get; init; }

    public string NextCursor { get; init; }
}
