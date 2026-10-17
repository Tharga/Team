namespace Tharga.Team;

/// <summary>
/// Claim type constants for team-based authorization.
/// </summary>
public static class TeamClaimTypes
{
    /// <summary>Claim type for the team key.</summary>
    /// <remarks>
    /// This is the <b>access anchor</b>: it is emitted only once access to the team has resolved, so its
    /// presence means "the caller may act on this team". It is not a record of what the caller picked —
    /// that is <see cref="SelectedTeamKey"/>. The two differ whenever a team is selected but grants the
    /// caller nothing.
    /// </remarks>
    public const string TeamKey = "TeamKey";

    /// <summary>
    /// Claim type marking which team the caller currently has <b>selected</b>, independent of whether that
    /// selection grants any access.
    /// </summary>
    /// <remarks>
    /// Emitted from the selected-team cookie before access is resolved, so it is present even when
    /// <see cref="TeamKey"/> is not. Consult it to tell "no team is selected" apart from "a team is
    /// selected that the caller cannot reach" — keying only off <see cref="TeamKey"/> conflates the two.
    /// </remarks>
    public const string SelectedTeamKey = "team_id";

    /// <summary>Claim type for the access level.</summary>
    public const string AccessLevel = "AccessLevel";

    /// <summary>Claim type carrying the caller's team-member key (<c>ITeamMember.Key</c>) for the selected team. Used to scope owner-private API keys.</summary>
    public const string MemberKey = "MemberKey";

    /// <summary>
    /// Claim type carrying the user key of the person a private API key belongs to, on a principal
    /// authenticated by that key. Use it to attribute what the key does to a person.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not <see cref="MemberKey"/>.</b> That claim is issued only from a membership and is
    /// read as "this caller is a member, at its own level" (see <c>TeamScopePolicy</c>). A key acts
    /// <i>for</i> its owner, at no more than the owner's access, but it is not the owner's session.
    /// </remarks>
    public const string ApiKeyOwner = "ApiKeyOwner";

    /// <summary>
    /// Claim type for scopes granted by the caller's access level, roles or overrides <b>on the selected
    /// team</b>. Multiple scope claims may be present.
    /// </summary>
    /// <remarks>
    /// A scope here authorizes the selected team only. System-wide grants use <see cref="SystemScope"/> —
    /// the two are separate claim types so that a grant's origin survives into the claim. Emitting both as
    /// one type meant a system role silently satisfied a check that asked for the scope on a specific team.
    /// </remarks>
    public const string Scope = "Scope";

    /// <summary>
    /// Claim type for scopes granted system-wide — from an app role via <c>ConfigureSystemRoles</c>, or from
    /// a system API key. Not bound to any team. Multiple claims may be present.
    /// </summary>
    public const string SystemScope = "SystemScope";

    /// <summary>Claim type that marks a principal as authenticated via a system API key (not bound to a team). Value: "true".</summary>
    public const string IsSystemKey = "IsSystemKey";

    /// <summary>Claim type carrying the stable identifier of the API key used to authenticate (the <c>IApiKey.Key</c> Guid string).</summary>
    public const string ApiKeyId = "ApiKeyId";

    /// <summary>Prefix for per-tag claims emitted from an API key's <c>Tags</c>. A tag with key <c>K</c> becomes a claim of type <c>tag.K</c>. Multiple claims with the same type may be present.</summary>
    public const string TagPrefix = "tag.";
}
