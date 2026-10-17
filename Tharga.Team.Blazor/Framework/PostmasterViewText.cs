namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>PostmasterView</c> — mail that reached the shared mailbox at an address no team owns.
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

    /// <summary>Someone else routed it, or the retention sweep removed it, between listing and routing.</summary>
    public static readonly TextKey AlreadyGone = new("team.mailbox.postmaster.alreadyGone", "The postmaster no longer holds this mail.");

    public static readonly TextKey LoadMore = new("team.mailbox.postmaster.loadMore", "Show more");

    public static readonly TextKey[] All =
    [
        Title, Held, None, NotPermitted, OpeningIsRecorded, From, To, Cc, DeliveredTo, Received, NoSubject,
        ReasonRetiredKey, ReasonUnknownAddress, RouteTo, Route, Routed, AlreadyGone, LoadMore
    ];
}
