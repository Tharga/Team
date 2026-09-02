namespace Tharga.Team.Support.Notifications;

/// <summary>
/// Which events reach Slack, and where.
/// </summary>
public class NotificationOptions
{
    /// <summary>
    /// Channel used by any route that does not name one. Until this is set, the built-in routes send
    /// nothing.
    /// </summary>
    public string DefaultChannel { get; set; }

    /// <summary>
    /// Where a support case lives on this application's own site, so a notification can link to it. Use
    /// <c>{caseId}</c> for the case — for example
    /// <c>https://app.example.com/support/{caseId}</c>. Unset renders <c>{case.url}</c> as nothing.
    /// </summary>
    /// <remarks>
    /// <b>A template rather than a base address, because the route is the host's to know.</b> The toolkit has
    /// no convention for where a case is shown — <c>/support</c> is what the sample happens to choose — so a
    /// base address would produce a working link only for hosts whose routing matched a guess. A template
    /// puts the knowledge where it exists.
    /// <para>
    /// <b>The public address is also the host's to know.</b> A reverse proxy, a custom domain and a
    /// container's own idea of its hostname all disagree, and a library that inferred one would produce links
    /// that work in development and break in production.
    /// </para>
    /// <para>
    /// <b>An unset template renders empty rather than broken.</b> A message whose wording includes a link is
    /// still readable without one; a message containing <c>http://localhost/support/</c> in front of a
    /// customer is not.
    /// </para>
    /// </remarks>
    public string CaseUrlTemplate { get; set; }

    /// <summary>
    /// The routing table. Replace it to take full control, or edit it to add and remove single events.
    /// </summary>
    /// <remarks>
    /// Starts as <see cref="DefaultRoutes"/> so a host that names a channel gets useful traffic without
    /// writing a table first. Clearing it turns notifications off without unregistering anything.
    /// </remarks>
    public IList<NotificationRoute> Routes { get; set; } = DefaultRoutes();

    /// <summary>
    /// The events worth telling someone about on a fresh install: a team appearing, someone joining or
    /// leaving it, and a user being deleted.
    /// </summary>
    /// <remarks>
    /// <b>The issue also named "user logs on" and "user created". Neither is in this list, because
    /// neither exists as an audited event today</b> — the toolkit audits API-key authentication
    /// (<c>auth:*</c>) but not an interactive logon, and users are created as a side effect of first
    /// sign-in rather than through an audited call. Routing them is a one-line addition here once those
    /// events are raised; a default naming an event nothing emits would look configured and do nothing.
    /// </remarks>
    public static IList<NotificationRoute> DefaultRoutes() =>
    [
        new() { Event = "team:create", Template = "New team *{team.name}* created by {actor}." },
        new() { Event = "team:invite", Template = "{actor} invited *{member.email}* to team {team}." },
        new() { Event = "team:remove-member", Template = "{actor} removed a member from team {team}." },
        new() { Event = "user:delete", Template = "{actor} deleted user *{user.key}*." },

        // Somebody asking for help is the one built-in worth answering promptly, so it ships as a default
        // rather than as something a host has to know to add. {case.url} renders empty until a URL template
        // is configured, which leaves the message readable rather than broken.
        new() { Event = "support:raise", Template = "New support case *{support.case.subject}* from {actor} on team {team}. {case.url}" },

        // A reply matters as much as the first message: a case somebody has come back to is waiting on
        // support again. Worded to be readable in a channel that only ever sees these two events.
        new() { Event = "support:reply", Template = "Reply on support case {support.case.id} from {actor}. {case.url}" }
    ];
}
