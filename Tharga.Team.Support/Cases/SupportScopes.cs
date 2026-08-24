namespace Tharga.Team.Support.Cases;

/// <summary>
/// Scope constants for support cases. Both are <b>team</b> scopes.
/// </summary>
/// <remarks>
/// <b>There is no scope for raising a case or reading your own.</b> Those are what an ordinary member does,
/// and are authorized by membership and authorship instead — a scope every host would have to grant to
/// everybody checks nothing.
/// </remarks>
public static class SupportScopes
{
    /// <summary>
    /// Read any case in the team, not only your own.
    /// </summary>
    /// <remarks>
    /// A real privilege boundary. A support case holds whatever a user typed into it, so it is a likely
    /// resting place for a pasted password, token or customer detail — the same reason reading the audit log
    /// is gated.
    /// </remarks>
    public const string Read = "support:read";

    /// <summary>Reply to and close any case in the team.</summary>
    public const string Manage = "support:manage";
}
