# Slack notifications

Send audited events to Slack, each event to the channel you choose, worded the way you choose.

This lives in the optional **`Tharga.Team.Support`** package — the first capability of the support
module. Nothing in the toolkit references it, so installing or upgrading anything else never brings
Slack along.

## Setup

Create a Slack app, add the **`chat:write`** bot scope, install it to the workspace, and invite the bot
to every channel you route to. Then:

```csharp
services.AddThargaAuditLogging();

services.AddThargaSupport(o =>
{
    o.Slack.BotToken = builder.Configuration["Slack:BotToken"];
    o.Notifications.DefaultChannel = "#team-events";
});
```

Order matters: notifications are an audit sink, so `AddThargaAuditLogging` has to come first — without
audit logging there is nothing to notify about. `AddThargaTeam` already calls it, so in a standard
Blazor host there is nothing extra to do.

That is the whole setup. Until both a token and a channel exist the module is registered and silent,
which is the intended state for a host that has installed the package but not finished configuring it.

## Routing

A route says **which** events it matches, **where** they go, and **how** they read.

```csharp
services.AddThargaSupport(o =>
{
    o.Slack.BotToken = token;
    o.Notifications.DefaultChannel = "#team-events";
    o.Notifications.Routes =
    [
        new() { Event = "team:create",  Template = "New team *{team.name}* created by {actor}." },
        new() { Event = "team:invite",  Template = "{actor} invited *{member.email}* to team {team}." },
        new() { Event = "team:set-consent", Channel = "#security" },
        new() { Event = "*", Channel = "#alerts", Success = false, Template = "{event} failed: {error}" }
    ];
});
```

| Property | Meaning |
|----------|---------|
| `Event` | `feature:action`, the same shape as a scope. `team:create` matches exactly, `team:*` matches every action on teams, `*` matches everything. Case-insensitive. |
| `Channel` | Channel name or id. Omit to use `DefaultChannel`. |
| `Template` | The message. Omit for a readable default built from the event. |
| `Success` | Restrict to successes (`true`) or failures (`false`). Omit for both. |

**Every matching route fires**, not just the first — so one event can go to two channels worded two
ways. The flip side is that a `*` route alongside a specific one posts twice.

### Turning an event off

**The routing table is the allowlist.** An event no route matches is not sent, so removing an event is
removing its route — configuration, never a code change, and never a deployment. There is no second
on/off concept that could drift out of step with the table.

```csharp
// Everything except the noisy one.
o.Notifications.Routes = NotificationOptions.DefaultRoutes()
    .Where(r => r.Event != "team:invite")
    .ToList();
```

Clearing the list turns notifications off entirely without unregistering anything.

### The built-in routes

`NotificationOptions.DefaultRoutes()` covers a team being created, a member being invited or removed, a user
being deleted, and **a support case being raised**. They name no channel, so they use `DefaultChannel` — and
stay dormant until one is set, rather than posting into a channel nobody picked.

`support:raise` is only ever emitted when `AddThargaSupportCases` is registered, so a host using
notifications alone simply never triggers it.

Replace the list to take full control, or edit it to add and remove single events.

## Message templates

Placeholders are `{name}`:

`{event}` · `{feature}` · `{action}` · `{actor}` · `{team}` · `{time}` · `{outcome}` · `{error}`

**Any other name is looked up in the entry's metadata**, so the audit vocabulary works directly with no
mapping table to maintain beside it:

```
{team.name}  {member.email}  {member.accesslevel.new}  {consent.roles}
```

A name that resolves to nothing renders as empty rather than leaving braces in the channel — so a typo
shows up as a gap in the message.

### Linking to a support case

`{case.url}` renders a link to the case an entry is about:

```csharp
o.Notifications.CaseUrlTemplate = "https://app.example.com/support/{caseId}";
```

**You supply the template because only you know it.** The toolkit has no convention for where a case is shown
— `/support` is what the sample happens to choose — and it cannot infer the public address either, since a
reverse proxy, a custom domain and a container's own hostname all disagree. A guess would produce links that
work in development and break in production.

**Unset renders nothing rather than something broken**, so a route worded with a link stays readable without
one — which is why the built-in `support:raise` route can ship with `{case.url}` in its wording. The same
holds for an entry that is not about a case: a team event borrowing that wording emits no link rather than a
link to nothing.

The case id itself has always been available as `{support.case.id}`, through the metadata fall-through above.
`{case.url}` is the template applied to it.

Omit `Template` and the message is built for you: the event, the actor, the team, and the reason if it
failed.

## Your own events

Anything written to the audit log can be routed, including your own operations. There is no second
mechanism and no registration step — build the entry through `IAuditEntryFactory` so the caller is
filled in, then log it:

```csharp
var entry = auditEntryFactory.Create("invoice", "paid", teamKey: teamKey,
    metadata: new Dictionary<string, string> { ["invoice.number"] = number });
auditLogger.Log(entry);
```

```csharp
new() { Event = "invoice:paid", Channel = "#billing", Template = "Invoice {invoice.number} paid." }
```

## What you can route today

Every mutation the toolkit audits: teams (`team:create`, `team:rename`, `team:delete`,
`team:transfer-ownership`, `team:icon-set`, …), membership (`team:invite`, `team:remove-member`,
`team:set-role`, `team:set-tenant-roles`, …), consent (`team:set-consent`), users (`user:verify`,
`user:delete`, `user:set-user-name`), API keys, and support cases (`support:raise`, `support:reply`,
`support:close`, `support:reopen`).

**Not yet: user sign-in and user creation.** Neither is an audited event today — the toolkit audits
API-key authentication but not an interactive logon, and users are created as a side effect of first
sign-in rather than through an audited call. When those events are raised they become routable with no
change to the package.

## Two things to know

**The audit filter sits upstream of routing.** An `AuditOptions.EventFilter` or `ExcludedActions` that
drops an entry drops the notification with it, however the route reads. That is the cost of
notifications being a sink on the existing audit seam rather than a parallel mechanism — and the
benefit is that enrichment, caller resolution and filtering all already apply.

**Slack never affects the operation.** A missing token, an unreachable network, a channel the bot was
never invited to, a Slack outage — each is logged as a warning and nothing else. Posting happens on a
background pump, so no HTTPS round trip ever runs on the thread of the operation being audited.

## Troubleshooting

| Symptom | Cause |
|---------|-------|
| Nothing posts at all | No `BotToken`, or no `DefaultChannel` and no route names a channel. |
| One event never posts | No route matches it — check the `feature:action` spelling against the audit log. |
| `channel_not_found` in the logs | The bot is not a member of that channel. Invite it. |
| `invalid_auth` in the logs | Wrong or revoked token, or the app is missing `chat:write`. |
| A placeholder renders empty | The name is not a built-in and is not a metadata key on that event. |
| Everything stopped after an audit change | `AuditOptions` is filtering the entry out before routing. |

## What this package will grow into

Slack notifications are capability one. Planned: support cases, Slack inbound, email in and out, an AI
support bot, and Jira tickets with a customer-facing ticket view. Those bring real dependencies, which
is why this is a separate package rather than part of `Tharga.Team.Service`.
