namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>TeamDialog</c> — the create/rename form.</summary>
public static class TeamDialogText
{
    public static readonly TextKey Name = new("team.teamDialog.name", "Name");
    public static readonly TextKey NameRequired = new("team.teamDialog.nameRequired", "Name is required.");
    public static readonly TextKey Ok = new("team.teamDialog.ok", "OK");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Name, NameRequired, Ok];
}

/// <summary>
/// Localizable strings rendered by <c>SuspendedTeamNotice</c> — the banner explaining why a selected team
/// does nothing.
/// </summary>
/// <remarks>
/// The component also exposes <c>Title</c> and <c>Message</c> parameters, which predate the text provider.
/// An explicitly supplied parameter still wins; these are the defaults it falls back to, so a host that
/// translates through a provider no longer has to set the parameters as well.
/// </remarks>
public static class SuspendedTeamNoticeText
{
    public static readonly TextKey Title = new("team.suspendedNotice.title", "Your access to this team is suspended");

    public static readonly TextKey Message = new("team.suspendedNotice.message",
        "You are still a member and nothing has been deleted, but you cannot use this team until an " +
        "administrator restores your access. Select another team to carry on working.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Title, Message];
}

/// <summary>Localizable strings rendered by <c>AssignOwnerDialog</c> — owner repair for a team that lost one.</summary>
public static class AssignOwnerDialogText
{
    /// <summary>Placeholder: the team name.</summary>
    public static readonly TextKey Prompt = new("team.assignOwner.prompt", "{0} has no owner. Choose one of its members to take ownership.");

    public static readonly TextKey NoCandidates = new("team.assignOwner.noCandidates",
        "This team has no accepted members, so there is nobody to make owner. Invite someone and have them accept first, or delete the team.");

    public static readonly TextKey NewOwner = new("team.assignOwner.newOwner", "New owner");

    public static readonly TextKey Consequence = new("team.assignOwner.consequence",
        "The new owner gains full control of this team. The action is recorded in the audit log.");

    public static readonly TextKey Ok = new("team.assignOwner.ok", "OK");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Prompt, NoCandidates, NewOwner, Consequence, Ok];
}
