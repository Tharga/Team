namespace Tharga.Team;

/// <summary>
/// System-level scope constants for API keys. Like <see cref="SystemUserScopes"/>, these are granted to
/// privileged roles (e.g. through <c>o.ConfigureSystemRoles</c>) rather than held through a team.
/// </summary>
public static class SystemApiKeyScopes
{
    /// <summary>
    /// Authorizes seeing and managing other members' private (owner-scoped) API keys, in any team: the
    /// oversight a developer or system administrator needs to investigate an incident or retire a departed
    /// person's keys. Map it to the roles that should have it.
    /// </summary>
    /// <remarks>
    /// Replaces the hard-coded <c>Developer</c> role, which granted the same oversight without the host
    /// being able to grant it to anyone else (Tharga/Team#323). Since 4.0 the role grants nothing by itself:
    /// map this scope to <c>Developer</c> if that role should keep it.
    /// </remarks>
    public const string Oversee = "apikeys:oversee";
}
