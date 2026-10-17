namespace Tharga.Team;

/// <summary>
/// A page of a team's mail, with an explicit cursor for the next one.
/// </summary>
/// <remarks>
/// Concrete and array-typed so it serializes by construction. <see cref="NextCursor"/> is <c>null</c> on the last page.
/// </remarks>
public record InboundMailPage
{
    public required InboundMailItem[] Items { get; init; }

    public string NextCursor { get; init; }
}
