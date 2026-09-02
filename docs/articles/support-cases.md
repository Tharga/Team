# Support cases

A signed-in member can raise a support case for their team, reply to it, and read its history. Cases are
persisted, authorized and audited.

> **What this is not, yet.** There is **no Jira link, no AI responder and no UI component**. Slack threads
> work in both directions; the rest is in [issue #142](https://github.com/Tharga/Team/issues/142).

## Enabling it

```csharp
builder.Services.AddThargaTeamRepository(o =>
{
    o.RegisterTeamRepository<TeamEntity, TeamMember>();   // provides the case store
});

builder.Services.AddThargaSupportCases();
```

`AddThargaSupportCases()` is **separate from `AddThargaSupport()`** on purpose: notifications must be usable
without the case machinery, so a product that only wants "post to Slack when a team is created" does not
acquire a case store and a scope pair for it.

The case store comes from `AddThargaTeamRepository`. Nothing in the support package registers storage — that
choice belongs to the host, and this package must not acquire a database dependency.

## The model

A case belongs to a **team**, has an author, a status, and a transcript.

| | |
|---|---|
| `SupportCase` | Header: id, team, author, subject, status, timestamps, message count, channel bindings |
| `SupportMessage` | One transcript entry: sequence, kind (user or system), author, body, timestamp |
| `SupportChannelBinding` | A projection onto an external system. **Always empty today** |

**The team owns the case; the author is a link that may dangle.** A case is raised by a person but is about
the team's account, and the team is what still exists a year later. Deleting a user removes their membership
from every team — so a case keyed on a member would be orphaned by design. Authorization keys on the team
alone, and nothing requires the author to still resolve.

**The author is recorded twice**: a stable subject for exact matching, and a display-name **snapshot** so the
transcript stays readable after that person is deleted or leaves. The audit trail carries the same pair for
the same reason.

**The header does not carry the transcript.** A case can accumulate years of messages, so `SupportCase` has a
`MessageCount` and history is read separately and paged.

## Who may do what

| Operation | Authorized by |
|---|---|
| Raise a case; list your own cases | **Membership.** No scope |
| Read, reply to, close or **reopen** your own case | **Authorship** |
| Read or list **anyone's** case | `support:read` |
| Reply to, close or **reopen** anyone's case | `support:manage` |

**Not everything is a scope, deliberately.** Raising a case about your own team is what an ordinary member
does; gating it would mean every host granting that scope to everybody, and a scope everyone holds checks
nothing. Membership and authorship are still *checks* — they are simply not grants.

> **`support:read` is a real privilege boundary.** A support case contains whatever a user typed into it,
> which is exactly where somebody pastes a password, a token or a customer's details. Grant it as carefully
> as you grant `audit:read`.

Both scopes are team scopes registered at `Administrator`. A case is always loaded through its team, so
holding a valid case id from another tenant gains nothing.

## The subject is optional

A subject is a small tax on somebody who has already typed the problem into the box below it, and what it
usually collects is a worse version of the first sentence. So it is **off by default**:

```csharp
builder.Services.AddThargaSupportCases(o => o.UseSubject = true);   // default: false
```

**`UseSubject` decides what a person is asked for, never whether the case ends up with a subject.** With no
subject supplied, the service derives one from the first 50 characters of the message — cut at a word
boundary, with whitespace collapsed first and an ellipsis marking the truncation. `SupportCase.Subject` is
never null, so nothing downstream has to cope with a case that has none.

That means a half-filled form cannot produce a case that renders as an empty row in a list, even with the
field shown.

## Reopening a case

```csharp
await supportCases.ReopenCaseAsync(teamKey, caseId);
```

Authorized exactly as replying is: the member who raised it, or a holder of `support:read` / `support:manage`.
It writes a system entry, clears the closure, and returns the case to `Open` — **keeping the history**, which
is the whole reason it exists rather than telling somebody to raise a second case that explains nothing.

**Reopening an already-open case does nothing and is not an error.** Two people looking at the same closed
case both press the button; the second sees an open case rather than a complaint.

## Closing after inactivity

A case support answered and nobody came back to closes itself after seven days.

```csharp
builder.Services.AddThargaSupportCases(o =>
{
    o.AutoCloseAfter = TimeSpan.FromDays(7);        // default; Zero turns it off
    o.AutoCloseSweepInterval = TimeSpan.FromHours(1);
    o.AutoCloseBatchSize = 100;
});
```

**The direction is the important part.** The clock runs only while the case is waiting on the *customer*: the
newest entry is support's and nobody has replied. A case whose newest entry is the **customer's** never
closes, however old — that one is waiting on *you*, and closing it would hide your backlog rather than tidy
it. A *system* entry does not start the clock either, so reopening a case does not immediately re-arm it.

`SupportCase.ClosedReason` distinguishes the two closures — `Manual` or `Inactivity` — so a component can say
"closed automatically, reopen it if the problem is still there" rather than something that reads as a
dismissal. It is derived from `ClosedBy`, which the sweep records as `SupportCaseActors.AutoClose`.

**`TimeSpan.Zero` registers no background work at all**, rather than a sweep that finds nothing every hour.

> **Existing cases are not swept until they are next written to.** The sweep needs the last-activity
> timestamp, which older documents do not carry. That is deliberate: applying new behaviour retroactively
> would close an untouched backlog in bulk on the first sweep after upgrading.

## Components

Two components ship in `Tharga.Team.Blazor`, and **both are optional**:

```razor
@* What a team member sees: raise a case, read the conversation, reply, reopen *@
<SupportCasesView ShowSubject="false" />

@* What support sees: every case in the team, answer, close, reopen *@
<SupportQueueView />
```

`SupportQueueView` needs `support:read`, and the service refuses without it — so gate rendering on the scope
to spare a member a refusal they can do nothing about.

**A host can always build its own instead, and that is the point.** Both components go through
`ISupportCaseService` and nothing else — no store, no internal service — and a test asserts it. If a shipped
component needed something you cannot reach, the surface would be incomplete and the component would be
hiding it.

`ShowSubject` is a parameter rather than a read of `UseSubject`, because the options live in
`Tharga.Team.Support` while the components depend on contracts only. Pass the same value you configured; or
differ per page, which reading the options could not express.

**Closing from `SupportQueueView` warns first** and suggests letting the person who raised the case close it
— they are best placed to say the problem is solved, and a case closed while somebody is still typing reads
as a dismissal. It is advisory: support can still close a case that is finished.

## Reading history

Paging uses an **explicit cursor**, and the cursor is the sequence of the last message returned:

```csharp
var page = await support.GetMessagesAsync(teamKey, caseId, pageSize: 50);

while (page.NextCursor != null)
{
    page = await support.GetMessagesAsync(teamKey, caseId, page.NextCursor, pageSize: 50);
}
```

A sequence cursor rather than an offset, because **a support conversation is appended to while somebody is
reading it**. With an offset, a reply arriving between two page reads shifts every later entry and the reader
silently sees one twice or misses one.

## Limits

| Limit | Value | Why |
|---|---|---|
| `SupportCaseLimits.MaxMessageLength` | 10,000 characters | Support text is where somebody pastes a log file |
| `SupportCaseLimits.MaxMessagesPerCase` | 500 | The transcript is embedded in one document |

Exceeding either throws with a message naming the limit. At their product the transcript is roughly 5 MB
against MongoDB's 16 MB document limit, so a case of maximum-length messages still has headroom.

The transcript is embedded rather than kept in a second collection because that makes each operation a single
write: raising a case creates the case *and* its first message, and closing one sets the status *and* records
why. Neither can half-apply, so a case always has a transcript.

## Auditing

Raise, reply and close are three distinct audited facts under the `support` feature, carrying the case id and
(on raise) the subject. **Refusals are audited too**, as failed entries with the reason — a denied attempt to
reach another team's case is worth knowing about.

**Message bodies are never recorded.** An audit entry has more readers, longer retention and easier export
than the case itself.

## Slack threads

A case can be projected onto a Slack thread. Replies flow both ways: a reply raised here appears in the
thread, and a reply typed in the thread appears on the case.

```csharp
builder.Services.AddThargaSupportCases(o =>
{
    o.SlackChannel  = builder.Configuration["Slack:SupportChannel"];   // e.g. "#support"
    o.SigningSecret = builder.Configuration["Slack:SigningSecret"];    // only needed to receive replies
});

app.MapThargaSupportSlack();          // where Slack posts thread replies
```

**Leave `SlackChannel` unset and nothing changes.** Cases stay on the site, exactly as they behave without
this feature — that is the ordinary configuration for a host that does not want Slack, not a degraded one.

### Configuring the Slack app

**There is no Tharga app to install, and there cannot be.** Slack issues tokens per workspace installation,
so every consumer creates their own app. A distributed app would mean Tharga hosting an OAuth endpoint and
holding your workspace's credentials — and it would not even help, because inbound events have to reach
*your* deployment rather than ours.

What we ship instead is a manifest, so this is a short job rather than an afternoon of guessing at scopes.

**Setting up is two phases, and the order matters.** The manifest declares the app and its scopes, which is
everything the *outbound* half needs. Event subscriptions come after, because Slack verifies the request URL
the moment you save it — so there has to be something listening first.

**Phase one — the app, and everything outbound:**

1. **Create the app from the manifest.** Slack API → *Your Apps* → *Create New App* → *From an app manifest*,
   choose the workspace, and paste
   [`slack-app-manifest.json`](https://github.com/Tharga/Team/blob/master/slack-app-manifest.json) from the
   repository root, unedited.
2. **Install to workspace**, then copy the **Bot User OAuth Token** (`xoxb-…`) into `Slack:BotToken` and the
   **Signing Secret** (Basic Information → App Credentials) into `SigningSecret`.
3. **Invite the bot to each channel** — `/invite @Tharga Support`. It cannot post to a channel it is not in,
   and it cannot read the member list of one either, which is how presence is determined.

At this point notifications and case threads work. Replies typed in Slack do not yet.

**Phase two — inbound replies, once you have a public URL:**

4. **Get a public address.** A deployment already has one; locally you need a tunnel — Visual Studio's *Dev
   Tunnels* or ngrok. Slack cannot reach `localhost`.
5. **Event Subscriptions** → enable, and set the request URL to
   `https://your-host/_tharga/support/slack/events`. Slack sends a one-off challenge as you save; the
   endpoint answers it automatically, so a green tick here means the whole inbound path is wired.
6. Under **Subscribe to bot events**, add `message.channels` for a public support channel, or
   `message.groups` for a private one.

> **The manifest deliberately carries no `request_url` and no comments.** Slack validates the document
> strictly: an unknown key is rejected outright, and a placeholder URL is rejected because it is *verified*
> rather than merely stored. The guidance that belongs with a human lives here instead of in the file.

The manifest asks for exactly four scopes, each required by code:

| Scope | Used by | For |
|---|---|---|
| `chat:write` | `chat.postMessage` | posting notifications and case replies |
| `users:read` | `users.getPresence` | whether anybody on support is active |
| `channels:read` | `conversations.members` | who is on a public support channel |
| `groups:read` | `conversations.members` | the same, for a private channel |

**Remove a scope and the feature that needs it stops working quietly**, not loudly — the client reports
failures rather than throwing, by design, so a missing scope looks like nothing happening.

### In production

**There is no tunnel.** Your application already has a public HTTPS address, so the inbound endpoint is a
route rather than something to acquire — which is why a webhook was chosen over Socket Mode in the first
place. The tunnel exists only because Slack cannot reach `localhost`.

Four steps, once per deployment:

1. **Create a Slack app** from the manifest, exactly as for local work.
2. **Set the request URL** to the real address —
   `https://app.example.com/_tharga/support/slack/events`.
3. **Subscribe to `message.channels`** and install to the workspace.
4. **Put the bot token and signing secret** into that environment's secret store.

The code is identical; only the URL differs.

> **One Slack app per deployment.** An app has exactly one Event Subscriptions request URL, so production and
> staging cannot share one — and neither can two products in the same workspace. Use the same manifest for
> each, and give each its own channel so test traffic never reaches real support.

**Three things in the request path break signature verification**, and each fails looking like a wrong
secret:

- **Anything that alters the body.** The signature covers the exact bytes Slack sent, so a proxy that
  re-serializes, re-encodes or reformats JSON invalidates every request.
- **A redirect.** Slack does not follow them, so an HTTP-to-HTTPS jump or a trailing-slash normalisation on
  that route fails verification.
- **Stripping `X-` headers.** `X-Slack-Signature` and `X-Slack-Request-Timestamp` *are* the credential.

**Running several instances needs nothing extra.** Inbound is stateless and load-balances like any request,
and the event ledger deduplicates across instances through a unique index — so whichever instance takes a
retry, only one reply is appended. Keep
`ThargaTeamOptions.SupportEventLedgerRetention` (default 24 hours) above Slack's retry horizon.

**Watch the log.** `SlackClient` reports failures rather than throwing, so nothing surfaces in the
application: alert on its warnings. The two failures that otherwise pass unnoticed are the bot being removed
from a channel and Slack disabling an event subscription after repeated delivery failures.

### The four settings

```csharp
builder.Services.AddThargaSupport(o =>
{
    o.Slack.BotToken = config["Slack:BotToken"];                      // xoxb-… from step 2
    o.Notifications.DefaultChannel = config["Slack:Channel"];          // where event notifications go
    o.Notifications.CaseUrlTemplate = config["Slack:CaseUrl"];         // https://you.example.com/support/{caseId}
});

builder.Services.AddThargaSupportCases(o =>
{
    o.SlackChannel = config["Slack:SupportChannel"];                   // where case threads go
    o.SigningSecret = config["Slack:SigningSecret"];                   // from step 2; needed to receive replies
});
```

Keep the token and the secret out of source control — `dotnet user-secrets` locally, your platform's secret
store in a deployment. The sample reads exactly these keys and stays dormant without them.

> **The endpoint is public and unauthenticated, deliberately.** Slack cannot present a credential, so the
> request signature *is* the credential: every request is verified with HMAC-SHA256 over the raw body before
> anything is read from it, and one older than five minutes is refused so a captured request cannot be
> replayed. Putting an authorization policy on the route would simply stop Slack reaching it.

> **Slack cannot reach `localhost`.** To try this locally you need a public URL — a tunnel such as ngrok
> pointed at your dev server — or you can exercise the endpoint by posting a correctly signed request
> yourself. Without one, outbound works and inbound silently never arrives.

### Knowing when something changed

`ISupportCaseNotifier` is raised whichever side replied, so a UI reacts the same way to both:

```csharp
@implements IDisposable
@inject ISupportCaseNotifier Notifier

protected override void OnInitialized() => Notifier.CaseUpdated += OnCaseUpdated;

public void Dispose() => Notifier.CaseUpdated -= OnCaseUpdated;

private void OnCaseUpdated(object sender, SupportCaseUpdatedEventArgs e)
    => _ = InvokeAsync(async () => { await ReloadAsync(); StateHasChanged(); });
```

Three things this will not forgive:

- **Marshal with `InvokeAsync`.** A reply from Slack is handled on the request thread of Slack's POST, not on
  your circuit. Touching component state directly updates off the synchronization context and loses the
  render.
- **Unsubscribe.** The notifier is a singleton and outlives every page. A component that subscribes and never
  detaches keeps itself alive for the lifetime of the application.
- **It carries no authorization.** Every subscriber hears about every case in every team — a singleton has no
  caller to filter by, and `FromChannel` plus the team key is all it says. Read the case back through
  `ISupportCaseService` to act on it, where the checks are.

`FromChannel` distinguishes a reply somebody typed here from one that arrived from Slack. The second is
usually the one worth notifying about; the first, the user is already looking at.

> **In-process only.** On more than one instance the notification is raised on whichever instance handled the
> change — a Slack reply wakes the one whose endpoint Slack reached, not necessarily the one holding the
> user's circuit. Fanning that out needs a backplane, which is not built.

### Whether a message got there

Every entry records its own delivery state, so a message that never reached Slack is visible rather than only
logged:

| State | Meaning |
|---|---|
| `NotApplicable` | The case has no channel — nothing to deliver to |
| `Pending` | Written, not confirmed sent. Retryable, and worth reminding about |
| `Sent` | The channel took it. A reply arriving *from* Slack is `Sent` by definition |
| `Failed` | The channel refused it or was unreachable |

**A channel being down never blocks a case.** The case is written first and is authoritative; projecting it
comes after. If Slack refuses, the case still exists and the entry is `Pending`.

## Is anybody on support

```csharp
@inject ISupportPresence Presence      // resolve with GetService: absent when Slack is not configured
```

`GetAsync` answers `Online`, `Away` or `Unknown`, from **who is active on the configured support channel** —
so adding somebody to the channel is how they become support, and there is no second list to keep in step.

**`Unknown` must render as nothing, never as "offline".** Telling a customer not to bother when support is in
fact there is worse than saying nothing, and unknown is what a rate limit, a network blip and an
unconfigured workspace all produce. Three separate paths preserve that distinction: an unreadable channel
keeps the previous roster rather than concluding support is empty, all-unknown presence stays unknown, and a
transport failure is unknown rather than an error.

**Advisory, never a gate.** Nothing that raises a case may wait on this or be refused by it.

**It is cached, and it has to be.** Slack rate-limits `users.getPresence`, and it is per user — so asking
about a channelful of people on every render is how a deployment gets throttled. The channel roster is
trusted for ten minutes and presence for sixty seconds, because the two questions change at completely
different rates.

> **The cache is process-local.** On several instances each keeps its own, so you make that many times the
> calls — still bounded by the interval, and a stale answer is already harmless because presence is
> advisory. This is deliberately unlike the inbound event ledger, where process-local state would be a
> correctness defect rather than a cost.

## Knowing what needs attention

Two questions, two methods, two different checks — because they serve two audiences and one of them is
privileged:

| Method | Answers | Requires |
|---|---|---|
| `GetMyUnreadCountAsync(teamKey)` | my own cases holding entries I have not read | membership |
| `GetAwaitingSupportCountAsync(teamKey)` | open cases whose newest entry came from the person who raised them | `support:read` |
| `MarkReadAsync(teamKey, caseId)` | records that I have read up to the newest entry | the same check as reading that case |

**Not one result carrying both numbers.** That would either show an ordinary member the support-wide figure
or arrive half-populated — and a half-populated result is what a component renders as a zero.

**These are public API on purpose.** A count chip or a dashboard panel is something you can build yourself
against exactly this; nothing about it is reserved for a component the toolkit ships.

### How the two behave

**Support's side needs no read state.** A case is awaiting an answer while its newest entry came from its
author, so the count is correct however many support people look at it, and closing a case removes it from
the count.

**A user's side does.** Opening a case marks it read; a reply arriving afterwards makes it unread again. Two
people on one case have independent state, and the marker never moves backwards — a second tab showing an
older page cannot relight the indicator. Replying counts as reading, so writing a message does not light your
own chip.

```csharp
var unread = await support.GetMyUnreadCountAsync(teamKey);

// Gate rendering on the scope with TeamScopeGate; the service checks it again on the call itself.
if (TeamScopeGate.HasTeamScope(principal, SupportScopes.Read, teamKey))
{
    var awaiting = await support.GetAwaitingSupportCountAsync(teamKey);
}
```

> **A chip does not update itself across instances.** `ISupportCaseNotifier` is in-process, so on a
> multi-instance deployment a Slack reply raises the notification on whichever instance handled it — not
> necessarily the one holding the viewer's circuit. Re-reading the count on navigation always works;
> live-updating everywhere needs a backplane, which is not built.

## What happens when things are deleted

| Event | Effect on cases |
|---|---|
| The author is deleted, or leaves the team | **Cases and transcripts are kept**, and stay readable with the name they were written with. Nobody then satisfies the authorship check, so such a case is reachable through `support:read` / `support:manage` |
| The team is soft-deleted | **Cases remain readable to a caller whose team claim still names it.** See the limitation below. Restoring the team is unaffected |
| The team is **purged** | **Cases are destroyed with it**, by a purge participant |

> **One known limitation.** A soft-deleted team's cases are still readable to a caller whose `TeamKey` claim
> still names it, which is inconsistent with every other team read — all of which exclude soft-deleted teams.
> Purge is unaffected: it destroys the cases either way.
