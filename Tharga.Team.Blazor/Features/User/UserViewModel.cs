namespace Tharga.Team.Blazor.Features.User;

/// <summary>
/// View model for a user row in the users list view.
/// </summary>
public record UserViewModel
{
    public string Key { get; init; }

    /// <summary>The name to display — the stored name, or one resolved from the email when none is set.</summary>
    public string Name { get; init; }

    /// <summary>
    /// The stored <see cref="IUser.Name"/> exactly as persisted, null when the user has never set one.
    /// Editing must bind to this rather than <see cref="Name"/>, so that a resolved fallback is not
    /// silently promoted into a real stored name.
    /// </summary>
    public string StoredName { get; init; }

    public string EMail { get; init; }

    /// <summary>
    /// The identity the user authenticates by (<see cref="IUser.Identity"/>) — the subject claim from the
    /// identity provider. Stable across name and email changes, which is what makes it the value worth
    /// quoting in a support ticket.
    /// </summary>
    public string Identity { get; init; }

    /// <summary>
    /// The user's id in the external directory (for Microsoft Entra ID: the <c>oid</c> claim / Graph
    /// object id), or null. Null has two distinct meanings the UI must not conflate: the host's user
    /// entity does not declare <see cref="IUser.DirectoryId"/> (nothing is ever stored), or it does and
    /// this user has not been resolved against the directory yet.
    /// </summary>
    public string DirectoryId { get; init; }

    public string Icon { get; init; }
    public int TeamCount { get; init; }
    public UserTeamInfo[] Teams { get; init; }
    public DateTime? LastSeen { get; init; }

    /// <summary>Set when the row has been verified against the external directory; mutable so a verify updates the badge in place.</summary>
    public DirectoryUserStatus? DirectoryStatus { get; set; }

    /// <summary>
    /// When this user was disabled <i>in this application</i>, or null. Distinct from
    /// <see cref="DirectoryStatus"/> being <see cref="DirectoryUserStatus.Disabled"/>, which means
    /// disabled in the external directory — blocked from the organization rather than from here. The two
    /// are independent, can disagree, and are rendered as separate badges for exactly that reason.
    /// </summary>
    public DateTime? DisabledAt { get; init; }

    /// <summary>Who disabled this user, or null.</summary>
    public string DisabledBy { get; init; }

    public bool IsDisabled => DisabledAt.HasValue;

    /// <summary>
    /// How the user signs in, once read from the directory; null until then. Mutable because the list
    /// reads it for the visible page after rendering, not with the rows.
    /// </summary>
    public Tharga.Team.Blazor.Framework.SignInMethod[] SignInMethods { get; set; }

    /// <summary>Why the sign-in methods could not be read, or null.</summary>
    public string SignInError { get; set; }
}
