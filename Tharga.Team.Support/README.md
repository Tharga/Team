# Tharga.Team.Support

Support and notifications for [Tharga.Team](https://www.nuget.org/packages/Tharga.Team).

The first capability is **Slack notifications**: audited events are matched against a routing table and
posted to the channel the matching route names, worded per event.

This package is optional and nothing in the toolkit references it, so no existing consumer acquires
Slack — or any later support dependency — by upgrading what they already had.

## Install

```
dotnet add package Tharga.Team.Support
```

## Register

Call it after `AddThargaAuditLogging`. Notifications are an audit sink, so without audit logging there
is nothing to notify about.

```csharp
services.AddThargaAuditLogging();

services.AddThargaSupport(o =>
{
    o.Slack.BotToken = builder.Configuration["Slack:BotToken"];
    o.Notifications.DefaultChannel = "#team-events";
});
```

That is the whole setup. The built-in routes cover a team being created, a member being invited or
removed, and a user being deleted.

## Routing

A route says which events it matches, where they go, and how they read.

```csharp
services.AddThargaSupport(o =>
{
    o.Slack.BotToken = token;
    o.Notifications.DefaultChannel = "#team-events";
    o.Notifications.Routes =
    [
        new() { Event = "team:create", Template = "New team *{team.name}* created by {actor}." },
        new() { Event = "team:*", Channel = "#team-audit" },
        new() { Event = "*", Channel = "#alerts", Success = false, Template = "{event} failed: {error}" }
    ];
});
```

- **`Event`** is `feature:action` — the same shape as a scope. `team:create` matches exactly, `team:*`
  matches every action on teams, `*` matches everything. Case-insensitive.
- **`Channel`** is a channel name or id. Omit it to use `DefaultChannel`.
- **`Template`** is the message. Omit it for a readable default built from the event.
- **`Success`** restricts a route to successes or failures. Omit it for both.

**The routing table is the allowlist.** An event no route matches is not sent, so removing a route is
how you stop the posts — configuration, never a code change. That matters most for the high-volume
entries on a large tenant, and it is why there is no all-or-nothing switch.

**Every matching route fires**, not just the first, so one event can go to two channels worded two
ways. A `*` route alongside a specific one therefore posts twice.

### What you can route today

Anything the toolkit audits, which is every mutation on teams, members, users and API keys —
`team:create`, `team:invite`, `team:set-consent`, `user:delete`, and so on — plus your own events.

**Now routable: user sign-in and user creation.** Both were missing - the toolkit audited API-key
authentication but not an interactive logon, and users are created as a side effect of first sign-in
rather than through an audited call. Both are raised as of the release carrying this note, and route here
with no change to this package:

```csharp
new() { Event = "auth:signin", Channel = "#activity", Template = "{actor} signed in." },
new() { Event = "auth:user-created", Channel = "#activity", Template = "New user {user.email}." },
```

`auth:signin` is an auth event, so `AuditOptions.EventFilter` must admit `AuthEvents` for it to reach
routing at all. `auth:user-created` is a data change and carries `{user.key}` and `{user.email}`.

### Template placeholders

`{event}` · `{feature}` · `{action}` · `{actor}` · `{team}` · `{time}` · `{outcome}` · `{error}`

Any other name is looked up in the entry's metadata, so the audit vocabulary works directly:
`{team.name}`, `{member.email}`, `{member.accesslevel.new}`. A name that resolves to nothing renders as
empty.

## Your own events

Anything written to the audit log can be routed, including your own operations. Build the entry through
`IAuditEntryFactory` so the caller is filled in, then log it:

```csharp
var entry = auditEntryFactory.Create("invoice", "paid", teamKey: teamKey,
    metadata: new Dictionary<string, string> { ["invoice.number"] = number });
auditLogger.Log(entry);
```

```csharp
new() { Event = "invoice:paid", Channel = "#billing", Template = "Invoice {invoice.number} paid." }
```

There is no second mechanism for custom events, and no registration step.

## Two things to know

**The audit filter sits upstream of routing.** An `AuditOptions.EventFilter` or `ExcludedActions` that
drops an entry drops the notification with it, however the route reads. This is the cost of
notifications being a sink on the existing seam rather than a parallel one.

**Slack never affects the operation.** A missing token, an unreachable network, a channel the bot was
never invited to — each is logged as a warning and nothing else. A notification observes something that
already happened, so it cannot be allowed to undo it.

## Slack setup

Create a Slack app, add the **`chat:write`** bot scope, install it to the workspace, and use the bot
token (`xoxb-…`) as `BotToken`. Invite the bot to every channel you route to.

## What this package will grow into

Slack notifications are capability one of the support module. Planned: support cases, Slack inbound,
email in and out, an AI support bot, and Jira tickets with a customer-facing ticket view. Those bring
real dependencies, which is why this is a separate package rather than part of `Tharga.Team.Service`.

## Links

- [Tharga.Team on GitHub](https://github.com/Tharga/Team)
