namespace Tharga.Team;

public interface ITeam
{
    string Key { get; }
    string Name { get; }
    string Icon { get; }

    /// <summary>
    /// Global roles that have been granted access to this team via consent.
    /// Null or empty means no consent granted.
    /// </summary>
    string[] ConsentedRoles => null;

    /// <summary>
    /// Access level granted to consented roles. Null falls back to the configured default consent level.
    /// </summary>
    AccessLevel? ConsentAccessLevel => null;

    /// <summary>
    /// Custom roles defined at runtime for this team (created / updated / deleted without a code deploy).
    /// Null or empty means only code-registered roles apply. Each role's scopes are constrained to
    /// app-registered scopes.
    /// </summary>
    IReadOnlyList<TenantRoleDefinition> CustomRoles => null;

    /// <summary>
    /// When the team was soft-deleted, or <c>null</c> while it is live.
    /// </summary>
    /// <remarks>
    /// A default interface member, so a host implementing <see cref="ITeam"/> directly keeps compiling —
    /// the same reason <see cref="ConsentedRoles"/> is one.
    /// <para>
    /// A soft-deleted team is excluded from every read and grants no access; it exists so a deletion can be
    /// undone, and so its storage can be dropped later as a separate, more privileged act.
    /// </para>
    /// </remarks>
    DateTime? DeletedAt => null;

    /// <summary>Identity of whoever soft-deleted the team, or <c>null</c> while it is live.</summary>
    string DeletedBy => null;

    /// <summary>
    /// Whether the team is soft-deleted.
    /// </summary>
    /// <remarks>
    /// <b>Derived from <see cref="DeletedAt"/> rather than stored beside it.</b> Two fields that must agree
    /// eventually disagree — a restore that clears one and not the other leaves a team that is deleted by
    /// one reading and live by the other, and every read path then depends on which it happened to consult.
    /// One value, one answer.
    /// </remarks>
    bool IsDeleted => DeletedAt != null;
}

public interface ITeam<TMember> : ITeam
    where TMember : ITeamMember
{
    public TMember[] Members { get; init; }
}
