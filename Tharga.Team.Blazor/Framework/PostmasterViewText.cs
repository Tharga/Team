namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>PostmasterView</c> and <c>PostmasterMail</c> — mail that reached the shared
/// mailbox at an address no team owns.
/// </summary>
public static class PostmasterViewText
{
    public static readonly TextKey Title = new("team.mailbox.postmaster.title", "Postmaster");

    /// <summary>Follows the count of mail the postmaster holds.</summary>
    public static readonly TextKey Held = new("team.mailbox.postmaster.held", "unrouted");

    public static readonly TextKey None = new("team.mailbox.postmaster.none", "No mail is waiting for a team.");

    public static readonly TextKey NotPermitted = new("team.mailbox.postmaster.notPermitted", "You do not have access to mail that belongs to no team.");

    /// <summary>Said before the mail is opened: opening it is recorded, because it may hold any team's material.</summary>
    public static readonly TextKey OpeningIsRecorded = new("team.mailbox.postmaster.openingIsRecorded", "Opening a mail is recorded, because it may hold another team's material.");

    public static readonly TextKey From = new("team.mailbox.postmaster.from", "From");

    public static readonly TextKey To = new("team.mailbox.postmaster.to", "To");

    public static readonly TextKey Cc = new("team.mailbox.postmaster.cc", "Cc");

    public static readonly TextKey DeliveredTo = new("team.mailbox.postmaster.deliveredTo", "Delivered to");

    public static readonly TextKey Received = new("team.mailbox.postmaster.received", "Received");

    public static readonly TextKey NoSubject = new("team.mailbox.postmaster.noSubject", "(no subject)");

    public static readonly TextKey ReasonRetiredKey = new("team.mailbox.postmaster.reason.retiredKey", "Sent to a retired address");

    public static readonly TextKey ReasonUnknownAddress = new("team.mailbox.postmaster.reason.unknownAddress", "Sent to an address nobody owns");

    public static readonly TextKey RouteTo = new("team.mailbox.postmaster.routeTo", "Route to team");

    public static readonly TextKey Route = new("team.mailbox.postmaster.route", "Route");

    public static readonly TextKey Routed = new("team.mailbox.postmaster.routed", "The mail was delivered to the team.");

    /// <summary>Someone else routed it, or the retention sweep removed it, between listing and acting on it.</summary>
    public static readonly TextKey AlreadyGone = new("team.mailbox.postmaster.alreadyGone", "The postmaster no longer holds this mail.");

    public static readonly TextKey LoadMore = new("team.mailbox.postmaster.loadMore", "Show more");

    /// <summary>Names the switch between held and archived mail, for a screen reader.</summary>
    public static readonly TextKey StateLabel = new("team.mailbox.postmaster.state.label", "Which mail");

    /// <summary>The switch's choice for mail waiting to be routed or archived.</summary>
    public static readonly TextKey StateHeld = new("team.mailbox.postmaster.state.held", "Held");

    /// <summary>The switch's choice for mail put away without routing.</summary>
    public static readonly TextKey StateArchived = new("team.mailbox.postmaster.state.archived", "Archived");

    public static readonly TextKey NoneArchived = new("team.mailbox.postmaster.noneArchived", "No mail has been archived.");

    /// <summary>On an archived mail's card, followed by when; {0} is who archived it.</summary>
    public static readonly TextKey ArchivedBy = new("team.mailbox.postmaster.archived.by", "Archived by {0}");

    /// <summary>On an archived mail's card when who archived it is not known, followed by when.</summary>
    public static readonly TextKey ArchivedWhen = new("team.mailbox.postmaster.archived.when", "Archived");

    public static readonly TextKey Archive = new("team.mailbox.postmaster.archive", "Archive");

    public static readonly TextKey ConfirmArchiveTitle = new("team.mailbox.postmaster.confirmArchive.title", "Archive this mail?");

    public static readonly TextKey ConfirmArchiveBody = new("team.mailbox.postmaster.confirmArchive.body", "The mail goes to no team. It moves to the archived mail, where it can be restored until it is deleted with the rest.");

    public static readonly TextKey ConfirmArchiveAccept = new("team.mailbox.postmaster.confirmArchive.accept", "Archive");

    public static readonly TextKey ConfirmArchiveCancel = new("team.mailbox.postmaster.confirmArchive.cancel", "Cancel");

    public static readonly TextKey Archived = new("team.mailbox.postmaster.archived", "The mail was archived.");

    public static readonly TextKey Restore = new("team.mailbox.postmaster.restore", "Restore");

    public static readonly TextKey Restored = new("team.mailbox.postmaster.restored", "The mail is back among the held mail.");

    public static readonly TextKey NoText = new("team.mailbox.postmaster.noText", "The mail has no text.");

    /// <summary>The mail is listed, but the host's content store no longer has what arrived.</summary>
    public static readonly TextKey ContentUnavailable = new("team.mailbox.postmaster.contentUnavailable", "What the mail said is no longer kept.");

    /// <summary>The reader may read the postmaster's mail but holds no grant to route, archive or restore it.</summary>
    public static readonly TextKey ManageNotPermitted = new("team.mailbox.postmaster.manageNotPermitted", "You may read this mail, but not route, archive or restore it.");

    /// <summary>A typed team key names no team, so the mail stays where it is.</summary>
    public static readonly TextKey NoSuchTeam = new("team.mailbox.postmaster.noSuchTeam", "No team has that key.");

    public static readonly TextKey[] All =
    [
        Title, Held, None, NotPermitted, OpeningIsRecorded, From, To, Cc, DeliveredTo, Received, NoSubject,
        ReasonRetiredKey, ReasonUnknownAddress, RouteTo, Route, Routed, AlreadyGone, LoadMore,
        StateLabel, StateHeld, StateArchived, NoneArchived, ArchivedBy, ArchivedWhen, Archive, ConfirmArchiveTitle,
        ConfirmArchiveBody, ConfirmArchiveAccept, ConfirmArchiveCancel, Archived, Restore, Restored, NoText,
        ContentUnavailable, ManageNotPermitted, NoSuchTeam
    ];
}
