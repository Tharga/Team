namespace Tharga.Team;

/// <summary>Who may receive a notice: the active members of the occurrence's team a rule admits, beside anyone the raiser names.</summary>
/// <remarks>
/// <b>The rule is evaluated per team</b>, against each member's access level, tenant roles and effective team scopes
/// in the team the occurrence belongs to. Recipients the raiser names are added to whoever the rule admits, and every
/// recipient must be an active member of that team: accepted, not suspended, with a user record that is not disabled.
/// <para>
/// <b>A member with no access level</b> holds nothing since 4.0, so a scope, role or access-level rule never admits
/// them; <see cref="EveryMember"/> does, and so does naming them.
/// </para>
/// <para>
/// A notice whose recipients are named or resolved per occurrence is offered on the profile of every active member,
/// because any of them could be the one it reaches.
/// </para>
/// </remarks>
public sealed record UserNoticeAudience
{
    private UserNoticeAudience(UserNoticeAudienceKind kind, string scope = null, string role = null, AccessLevel? minimumAccessLevel = null,
        Func<UserNoticeOccurrence, IServiceProvider, CancellationToken, Task<IEnumerable<string>>> resolver = null)
    {
        Kind = kind;
        Scope = scope;
        Role = role;
        MinimumAccessLevel = minimumAccessLevel;
        Resolver = resolver;
    }

    /// <summary>Which rule this is.</summary>
    public UserNoticeAudienceKind Kind { get; }

    /// <summary>The team scope a member must hold, for <see cref="UserNoticeAudienceKind.Scope"/>.</summary>
    public string Scope { get; }

    /// <summary>The tenant role a member must hold, for <see cref="UserNoticeAudienceKind.Role"/>.</summary>
    public string Role { get; }

    /// <summary>The least privileged access level admitted, for <see cref="UserNoticeAudienceKind.AccessLevel"/>.</summary>
    public AccessLevel? MinimumAccessLevel { get; }

    /// <summary>The callback naming an occurrence's recipients by user key, for <see cref="UserNoticeAudienceKind.Resolved"/>; host-side only and never serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Func<UserNoticeOccurrence, IServiceProvider, CancellationToken, Task<IEnumerable<string>>> Resolver { get; }

    /// <summary>Members holding <paramref name="scope"/> in the team, through their access level, roles or scope overrides.</summary>
    /// <exception cref="ArgumentException">The scope is empty.</exception>
    public static UserNoticeAudience WithScope(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return new UserNoticeAudience(UserNoticeAudienceKind.Scope, scope: scope.Trim());
    }

    /// <summary>Members holding the tenant role <paramref name="role"/> in the team.</summary>
    /// <exception cref="ArgumentException">The role is empty.</exception>
    public static UserNoticeAudience WithRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return new UserNoticeAudience(UserNoticeAudienceKind.Role, role: role.Trim());
    }

    /// <summary>Members at <paramref name="minimum"/> or a more privileged level in the team; <see cref="AccessLevel.Custom"/> is admitted only by itself.</summary>
    public static UserNoticeAudience AtLeast(AccessLevel minimum) => new(UserNoticeAudienceKind.AccessLevel, minimumAccessLevel: minimum);

    /// <summary>Every active member of the team.</summary>
    public static UserNoticeAudience EveryMember { get; } = new(UserNoticeAudienceKind.EveryMember);

    /// <summary>Only the recipients the raiser names on each occurrence.</summary>
    public static UserNoticeAudience NamedOnly { get; } = new(UserNoticeAudienceKind.NamedOnly);

    /// <summary>The user keys <paramref name="resolver"/> returns for each occurrence, from a scope of the host's services.</summary>
    /// <exception cref="ArgumentNullException">The resolver is null.</exception>
    public static UserNoticeAudience ResolvedBy(Func<UserNoticeOccurrence, IServiceProvider, CancellationToken, Task<IEnumerable<string>>> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return new UserNoticeAudience(UserNoticeAudienceKind.Resolved, resolver: resolver);
    }

    /// <summary>Whether the rule chooses recipients from the team's members by what each holds, rather than by name or callback.</summary>
    public bool SelectsByMembership => Kind is UserNoticeAudienceKind.Scope or UserNoticeAudienceKind.Role or UserNoticeAudienceKind.AccessLevel or UserNoticeAudienceKind.EveryMember;

    /// <summary>Whether a member holding this level, these roles and these effective scopes may receive the notice in their team; a null level is admitted only by every member or a named or resolved rule.</summary>
    public bool MayReceive(AccessLevel? accessLevel, IEnumerable<string> roles, IEnumerable<string> scopes) => Kind switch
    {
        UserNoticeAudienceKind.Scope => accessLevel != null && (scopes ?? []).Contains(Scope, StringComparer.Ordinal),
        UserNoticeAudienceKind.Role => accessLevel != null && (roles ?? []).Contains(Role, StringComparer.Ordinal),
        UserNoticeAudienceKind.AccessLevel => MinimumAccessLevel is { } minimum && AccessLevelPrivilege.IsNoMorePrivilegedThan(minimum, accessLevel),
        _ => true
    };
}

/// <summary>The kinds of rule a <see cref="UserNoticeAudience"/> can be.</summary>
public enum UserNoticeAudienceKind
{
    /// <summary>Members holding a team scope.</summary>
    Scope,

    /// <summary>Members holding a tenant role.</summary>
    Role,

    /// <summary>Members at an access level or above.</summary>
    AccessLevel,

    /// <summary>Every active member.</summary>
    EveryMember,

    /// <summary>Only those the raiser names.</summary>
    NamedOnly,

    /// <summary>Those a host callback names per occurrence.</summary>
    Resolved
}
