namespace Tharga.Team.Service.Audit;

/// <summary>
/// Metadata keys the toolkit writes onto <see cref="AuditEntry.Metadata"/>.
/// </summary>
/// <remarks>
/// Named rather than inlined so the vocabulary stays consistent across decorators and so a consumer can
/// filter or display on a stable key. Values are dotted and lower-case; a change here is a change to the
/// audit record's shape, so treat these as part of the public contract.
/// <para>
/// A <c>.old</c> / <c>.new</c> pair is written only where the previous value is needed to interpret the
/// entry (rename, consent level, member access level, member display name). Operations whose identity is
/// the whole story — invite, remove — record the subject only, so they cost no extra read.
/// </para>
/// </remarks>
public static class AuditMetadataKeys
{
    /// <summary>Team display name, on create.</summary>
    public const string TeamName = "team.name";

    /// <summary>Team name before a rename.</summary>
    public const string TeamNameOld = "team.name.old";

    /// <summary>Team name after a rename.</summary>
    public const string TeamNameNew = "team.name.new";

    /// <summary>Member the operation acted on.</summary>
    public const string MemberKey = "member.key";

    /// <summary>Email a member was invited with.</summary>
    public const string MemberEmail = "member.email";

    /// <summary>Member access level before a change.</summary>
    public const string MemberAccessLevelOld = "member.accesslevel.old";

    /// <summary>Member access level after a change.</summary>
    public const string MemberAccessLevelNew = "member.accesslevel.new";

    /// <summary>Per-team member display name before a change. Empty string means "no override".</summary>
    public const string MemberNameOld = "member.name.old";

    /// <summary>Per-team member display name after a change. Empty string means the override was cleared.</summary>
    public const string MemberNameNew = "member.name.new";

    /// <summary>The display name a user was renamed to, on an administrative rename.</summary>
    public const string UserNameNew = "user.name.new";

    /// <summary>Tenant roles assigned to a member, comma-separated.</summary>
    public const string MemberTenantRoles = "member.tenantroles";

    /// <summary>Per-member scope overrides, comma-separated.</summary>
    public const string MemberScopeOverrides = "member.scopeoverrides";

    /// <summary>Consented access level before a change. Absent value means "no consent".</summary>
    public const string ConsentAccessLevelOld = "consent.accesslevel.old";

    /// <summary>Consented access level after a change. Absent value means consent was cleared.</summary>
    public const string ConsentAccessLevelNew = "consent.accesslevel.new";

    /// <summary>Roles the team consented to, comma-separated.</summary>
    public const string ConsentRoles = "consent.roles";

    /// <summary>Custom (runtime-defined) tenant role names, comma-separated.</summary>
    public const string CustomRoleNames = "customroles.names";

    /// <summary>New owner on an ownership transfer.</summary>
    public const string NewOwnerKey = "team.newowner.key";

    /// <summary>
    /// Owners demoted to Administrator by a set-owner, comma-separated. Empty is never recorded — an
    /// operation that changed nothing writes no entry at all.
    /// </summary>
    /// <remarks>
    /// Plural on purpose. A team synced from a system permitting several owners is reduced to one in a
    /// single operation, so "who lost ownership" is a list rather than a value, and recording only the
    /// first would understate what happened.
    /// </remarks>
    public const string DemotedOwnerKeys = "team.demotedowners.keys";

    /// <summary>Number of teams a user was removed from, on a remove-from-all-teams operation.</summary>
    public const string MemberTeamCount = "member.teamcount";

    /// <summary>Icon content type, on a team-icon set.</summary>
    public const string IconContentType = "icon.contenttype";

    /// <summary>Icon size in bytes, on a team-icon set.</summary>
    public const string IconSize = "icon.size";

    /// <summary>User the operation acted on (cross-team user administration).</summary>
    public const string UserKey = "user.key";

    /// <summary>Directory verification outcome (Found / NotFound / Disabled / NotLinked).</summary>
    public const string DirectoryStatus = "directory.status";

    /// <summary>Whether a user delete also deleted the directory user.</summary>
    public const string DirectoryDeleted = "directory.deleted";

    /// <summary>Why a requested directory operation did not complete.</summary>
    public const string DirectoryError = "directory.error";

    /// <summary>Number of users processed by a bulk verification.</summary>
    public const string VerifiedCount = "verify.count";
}
