namespace Tharga.Team;

/// <summary>
/// Represents an API key with associated metadata.
/// </summary>
public interface IApiKey
{
    /// <summary>Unique identifier for this API key entry.</summary>
    string Key { get; }

    /// <summary>Human-readable name for this API key.</summary>
    string Name { get; }

    /// <summary>The raw API key value (only populated on creation; otherwise empty).</summary>
    string ApiKey { get; }

    /// <summary>Team that owns this API key. Null for system keys (not bound to a team).</summary>
    string TeamKey { get; }

    /// <summary>Explicit scopes granted to a system key at creation time. Null/empty for team keys.</summary>
    string[] SystemScopes { get; }

    /// <summary>User who created this key (identity/display name). Null if not recorded.</summary>
    string CreatedBy { get; }

    /// <summary>
    /// Owner of an owner-scoped ("private", shown as "Personal") key, or null for a shared key. On a team key
    /// this is the owning team member (<see cref="ITeamMember.Key"/>); on a system key it is the owning user's
    /// key — the same value, since a member key is the user key. Private keys are hidden from others in the
    /// UI and can only be recycled/locked/disabled/deleted by their owner (a caller holding the
    /// <see cref="SystemApiKeyScopes.Oversee"/> system scope may still manage them, for audit).
    /// </summary>
    string OwnerMemberKey { get; }

    /// <summary>
    /// System-set key-value tags on this API key. A list (not a map), so a key may repeat.
    /// Set only at creation via the service; immutable thereafter and not editable from the UI.
    /// Each tag is surfaced as a <c>tag.{Key}</c> claim on the authenticated principal.
    /// </summary>
    IReadOnlyList<Tag> Tags { get; }

    /// <summary>Access level assigned to this API key. Null defaults to Administrator.</summary>
    AccessLevel? AccessLevel { get; }

    /// <summary>Roles assigned to this API key.</summary>
    string[] Roles { get; }

    /// <summary>Scope overrides for this API key.</summary>
    string[] ScopeOverrides { get; }

    /// <summary>Expiry date, or null if no expiry.</summary>
    DateTime? ExpiryDate { get; }

    /// <summary>When the key was created. Reset when the key is recycled (refreshed).</summary>
    DateTime? CreatedAt { get; }

    /// <summary>When the key was last used to authenticate, or null if never used. Reset when the key is recycled (refreshed).</summary>
    DateTime? LastUsedAt { get; }

    /// <summary>
    /// When this key was disabled, or null if it is enabled. A disabled key is refused at authentication.
    /// </summary>
    /// <remarks>
    /// A timestamp rather than a flag, because <i>when</i> and — with <see cref="DisabledBy"/> — <i>who</i>
    /// are what an operator needs after a security action, and a bool answers neither.
    /// <para>
    /// <b>Distinct from expiry and from locking.</b> An expired key stopped working on a date nobody
    /// chose and is fixed by a new expiry; a disabled key was stopped by a person and is fixed by that
    /// person or another. A <i>locked</i> key is neither — locking only discards the stored secret so the
    /// raw value cannot be retrieved again, and a locked key still authenticates.
    /// </para>
    /// <para>
    /// <b>Refreshing does not clear this.</b> A key disabled because it might have leaked stays disabled
    /// until someone explicitly enables it, or the remedy for a compromise silently undoes the
    /// containment.
    /// </para>
    /// </remarks>
    DateTime? DisabledAt { get; }

    /// <summary>Who disabled this key, or null if it is enabled.</summary>
    string DisabledBy { get; }
}
