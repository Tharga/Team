namespace Tharga.Team;

/// <summary>
/// A user account holding a support mail's sender address, and the teams it is an active member of.
/// </summary>
/// <remarks>
/// <b>An account, not a person.</b> Two accounts can hold one address — the same person signing in two ways,
/// or somebody who claimed an address that is not theirs — which is why <see cref="ISupportSenderDirectory"/>
/// returns every account it finds rather than the first.
/// </remarks>
public record SupportSender
{
    /// <summary>The user's stable identity — what a case opened by them records as its author.</summary>
    public required string Identity { get; init; }

    /// <summary>The user's display name, or <c>null</c> when the record has none.</summary>
    public string Name { get; init; }

    /// <summary>
    /// The teams in which this account has accepted its membership and is not suspended. Empty for a
    /// disabled account.
    /// </summary>
    public SupportCaseTeam[] Teams { get; init; } = [];
}
