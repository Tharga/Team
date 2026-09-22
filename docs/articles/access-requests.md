# Team access requests — time-bound access, granted by the team

Let someone who holds a consent role ask a team for access at a level, for a time. The team's managers
approve or deny it, and when the window runs out the team's previous consent comes back.

It exists so that reaching into a customer's tenant is a thing the customer **grants and can watch expire**,
rather than a standing privilege somebody remembers to remove. Support, operations and developer roles need
occasional access to a team they do not belong to; consent already expresses that, but until now it had no
end date and no way for the person needing it to ask.

**Approval grants through the team's consent, not to the person.** Every configured consent role reaches
the team at the approved level until the window closes. That is how consent works in this toolkit, and the
approval screen says so plainly — a manager approving one developer's request is admitting everyone holding
that role.

## Setup

Access requests build on consent, so a team must have consent roles configured for anything to be
requestable:

```csharp
builder.AddThargaTeam(o =>
{
    o.Blazor.Consent.Roles = ["Developer"];   // the roles a team may consent to
});
```

No new scope and no new option. A caller may request access if they hold one of those roles; a manager may
approve if they hold `team:manage` **as a member** of the team.

The requester and manager UI both live inside `<TeamComponent>` and need no wiring. The notification bell is
optional — place it wherever your layout puts such things, typically beside the team selector:

```razor
<TeamSelector />
<TeamNotificationMenu />
```

The badge counts requests waiting for **this caller's** decision. The menu lists those first, then the
caller's own pending requests, uncounted — waiting on someone else is not a task. Choosing one selects that
team and opens the team page, because `team:manage` is issued for the selected team only.

## What can be asked for

| | |
|---|---|
| **Levels** | Viewer, User, Administrator. **Not Owner** — ownership is not a thing to lend — and not Custom, which grants no base scopes |
| **Durations** | 1 hour · 8 hours · 1 day · 1 week · 30 days · no end |
| **Message** | Optional, up to 1000 characters |

**A duration is requested, not an end time.** The window starts at approval, so a request that sits waiting
for a decision does not spend the access it asked for.

**One pending request per requester per team.** Asking again replaces the previous one rather than queueing
a second, so a manager never has to work out which of two competing asks from the same person is current.

## Who may do what

| Operation | Allowed for |
|---|---|
| `RequestTeamAccessAsync` | A caller holding a configured consent role as an **application** role, who is **not a member** of the team. Membership outranks consent, so a request from a member would grant nothing |
| `CancelTeamAccessRequestAsync` | The requester, and nobody else |
| `ApproveTeamAccessRequestAsync` | `team:manage` held **as a member**; never the requester themselves |
| `DenyTeamAccessRequestAsync` | `team:manage` held **as a member** |

All four are enforced in the domain, not in the UI. The components hide what a caller cannot do, and the
service refuses it anyway.

## Breaking change: changing consent now requires membership

**`SetTeamConsentAsync` requires `team:manage` held as a member of the team.** It previously accepted the
scope however it arrived — including through consent itself.

That was survivable while consent was open-ended. With time-bounded consent it is not: a caller consented in
at Administrator holds `team:manage` in that team, and could therefore change the very consent that admitted
them — extending their own window, or removing its expiry altogether. The grant would have been able to
rewrite its own terms.

A team API key naming its own team still counts as direct; a system key reaching the team through consent
does not.

**If a host relies on a consented caller changing consent, that stops working in 3.23.** The fix is to have
a member do it, which is what the operation always meant.

## Where the two services live

Read the split in *Service layering and authorization* terms: one interface is **gated**, the other is
**filtered**.

```csharp
// Gated — [RequireScope(TeamScopes.Manage)], every method names a team first.
Task<IReadOnlyList<TeamAccessRequest>> ITeamManagementService.GetAccessRequestsAsync(string teamKey);
Task ITeamManagementService.ApproveTeamAccessRequestAsync(string teamKey, string requestId);
Task ITeamManagementService.DenyTeamAccessRequestAsync(string teamKey, string requestId);

// Filtered — names no team, so it cannot be gated; visibility is recomputed per team.
Task<TeamAccessRequest> ITeamAccessRequestService.RequestTeamAccessAsync(string teamKey, AccessLevel accessLevel, TimeSpan? duration, string message);
Task ITeamAccessRequestService.CancelTeamAccessRequestAsync(string teamKey, string requestId);
Task<IReadOnlyList<TeamAccessRequestItem>> ITeamAccessRequestService.GetMyAccessRequestsAsync();
Task<IReadOnlyList<TeamAccessRequestItem>> ITeamAccessRequestService.GetAccessRequestsAwaitingMeAsync();
```

`ITeamAccessRequestService` is registered by the library with `TryAdd` semantics, like every other team
service facet — you do not enumerate it yourself.

**Requesting access cannot be gated by a scope**, and that is the point rather than an omission: a requester
holds nothing in the team they are asking about. The check is that they hold a consent role and are not
already a member.

## Expiry needs nothing to run

There is no background job, and no event at the moment a window closes.

Every consent read goes through one function:

```csharp
ConsentInForce TeamConsent.Resolve(ITeam team, DateTime utcNow);
```

It returns the stored consent while unexpired and the **previous** consent afterwards, so expiry takes
effect at the next claims issuance or revalidation — which is the same mechanism that already applies a
removed membership or a revoked consent. `ClaimRevalidation.Interval` therefore bounds how long an expired
window can still be in use, exactly as it bounds every other access change.

An architecture test fails the build if anything reads `ConsentAccessLevel` or `ConsentedRoles` directly
instead of resolving through it. That test is the only thing keeping "expired" from meaning different things
in the claims path, the API-key team context, the consented-team lookup, the team badges and the MCP team
resource.

**A direct consent change clears the expiry.** When a manager sets consent by hand, the result is standing
consent — they have made a decision that supersedes the window, and a temporary grant quietly surviving
underneath it would be a surprise later.

## What is recorded

Four audit actions, through the ordinary decorator, so existing notification routes carry them:

| Action | Recorded |
|---|---|
| `request-access` | requester, level asked for, duration |
| `approve-access-request` | the above, plus the previous consent and when the grant ends |
| `deny-access-request` | who decided |
| `cancel-access-request` | who withdrew it |

Metadata keys are `accessrequest.id`, `accessrequest.requester.key`, `accessrequest.accesslevel`,
`accessrequest.duration` (`d.hh:mm:ss`, or `none`) and `accessrequest.granteduntil` (ISO 8601 UTC, or `none`
for a standing grant).

The **audit log is the record**; the team document is not. A team keeps its 50 most recent requests
(`TeamAccessRequestRules.HistoryLimit`) and drops older ones as new arrive.

## For hosts with a custom store

`TeamServiceBase` gains three virtual methods that throw until implemented — `AddAccessRequestAsync`,
`DecideAccessRequestAsync` and `ApproveAccessRequestAsync` — following the same pattern as
`SetTeamMemberSuspendedAsync`. An existing host keeps compiling, and a host that has not implemented them
gets a `NotSupportedException` naming the method rather than a silent no-op.

`Tharga.Team.MongoDB` implements all three. **Approval is one conditional document update**: it sets the
consent, records the previous consent and marks the request approved together, so there is no window in
which a team is consented-to by a request that is still pending.

> **If you decorate `ITeamService` yourself, forward the new members.** They are default interface members,
> so a decorator that does not forward them compiles and then resolves to the throwing default at runtime.
> This is the same trap that broke short invitation links in 3.20; `DecoratorDefaultMemberTests` now pins
> every decorator in the toolkit against it, but it cannot see yours.

## What this deliberately does not do

- **No email to managers.** The notification menu and the audit route are the channels.
- **No approve-with-changes.** A manager approves what was asked or denies it; negotiating a different level
  or duration in the approval screen would mean the requester consents to terms they never saw.
- **No system API key requesting access.** A key is not a person who can be told why the answer was no.
- **No audit entry at the moment of expiry.** The approval entry already records when the window ends.
