namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>LinkIdentityDialog</c> — an administrator moving the sign-in identity of a duplicate user onto the user who keeps the teams.</summary>
public static class LinkIdentityDialogText
{
    /// <summary>Opens the dialog. <c>{0}</c> is the user who keeps their teams.</summary>
    public static readonly TextKey Help = new("team.linkIdentity.help",
        "{0} keeps their teams, their API keys and their history. Choose the user the person signs in as now. That user is removed, and its sign-in identity moves to {0}.");

    public static readonly TextKey Candidate = new("team.linkIdentity.candidate", "Duplicate to remove");

    /// <summary>One duplicate in the list. <c>{0}</c> is its name or address and <c>{1}</c> when it was last seen.</summary>
    public static readonly TextKey CandidateLine = new("team.linkIdentity.candidateLine", "{0}, last seen {1}");

    public static readonly TextKey NeverSeen = new("team.linkIdentity.neverSeen", "never");

    /// <summary>Shown when the duplicates all belong to a team, so none can be removed.</summary>
    public static readonly TextKey AllHaveTeams = new("team.linkIdentity.allHaveTeams",
        "Every user with this address belongs to a team. Remove their memberships first, so that nothing is lost.");

    public static readonly TextKey Required = new("team.linkIdentity.required", "Choose the duplicate to remove.");

    public static readonly TextKey Link = new("team.linkIdentity.link", "Link and remove duplicate");

    public static readonly TextKey Cancel = new("team.linkIdentity.cancel", "Cancel");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Help, Candidate, CandidateLine, NeverSeen, AllHaveTeams, Required, Link, Cancel];
}
