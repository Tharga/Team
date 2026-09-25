namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>TeamInviteView</c> — the invitation a recipient answers.</summary>
/// <remarks>
/// The tenant noun appears here, which is the whole reason Tharga/Team#204 was filed: a host calling the
/// tenant an Organisation had this component say "team" beside it.
/// </remarks>
public static class TeamInviteViewText
{
    /// <summary>The invitation itself. <c>{0}</c> is the team name.</summary>
    /// <remarks>
    /// One key for the whole sentence, with the name as a positional placeholder. The name sits
    /// mid-sentence in English and moves in other languages, so a template a translator can reorder is the
    /// only shape that works.
    /// </remarks>
    public static readonly TextKey Invitation = new("team.invite.invitation",
        "You have been invited to team '{0}'. Do you want to join?");

    public static readonly TextKey Accept = new("team.invite.accept", "Yes");

    public static readonly TextKey Decline = new("team.invite.decline", "No");

    /// <summary>Shown on a standalone invites page when there is nothing to answer.</summary>
    public static readonly TextKey NoInvitations = new("team.invite.noInvitations",
        "You have no pending invitations. When you open an invitation link, the request to join will appear here.");

    /// <summary>A code was presented and resolved to nothing — unknown, superseded, withdrawn or answered.</summary>
    /// <remarks>
    /// Deliberately does not say which: the service makes those indistinguishable because the answer would be
    /// given to someone who guessed.
    /// </remarks>
    public static readonly TextKey InvalidLink = new("team.invite.invalidLink",
        "This invitation link is no longer valid. Ask whoever invited you for a new one.");

    /// <summary>The code resolved, but the caller is already an accepted member. <c>{0}</c> is the team name.</summary>
    public static readonly TextKey AlreadyMember = new("team.invite.alreadyMember",
        "You are already a member of team '{0}'.");

    /// <summary>The code resolved to a real invitation whose time has passed. <c>{0}</c> is the team name.</summary>
    /// <remarks>
    /// Says "extend" rather than "send a new one": extending keeps the code, so the link already in the
    /// recipient's inbox starts working again.
    /// </remarks>
    public static readonly TextKey Expired = new("team.invite.expired",
        "The invitation to team '{0}' has expired. Ask whoever invited you to extend it.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Invitation, Accept, Decline, NoInvitations, InvalidLink, AlreadyMember, Expired];
}
