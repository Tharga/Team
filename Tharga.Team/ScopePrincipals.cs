namespace Tharga.Team;

/// <summary>
/// Who an access level hands a scope to by default (Tharga/Team#412).
/// </summary>
/// <remarks>
/// <b>Only the level-derived part is filtered.</b> A scope that applies to members only is not granted to an API
/// key by its access level, but a key still holds it when a role names it or it is set as an override on that key.
/// That is the intent: the level is a default, and naming a scope on a key is a recorded decision.
/// </remarks>
public enum ScopePrincipals
{
    /// <summary>Members and API keys alike, as every scope was before this existed.</summary>
    All = 0,

    /// <summary>
    /// Members at the level hold the scope by default; an API key at the same level does not. For a scope an
    /// application's own key should not inherit just because the key is kept at that level so it can write back
    /// what it owns.
    /// </summary>
    Members = 1
}

/// <summary>What holds a set of scopes: a person who is a member of the team, or an API key.</summary>
public enum PrincipalKind
{
    /// <summary>A member of the team, or a person reaching it through consent.</summary>
    Member = 0,

    /// <summary>An API key.</summary>
    ApiKey = 1
}
