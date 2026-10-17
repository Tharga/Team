namespace Tharga.Team;

/// <summary>
/// A team's mail keys: at most one primary and one secondary.
/// </summary>
/// <remarks>
/// <see cref="Primary"/> is <c>null</c> until a key has been issued, which is a state a host shows rather than
/// an error: a team that has not switched its mailbox on has no address.
/// </remarks>
public record TeamMailAddresses
{
    public required string TeamKey { get; init; }

    public TeamMailKey Primary { get; init; }

    public TeamMailKey Secondary { get; init; }
}
