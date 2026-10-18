namespace Tharga.Team;

public interface IUser
{
    public string Key { get; }
    public string Identity { get; }
    public string EMail { get; }

    /// <summary>
    /// Display name for the user. Defaults to null.
    /// Consumers should populate this from the 'name' claim in CreateUserEntityAsync.
    /// Used for default team names and member display names.
    /// </summary>
    public string Name => null;

    /// <summary>
    /// The user's id in the external directory (for Microsoft Entra ID: the 'oid' claim / Graph object id).
    /// Defaults to null. Captured automatically for new users and backfilled for existing users on their
    /// next resolve; used by <see cref="IUserDirectoryService"/> to verify and delete the directory user.
    /// </summary>
    public string DirectoryId => null;

    /// <summary>
    /// When the user last made an authenticated request. Defaults to null. Stamped by the user service
    /// at most once per configured interval (see LastSeenStampInterval), so the value is approximate
    /// within that interval. Distinct from the per-team-member <see cref="ITeamMember.LastSeen"/>, which tracks
    /// activity in that one team.
    /// </summary>
    public DateTime? LastSeen => null;

    /// <summary>
    /// Reference to the user's uploaded icon (from <see cref="IIconStore"/>), or null. When set, it takes
    /// precedence over Gravatar in avatar resolution. Opt-in: declare the property on the user entity to
    /// persist it (the same shape-based opt-in as <see cref="DirectoryId"/> / <see cref="LastSeen"/>).
    /// </summary>
    public string Icon => null;

    /// <summary>
    /// When this user was disabled, or null if they are enabled. A disabled user is evicted from their
    /// session within the claim-revalidation interval and cannot sign back in.
    /// </summary>
    /// <remarks>
    /// Opt-in by shape, like <see cref="DirectoryId"/> and <see cref="Icon"/>: declare the property on
    /// the user entity to persist it, and implement <c>IUserService.SetUserDisabledAsync</c>.
    /// <para>
    /// <b>Not the same as <see cref="DirectoryUserStatus.Disabled"/></b>, which means disabled in the
    /// external directory — blocked from the organization rather than from this application. The two are
    /// independent and are shown separately, for the same reason local and directory deletion are.
    /// </para>
    /// </remarks>
    public DateTime? DisabledAt => null;

    /// <summary>Who disabled this user, or null if they are enabled.</summary>
    public string DisabledBy => null;
}