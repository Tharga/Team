namespace Tharga.Team;

/// <summary>
/// A page of support cases, with an explicit cursor for the next one.
/// </summary>
/// <remarks>
/// Concrete rather than a generic page type, and an array rather than an interface: a contract has to
/// serialize by construction, and the cheapest way to guarantee that is to leave nothing about the shape to
/// inference. <see cref="NextCursor"/> is <c>null</c> when there is no further page.
/// </remarks>
public record SupportCasePage
{
    public required SupportCase[] Items { get; init; }

    public string NextCursor { get; init; }
}
