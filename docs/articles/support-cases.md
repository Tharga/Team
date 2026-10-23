# Support cases

A signed-in member can raise a support case for their team, reply to it, and read its history. Cases are
persisted, authorized and audited.

> **What this is not, yet.** There is **no Jira link and no AI responder**. Cases work on the site, over
> Slack threads and by email, all in both directions; the rest is in
> [issue #142](https://github.com/Tharga/Team/issues/142).

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

A case belongs to a **team** — or to no team at all — and has an author, a status, and a transcript.

| | |
|---|---|
| `SupportCase` | Header: id, team, author, subject, status, timestamps, message count, channel bindings, and for mail that opened without a team the teams it may belong to |
| `SupportMessage` | One transcript entry: sequence, kind (user or system), author, body as text and as sanitized HTML, files, timestamp, and whether it reached its channels |
| `SupportChannelBinding` | A projection onto an external system: the Slack thread, the mail conversation. Carries the correspondent's address where there is one, the support mailbox it came in through, and for a mailed answer which entry it was |

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
| Read or list a case with **no team** | `support:unassigned:read` — a **system** scope |
| Reply to, close, reopen or **assign** a case with no team | `support:unassigned:manage` — a **system** scope |
| Read or list cases in **any** team, without belonging to it | `support:all:read` — a **system** scope |
| Reply to, close, reopen or hand over a case in **any** team | `support:all:manage` — a **system** scope |

**Not everything is a scope, deliberately.** Raising a case about your own team is what an ordinary member
does; gating it would mean every host granting that scope to everybody, and a scope everyone holds checks
nothing. Membership and authorship are still *checks* — they are simply not grants.

> **`support:read` is a real privilege boundary.** A support case contains whatever a user typed into it,
> which is exactly where somebody pastes a password, a token or a customer's details. Grant it as carefully
> as you grant `audit:read`.

`support:read` and `support:manage` are team scopes, registered at `Administrator` by default — see
[Taking support off the access level](#taking-support-off-the-access-level). A case with a team is always
loaded through it, so holding a valid case id from another tenant gains nothing.

> **Changed in 3.22: `support:read` no longer authorizes writing.** It used to satisfy reply, close,
> reopen, hand over and answer as well, which is not what the table above has ever said. If you granted
> only `support:read` to people who have been answering cases, grant them `support:manage` as well —
> nothing else changes, and reading, listing and marking read are unaffected.

**The unassigned scopes are system-wide, and that is not a convenience.** `support:read` is held *against a
team*, and a case with no team has nothing to hold it against — so widening the team scope to cover these
would let a member of the smallest team read everything that arrived by mail. Granting these is a decision
about the whole product, which is what a system scope is for. They are registered by
`AddThargaSupportCases`, so they appear in the scope catalogue only where support cases exist.

**An unassigned case is deny-by-default.** No grant, no access — authorship does not open one either, because
every case that has no team today arrived by mail from somebody with no account to match.

## Support staff who belong to no team

The ordinary shape of a support queue is a customer raising a case inside their own organisation and the
product's own staff answering it. Staff belong to none of the customers' teams, so no team scope can reach
those cases — which is what `support:all:read` and `support:all:manage` are for.

```csharp
// Every team's cases, for whoever runs support
var page = await supportCases.GetCasesAcrossTeamsAsync();

// How many of them are waiting on an answer
var waiting = await supportCases.GetAwaitingSupportCountAcrossTeamsAsync();
```

`SupportAcrossTeamsView` renders that queue — see [Components](#components).

Grant them through a system role, as you would any system scope:

```csharp
builder.AddThargaTeam(o =>
{
    o.ConfigureSystemRoles = roles => roles.Map("Support", SystemSupportScopes.AllRead, SystemSupportScopes.AllManage);
});
```

**Do not put staff in each customer's team instead.** It does not scale past a handful of tenants and it
lists product staff in tenant membership lists. Access simulation is not the answer either: it is a
deliberate *reduction* of access, and the audit trail would say a member acted when a member did not. A
support reply has to be attributable to the person who wrote it, and with this grant it is.

**These do not reach the unassigned queue, and the unassigned scopes do not reach a team.** The two pairs
cover different populations and neither confers the other — `GetCasesAcrossTeamsAsync` lists every team's
cases and leaves out the ones no team owns, which is `GetUnassignedCasesAsync` and its own grant.

`support:all:manage` **does not grant assignment.** Assigning decides which tenant an unassigned case and
its whole transcript become part of, so it stays with `support:unassigned:manage`.

### Answering as support

`ReplyToCaseAsync` adds to a case; `AnswerCaseAsync` answers it **from the support side**. The difference is
who the message counts as, not who wrote it: an answer takes the case out of the waiting count and is unread
for whoever raised it, even when they wrote the answer themselves, from a support page. Every support view
uses it, and so does a reply typed in a case's Slack thread. Each message carries `AsSupport` so a
transcript can show it on support's side.

`AnswerCaseAsync` needs a support grant — `support:all:manage`, `support:manage` on the case's team, or for a
case with no team `support:unassigned:manage`. Having raised the case confers nothing, or anybody could clear
their own case from the queue by answering it.

`GetMyUnreadByCaseAsync(teamKey)` returns each of the caller's cases that holds unread entries, with how many,
so a list can mark where to look.

## Taking support off the access level

By default `support:read` and `support:manage` are granted to every Owner and Administrator of every team,
because in most products support is a shared team function and an administrator should see what their team
has raised.

**Where a support conversation is personal, that default is wrong.** If a case is one member talking to
your own staff, their manager reading it is a different product from the one you are building. Turn the
level off and the scopes become grantable only deliberately:

```csharp
builder.Services.AddThargaSupportCases(o =>
{
    o.TeamScopeAccessLevel = null;   // registered, enforced, granted by no access level
});
```

`null` does not unregister anything. Both scopes still appear in the scope catalogue with their
descriptions and are enforced exactly as before; they are simply granted by no access level, refused in
tenant-defined custom roles, and left out of the scope-override pickers. Holding one then takes a
code-registered tenant role or an explicit override — a recorded decision rather than a consequence of
being an Owner.

> **Only some values differ from each other.** Owner and Administrator are granted every registered scope
> regardless of the declared minimum, so `AccessLevel.Owner` behaves exactly as the default does. The
> choices that actually differ are `Administrator`, `User`, `Viewer` and `null`. Do not use
> `AccessLevel.Custom` to mean "nobody" — it grants the scope to *every* level.

The author of a case still reaches their own without holding anything, whatever you set here.

## Letting each case decide

`TeamScopeAccessLevel` decides *who* holds the team's support scopes. `Visibility` decides *which cases*
those scopes reach:

```csharp
builder.Services.AddThargaSupportCases(o =>
{
    o.Visibility = SupportCaseVisibilityMode.PerCase;   // default: TeamWide
});
```

| Mode | A team's `support:read` / `support:manage` holder reaches |
|---|---|
| `TeamWide` (default) | every case in the team — the behaviour before this option existed |
| `PerCase` | only cases their author has shared with the team, plus their own |

Under `PerCase` a new case starts as `DefaultVisibility` (`Private` unless you say otherwise), and its
author shares it — or takes it back — at any time:

```csharp
builder.Services.AddThargaSupportCases(o =>
{
    o.Visibility = SupportCaseVisibilityMode.PerCase;
    o.DefaultVisibility = SupportCaseVisibility.Team;               // a support case is the team's
    o.AssistantDefaultVisibility = SupportCaseVisibility.Private;   // a conversation with the assistant is the asker's own
});

await supportCases.SetCaseVisibilityAsync(teamKey, caseId, SupportCaseVisibility.Team);
```

- **A conversation with the assistant starts as `AssistantDefaultVisibility`** (null uses
  `DefaultVisibility`), and **takes `DefaultVisibility` when it is handed to a person** — from then on it is a
  support case and reads as one.
- **Only the author decides.** A team manager cannot share somebody else's case, and neither can your own
  support staff: a grant to answer every case is not a grant to widen who else reads one.
- **Open or closed.** Who may read a case's history matters as much once it is over, so a closed case can
  still change.
- **Private only while nobody else in the team has written in it.** Once a colleague has joined the
  conversation it is no longer only the author's to hide; support's answers do not count as the team writing.
  `GetVisibilityChoiceAsync(teamKey, caseId)` says what the caller may choose — whether they may change it at
  all, and whether `Private` is on offer — so a component offers exactly what the service would accept.
- **Your support staff are unaffected.** `support:all:*` reaches a case whatever it says, and so does the
  author.
- **Listings and counts follow it.** `GetCasesAsync` and `GetAwaitingSupportCountAsync` return only what the
  caller may read, so a team reader is never shown a case they would be refused.
- **A case stored before this option existed has no visibility, and reads as `Private` under `PerCase`** —
  nobody ever chose to share it. Under `TeamWide` it reads exactly as before.
- Changing visibility is audited (action `visibility`) and raises `SupportCaseChange.VisibilityChanged`.

`SetCaseVisibilityAsync` is refused under `TeamWide`, where there is nothing to decide.

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

Authorized exactly as replying is: the member who raised it, a holder of `support:manage` on the team, or
a holder of `support:all:manage`. Reading the case is not enough.
It writes a system entry, clears the closure, and returns the case to `Open` — **keeping the history**, which
is the whole reason it exists rather than telling somebody to raise a second case that explains nothing.

**Reopening an already-open case does nothing and is not an error.** Two people looking at the same closed
case both press the button; the second sees an open case rather than a complaint.

**Writing to a closed case reopens it.** Whatever is written — on the site, by mail, or as a reply in its
Slack thread — continues the conversation that explains it rather than being refused, so the case opens
again first and the entry lands in it.

## Who closes a case

Closing is the customer's word that the problem is solved. The author and the team's own support readers close
whenever they like. Your own staff (`support:all:manage`) may too, unless you hold them to a window:

```csharp
builder.Services.AddThargaSupportCases(o => o.StaffCloseAfter = TimeSpan.FromDays(14));   // default: null
```

With a window set, staff close a case only once support answered last and the customer has said nothing for
that long; otherwise the service refuses. `SupportAcrossTeamsView` takes the same value as a parameter, so it
does not offer Close where the service would refuse it. Without a window, the view still asks first, and says
it is better that the team closes the case themselves.

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

Four components ship in `Tharga.Team.Blazor`, and **all are optional**:

```razor
@* What a team member sees: raise a case, read the conversation, reply, reopen *@
<SupportCasesView ShowSubject="false" />

@* What support sees: every case in the team, answer, close, reopen *@
<SupportQueueView />

@* Cases that belong to no team: answer them, and assign a team when you know which it is *@
<SupportUnassignedView />

@* Your own support staff: every team's cases, each naming its team, answered in place *@
<SupportAcrossTeamsView />
```

`SupportAcrossTeamsView` needs `support:all:read` (or `:manage` to answer) and, like
`SupportUnassignedView`, renders a note rather than throwing when the caller holds neither. It names each
case's team through `ITeamOversightService` when the caller can list teams, and shows the team key when
they cannot.

**`SupportCasesView`** has two tabs — the cases first, then where help is asked for. `Tab` names the one to
show (`SupportCasesView.CasesTab` or `NewCaseTab`) and `TabChanged` reports a change, so a page can keep its
address in step and link straight to either. Cases are expandable cards; each conversation loads when its card
opens. Every case list shares one filter — status and kind of conversation, open support cases by default —
remembered in the browser under `StorageKey`, and every list reads every page of the listing rather than the
first. `SupportCaseFilter` also takes `ShowSearch` — a search over the subject, who wrote, the case id, the team
and suggested teams, the address a case corresponds with and the support address it came in to, which is not
remembered — and `ShowKinds`, to leave the kind out where no assistant answers.

**`SupportUnassignedView`** reads like the cases view: each case an expandable card with the sender, status,
*Needs an answer* and last activity in its header, whose conversation is read only while it is open; waiting
cases first, then the newest activity, with the count awaiting an answer beside the filter (with search, and
without kinds, since no assistant answers a case without a team). It takes `TimeTemplate` and `StorageKey`
(default `Tharga.Team.Support.Unassigned.Filter`), and `RefreshAsync()` for a host that relays changes from other
instances over its own backplane. **It carries no heading**; `SupportUnassignedViewText.Title` is there for a
host that labels the tab or card it sits in.

Every answer box in these views takes formatting ([`RichTextEditor`](#formatting)) and files
([`SupportAttachmentPicker`](#attachments)), and every staff view shows the support address a case came in to
and whether each answer was delivered. Above each support answer box, `SupportAnswerRouteLine` says **where the
answer goes** — the address it is mailed to and the support address it is sent from, or that it is shown on the
site — and beside the button that answers, **Preview** opens the mail as the person who raised the case would
receive it; see [Where the answer goes, and its preview](#where-the-answer-goes-and-its-preview).

**Closing asks first.** The team's dialog offers who may read the case from now on, under `PerCase`, with
`Private` offered only where `GetVisibilityChoiceAsync` allows it; the staff dialog says it is better left to the
team. A conversation with the assistant is neither open nor closed, so it has nothing to close and shows
what it is instead of a status.

**An answer is marked read only once somebody is looking.** An open conversation on a page in a background
tab, or in a window without focus, leaves the answer unread — and every count saying so — until the reader
comes back. `SupportAttention` exposes the same check to a host's own components.

**`SupportAcrossTeamsView`** marks what needs an answer and lists it first, shows each case's team with its
icon (through `IIconResolver`), and shows who may read a private case.

`SupportQueueView` needs `support:read`, and the service refuses without it — so gate rendering on the scope
to spare a member a refusal they can do nothing about.

`SupportUnassignedView` needs the **system** scopes instead, and behaves differently in two ways worth
knowing before you place it:

- **It renders a note instead of throwing** when the caller cannot read the queue, because it is a panel
  beside content they *can* see. `SupportQueueView` lets the refusal propagate, because there it is the page.
- **Its assign control lists teams through `ITeamOversightService`**, which needs `teams:read`. An operator
  holding the unassigned scopes but not that one can still answer and close; they simply have nothing to
  assign to, and the component says so. **This is deliberate rather than a limitation:** nothing validates a
  team key on the way through, so a free-text field would let somebody assign a case to a team that does not
  exist — out of the queue and into nobody's.

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
| `SupportCaseLimits.MaxMessageHtmlLength` | 10,000 characters | Longer formatting is dropped and the message kept as its text |
| `SupportCaseLimits.MaxMessagesPerCase` | 500 | The transcript is embedded in one document |

Files are not in the case document; see [Attachments](#attachments) for their limits.

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
than the case itself. Neither are file names: a reply or an answer carrying files records how many
(`support.message.attachments`). Sending an entry again is audited as `resend`, with where it stands afterwards
(`support.message.delivery`).

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

The manifest asks for exactly six scopes, each required by code or by Slack:

| Scope | Used by | For |
|---|---|---|
| `chat:write` | `chat.postMessage` | posting notifications and case replies |
| `users:read` | `users.getPresence` | whether anybody on support is active |
| `channels:read` | `conversations.members` | who is on a public support channel |
| `groups:read` | `conversations.members` | the same, for a private channel |
| `channels:history` | the `message.channels` event | receiving replies typed in a public support channel's threads |
| `groups:history` | the `message.groups` event | the same, for a private channel |

**Without the history scope Slack will not let you subscribe to the message event**, so replies typed in
Slack never arrive. An app created from an older manifest needs the scope added and the app reinstalled.

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
| `Failed` | The channel refused it, was unreachable, or the mail bounced — with the reason in `DeliveryError` |

**A channel being down never blocks a case.** The case is written first and is authoritative; projecting it
comes after. If Slack refuses, the case still exists and the entry is `Pending`.

**"Not delivered" says why.** A recipient the server refused, a bounce saying the sending domain failed SPF and
the outbound policy withholding mail outside production are three different fixes, so the reason is kept on the
entry. A channel of your own reports it by overriding `ISupportChannel.PostWithResultAsync`; the default wraps
`PostAsync` and reports no reason. A bounce that names a mailed answer marks that answer `Failed` with the
receiving server's words, and raises `SupportCaseChange.DeliveryChanged`.

**Send it again.** `ResendAsync(teamKey, caseId, sequence)` sends a `Failed` or `Pending` entry to the case's
channels again and returns where it stands; any other entry is left alone, so pressing the button twice sends
nothing twice. It is authorized as answering is and audited as `resend` with `support.message.delivery`. The
support views show *Not sent yet*, *Not delivered* with the reason, and **Send again** under such an answer.

## Email

A case can arrive by mail, and be answered by mail. **IMAP in, SMTP out, against your own mailbox** — no
provider account, no public endpoint, no webhook to expose.

```csharp
builder.Services.AddThargaSupportCases(o =>
{
    o.Email.Imap.Host = builder.Configuration["Support:Mail:Imap:Host"];
    o.Email.Imap.UserName = builder.Configuration["Support:Mail:Imap:UserName"];
    o.Email.Imap.Password = builder.Configuration["Support:Mail:Imap:Password"];
    o.Email.Smtp.Host = builder.Configuration["Support:Mail:Smtp:Host"];
    o.Email.Smtp.UserName = builder.Configuration["Support:Mail:Smtp:UserName"];
    o.Email.Smtp.Password = builder.Configuration["Support:Mail:Smtp:Password"];

    o.Email.FromAddress = "support@example.com";   // where replies come back to
});
```

**Leave the hosts unset and nothing is registered** — no channel, no poller. IMAP alone and SMTP alone are
both real configurations: read the mailbox without answering by mail, or answer by mail while questions come
in on the site.

### Email is the customer's channel; Slack is support's

A person is answered the way they made contact. Somebody who wrote to `support@` gets a mail back; somebody
who typed into the site reads the answer on the site. Support works in the back office or in a Slack thread
and never touches mail in either direction.

That is why **a case raised on the site opens no mail projection**: the person who raised it is already
looking at the page. An email projection is opened by mail *arriving*.

Both channels are live at once, so a case that arrived by mail can hold an email binding facing the customer
and a Slack binding facing support, and an answer reaches both.

### Which team does an emailed case belong to?

**Only the one the sender provably belongs to, and otherwise none until somebody says.** A `From:` header does
not say which tenant a problem concerns, and every way of guessing — a fallback team, a configured default — is
the guess that puts one customer's problem in another customer's list.

**A mail opens in a team only when all of this holds:**

- **The sender is verified.** Every mail read is checked by DKIM: a signature that verifies, signs `From` and the
  whole body, and whose domain is the `From` address's domain or a parent of it. The key is looked up in DNS,
  cached for ten minutes (a miss for one) and cut off after five seconds; anything that goes wrong means *not
  verified*, never an error. The result is `InboundMail.SenderVerified` and `SenderDomain`.
- **Exactly one user account holds the address**, compared trimmed and ignoring case.
- **That account is an active member of exactly one team**: accepted, not suspended, with an access level, the
  account enabled. A membership with no access level grants nothing since 4.0, so mail neither opens there nor
  suggests it.

Then the case opens in that team with that user as its author, exactly as if they had raised it on the site.

**Anything short of that opens unassigned**, with the teams the address matched in `SupportCase.SuggestedTeams`
and why they are only suggestions in `SuggestionReason`:

| `SupportTeamSuggestionReason` | |
|---|---|
| `SeveralTeams` | The sender is verified and an active member of more than one team |
| `SenderNotVerified` | The address belongs to members of these teams, but the mail could not be verified as coming from it |
| `SharedAddress` | More than one user account holds the address |

An unknown sender opens unassigned with no suggestions. `SupportUnassignedView` shows the suggestions with their
reason and assigns to one in a click; that goes through `AssignCaseAsync` like any other assignment.

**Verified vouches for the domain's mail system, not for a person.** A large mail provider signs only what its own
account holder sent, so there it amounts to the address; a company domain signs whatever its servers relay, so it
is only as strict as that company is about who may send as whom.

**The address compared is the one on the user record**, which comes from the sign-in provider's e-mail claim.
Who a sender is comes from `ISupportSenderDirectory`, which `AddThargaTeamRepository` registers when it has both
the user and the team repository. If your sign-in provider issues addresses it has not verified, register one of
your own that finds nobody before it, and every mail opens unassigned without suggestions, as it did before 4.2.

An unassigned case is **unattributed**: no team, no author identity, the sender's address as the display name. An
operator holding `support:unassigned:manage` assigns a team when they know which it is, and **leaving it
unassigned forever is a supported state, not a backlog.** The assignment is recorded in the transcript, because
which tenant a case belongs to is part of its history.

Assignment is conditional: two operators triaging the same queue cannot both assign a case, and
`AssignCaseAsync` returns `false` to the one who lost rather than silently doing nothing.

### The trust rule

**A `From:` header authenticates nobody.** So:

- Mail that matches an existing case is accepted **only from the address that case already corresponds with**.
  Anyone else is dropped and logged — otherwise learning a thread id would let a stranger write into a
  transcript a real person reads and trusts.
- Mail that matches no case opens a new one, which is the only thing an unknown sender can do.
- Automated mail — vacation responders, bounces, `Precedence: bulk` — is never acted on. Answering one is how
  a support mailbox and an out-of-office reply fill each other overnight.
- Mail this instance sent is never read back as a reply.

> **A support mailbox collects spam, and some of it becomes cases.** Mail from a person to an address that
> accepts mail from anyone is a case by definition. The recipient filter narrows it and the checks above drop
> the machinery, but rate limiting is yours.

### One mailbox, two sites

```csharp
o.Email.Recipients = ["example.com"];       // this instance answers for this domain only
```

A bare domain matches any local part; a full address matches only itself; matching is case-insensitive and
plus-addressing is stripped, so a per-case reply-to is not rejected. Unset accepts everything.

Three things make sharing a mailbox actually safe, and each of them is load-bearing:

- **Nothing is ever flagged or moved.** The mailbox is opened read-only, so a fetch cannot set `\Seen` as a
  side effect. A flag meaning "handled" to one instance hides the message from the other.
- **The filter runs before deduplication.** Recording a message id and *then* discarding the mail would leave
  the instance that wanted it seeing a duplicate and concluding somebody had handled it. Nobody had.
- **Each instance keeps its own read position**, keyed on the recipients it answers for. Sharing a position
  would let the first instance to poll advance past a message addressed to the second.

### Reading the mailbox

A background service polls on `o.Email.PollInterval` (default one minute) and applies what arrives. There is
no endpoint to map and nothing to verify — the cost is latency bounded by the interval.

The read position is a **bookmark, not a record**: it is stored by `ISupportMailPositionStore`
(`AddThargaTeamRepository` provides the MongoDB one), written only after mail is handled, and losing it costs
a re-read rather than duplicate cases — the event ledger recognises everything already applied. Without a
store registered the poller keeps its position in memory and says so once at startup.

A mailbox that reports a new UID generation is re-read from the start, for the same reason: the stored UIDs
now refer to different messages, so trusting them is the one thing that could lose mail.

**Connecting a mailbox that already holds mail?** Set `FirstReadSince` to a date, and a mailbox with no read
position starts there instead of turning every message it ever received into a case. It applies whenever reading
starts from the beginning, including after a new UID generation.

A mailbox that cannot be read is logged with its name and the server's reason (`MailFetchResult.Error`, never the
password), and tried again on the next interval.

### What is trimmed

Quoted history and signatures are cut on unambiguous markers only, and the whole body is kept when nothing is
recognised — reading too much is untidy, cutting too much silently loses what somebody wrote. A mail's HTML is
kept, sanitized and cut at the same point — see [Formatting](#formatting) — and its files are stored — see
[Attachments](#attachments).

Mail longer than `MaxMessageLength` is **trimmed rather than refused**, unlike a reply typed into a form:
there is nobody to tell, and the message id has already been recorded, so refusing would discard what a
customer sent and never ask again.

### The thread

**An answer carries the conversation.** It is sent `In-Reply-To` the newest mail of the case, with every earlier
`Message-ID` in `References`, and quotes the conversation so far, newest first, under the answer — so the
customer's mail program threads it, and whoever reads it on a phone sees what it answers.

**Every message id is recorded** — the mail that opened the case, every reply, every answer sent — so a reply to
any of them finds its case, whichever one the customer's mail program chose to answer.

**Outside production, a reply from the override address counts as the customer's.** The outbound policy sends
every answer to the override address, so the person testing answers from there; that reply is accepted on the
case as if the customer had written it.

### How an answer speaks for support

**An answer mail names no staff member.** It starts with the answer itself, with no author heading; the host's
signature follows it, before the conversation it quotes, the way a mail program places a signature; and every
earlier entry written by support — a person answering from the support side, or the assistant — is attributed to
**support's name**, never the person. The customer's own entries keep the name they were written under.

```csharp
builder.Services.AddThargaSupportCases(o =>
{
    o.Answers.SupportName = "Example Support";                  // default: the text key team.support.mail.supportName, "Support"
    o.Answers.Signature = ["Kind regards,", "Example Support"];  // default: none
    o.Answers.TimeZoneId = "Europe/Stockholm";                  // default: UTC
});
```

- **The signature** is plain text lines: in the text body after a blank line, and in the HTML one paragraph,
  `<p class="signature">`, its lines encoded and joined by `<br>`, so a template can style it. Without one nothing
  is added.
- **Each quoted entry** is introduced by one translatable line, `team.support.mail.wrote` — "On {0}, {1} wrote:",
  `{0}` when and `{1}` who. The time is `yyyy-MM-dd HH:mm` in `TimeZoneId`, followed by `UTC` only when that is UTC.
  An unknown zone is refused at registration.
- **A frame of its own.** With `MailTemplateOptions.SupportHtml` set, answers and their previews go out in it rather
  than in the template for mail to users — see [the mail template](user-mail.md#the-mail-template). Its
  `{{footer}}` is always empty.
- **The language is the culture of whoever answers**, as the request runs in it; there is no per-recipient
  language yet.

### Where the answer goes, and its preview

Staff answering a mailed case see **the address the answer is mailed to** and **the support address it is sent
from** — not only the author's name, which for a sender nobody knows is "Unknown". A case with no mail thread,
raised on the site, says the answer is shown there instead. Outside production, when the
[outbound mail policy](implementation-guide.md#mail-outside-production) would not deliver to that address, it says
so plainly: *In this environment the answer is redirected to {override}*, or *…is not sent*. In production, or
for an address in an allowed domain, nothing more is added.

```csharp
var route = await cases.GetAnswerRouteAsync(teamKey, caseId);          // null for no such case
var preview = await cases.PreviewAnswerAsync(teamKey, caseId, body: null, bodyHtml: draft);  // null when not mailed
```

- **`GetAnswerRouteAsync`** returns a `SupportAnswerRoute`: `ByMail`, `To`, `From` and `Mailbox` (the mailbox the
  case came in through, or the primary when that one is no longer configured), and `Effect` —
  `MailPolicyEffect.None`, `Redirect` with `RedirectedTo`, or `Withhold` — from `IOutboundMailPolicy.Decide`.
  `SupportAnswerRoute.Site` is a case answered on the site: one with no mail thread, or a host that mails nothing.
- **`PreviewAnswerAsync`** returns the draft as a `MailPreview` — `From`, `To`, `ReplyTo` (the per-case address
  when the mailbox uses one), `Subject`, the `Html` in the host's [mail template](user-mail.md#the-mail-template) —
  its support frame when it has one — with the signature and the conversation so far quoted, and the `Text`
  alternative — **composed by the code that sends the answer**,
  without sending or storing anything. The draft is sanitized and derived exactly as an answer is, and written as
  by the caller. Files are not rendered; the dialog names them.
- **Both are authorized exactly as answering**, since they show the correspondent's address to whoever may write to
  it; neither is audited, as reads are not — the answer is, when it is sent.

The components show them: `SupportAnswerRouteLine` (`Route`) and `MailPreviewDialog` (in `Tharga.Team.Blazor`'s
framework, `Preview`, `FileNames`), which renders the mail in an `iframe` given the HTML as `srcdoc` and sandboxed
with no permissions, so the template's styles stay inside it and nothing in it runs. A reader who may not answer
is shown no route and no preview. Their texts are `team.support.answerRoute.*` and `team.mailPreview.*`.

### Several mailboxes

`o.Email` is the **primary** mailbox. Add more with `AddMailbox`, each with a whole `MailOptions` of its own — its
own IMAP and SMTP account, sending address, folder, interval, recipient filter, `PerCaseReplyTo` and file limits:

```csharp
builder.Services.AddThargaSupportCases(o =>
{
    o.Email.Imap.Host = "imap.example.com";            // the primary, as before
    o.Email.FromAddress = "support@example.com";

    o.AddMailbox("billing", m =>
    {
        m.Imap.Host = "imap.example.com";
        m.Smtp.Host = "smtp.example.com";
        m.FromAddress = "billing@example.com";
    });

    // Or one per configuration section: Support:Mailboxes:{name}
    foreach (var section in builder.Configuration.GetSection("Support:Mailboxes").GetChildren())
        o.AddMailbox(section.Key, m => section.Bind(m));
});
```

- **The name is a stable key stored on every case** that comes in through the mailbox. It must be non-empty and
  unique ignoring case, and `primary` is reserved: the primary is stored under that name, and its read position
  is unchanged, so there is nothing to migrate. Renaming a mailbox re-reads it from the start and sends its old
  cases from the primary.
- **Each mailbox is read on its own**, by its own loop at its own interval, from its own position. One failing is
  logged by name and leaves the others reading.
- **The same mail in two mailboxes opens one case.** One ledger covers every mailbox, so a mail sent to both
  addresses becomes one case, bound to whichever mailbox read it first. Mail from any of our own addresses is
  never read back as a customer's.
- **A case is answered from the mailbox it came in through** — its account, its sending address and its reply
  address rules, behind the same outbound policy. A case whose mailbox is no longer configured is answered from
  the primary, with a warning naming it. A mailbox that reads but cannot send leaves its cases' answers *not
  delivered* rather than borrowing another mailbox's account.
- **Staff see which address a case came in to**, in the unassigned, across-teams and team queues
  (`SupportMailboxAddress`, "to {0}"), and the filter's search matches it.
- **A hosted mailbox works as any other**, over IMAP and SMTP — for example with an app password where the
  provider requires one.

Cases from before 4.2 show no support address and are answered from the primary.

### Health

The poller logs a failed read and tries again, and an answer that cannot go out is recorded on its case. Neither
tells anyone watching the service, so a changed password or a locked account shows as mail to support that simply
stops arriving. `ISupportMailboxHealth.CheckAsync` checks every mailbox that has an IMAP or SMTP server set, the
primary first, and returns one `SupportMailboxHealth` for each:

- `Name` — `primary`, or the name the mailbox was added under.
- `Reading` — a `MailboxHealth` (`IsConfigured`, `IsReachable`, `Error`) for the IMAP account: it connects, signs in
  and opens the folder read-only. Not configured for a mailbox that only sends.
- `Sending` — the same for the SMTP server: it connects and signs in, and sends nothing. Not configured for a mailbox
  that only reads.
- `IsHealthy` — everything that is configured works.

It never throws, checks the mailboxes at the same time, and returns an empty list when no mailbox is configured. It
is registered with `AddThargaSupportCases` whatever is configured.

```csharp
builder.Services.AddHealthChecks().AddCheck<SupportMailboxesCheck>("support-mail");

public class SupportMailboxesCheck(ISupportMailboxHealth mailboxes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var failing = (await mailboxes.CheckAsync(cancellationToken)).Where(x => !x.IsHealthy).Select(x => x.Name).ToArray();

        return failing.Length == 0 ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded($"Support mailboxes failing: {string.Join(", ", failing)}");
    }
}
```

Each check opens a connection to every server, so let the health endpoint cache its result rather than call this on
every probe. `Error` carries the server's own message, which can name hosts and accounts: keep it off an endpoint
anonymous callers can read.

### Bounces

A mail that cannot be delivered comes back to the address it was sent from as a delivery status notification.
When it names a support answer, that answer becomes **not delivered**, with the receiving server's reason; a bounce
never opens a case. A bounce that names no support answer is handed to every registered `IMailBounceHandler` until
one recognises it — that is how [mail to users](user-mail.md#bounces) sent from a support address is marked
bounced. One that nobody recognises is logged at Debug, without addresses.

### Invitation mail is separate, deliberately

`ThargaTeamOptions.Email` configures invitation delivery and nothing else. It is not a fallback for support
mail and support mail is not a fallback for it: each sends from the address it is configured with, and every one
of them should be an address a reply reaches, because a no-reply address loses every reply. Bind both from the
same configuration section if you want one mailbox. With [mail to users](user-mail.md#invitations) registered,
invitations go through its outbox and its address instead.

## Attachments

A support message carries files both ways: what a customer's mail brought, and what support or the customer
attaches to an answer or a reply.

- **In:** a mail's attachments, the images shown inside its body (with their Content-ID), and forwarded mail as
  an `.eml` file. Signatures carry none, and neither does a bounce.
- **Out:** an answer's files are mailed with it; an inline image is mailed beside the HTML that shows it.
- **Kept outside the case**, through `ISupportAttachmentStore`, keyed by case. `AddThargaTeamRepository` registers
  one over a GridFS bucket (`SupportAttachment`, `ThargaTeamOptions.SupportAttachmentBucketName`); register your
  own before it to keep files in blob storage. Without a store, a mail's files are named in its message as not
  stored, and a reply carrying files is refused.
- **Limits apply both ways**, per mailbox, on `MailOptions`: `MaxAttachmentBytes` (25 MB a file),
  `MaxAttachmentTotalBytes` (50 MB a message) and `MaxAttachmentCount` (20). A file over a limit, or one the store
  refuses, is left out with a note in the message, and the mail is kept. An answer is held to the strictest
  limits of the mailboxes that can send.
- **File names are sanitized** — no path, no control characters, never empty — and the declared content type is
  kept as data, never trusted.

```csharp
await cases.AnswerCaseAsync(teamKey, caseId, "Here is the report.",
    [new SupportAttachmentUpload { FileName = "report.pdf", ContentType = "application/pdf", Content = stream }]);

await using var file = await cases.OpenAttachmentAsync(teamKey, caseId, sequence, attachmentId);
```

`ReplyToCaseAsync` and `AnswerCaseAsync` take files in their overloads, authorized exactly as without them; the
upload is measured while it is stored and every file of a refused message is discarded again. `OpenAttachmentAsync`
is authorized **exactly as reading the transcript** and returns null for an id that entry does not carry; you
dispose what it returns. `GetAttachmentLimitsAsync()` says what an answer may carry, so a form offers what the
service accepts.

**Serve a file as `SupportAttachmentContentTypes.ServedAs` says, never as its declared type.** A file opened from
your application runs in its origin, so only PDF, PNG, JPEG, GIF, WebP and plain text may open in the browser;
everything else is served as `application/octet-stream` and saved. The transcript does exactly this.

Reading a file is not audited; a reply or answer records how many files it carried
(`support.message.attachments`), never their names. Purging a team removes its files first.

`SupportAttachmentPicker` is the file chooser the answer boxes use: `Files`/`FilesChanged`, `Disabled`, and
`Limits` (the support service's when not set).

## Formatting

Messages carry formatting: answers are written in a small rich-text editor, a mail's HTML is kept, and both are
shown formatted — through **one sanitizer**, on every way in and again at every render.

- **`SupportMessage.Body` is always the plain text**, which every reader can use: Slack, the notification, the
  mail's text part. **`BodyHtml` is the sanitized HTML beside it**, or null for a plain message. Formatting that
  is only paragraphs, or longer than `SupportCaseLimits.MaxMessageHtmlLength` (10 000), is not kept; the message
  is kept as its text.
- **The allow-list** (`SafeHtml`, in `Tharga.Team.Service`): paragraphs, line breaks, headings to `h4`, bold,
  italic, underline, strike-through, lists, quotes, preformatted text and code, tables, links and images.
  Links are http, https or mailto only, and open in a new window with `noopener noreferrer`. Images are shown
  only by `cid:` from the message's own files, or from data the caller read — a remote image becomes its alt
  text, so a mail cannot track who opened it. Scripts, styles, forms, frames, media and hidden text are removed
  with their content.
- **Inbound HTML is cut at the quoted thread** — on the markers the common mail programs write — and dropped
  when the text lost a quote the HTML did not, when it carries no formatting, or when it is too long.
- **Inline images show in place** in the transcript: PNG, JPEG, GIF and WebP up to 2 MB, read through
  `OpenAttachmentAsync`. An image shown in place is not listed again among the files; one that cannot be shown is.
- **A formatted answer is mailed as HTML** beside the same text as before (`multipart/alternative`), with the
  earlier conversation in quotes. With a [mail template](user-mail.md#the-mail-template) it is framed in it, and a
  plain answer is mailed as HTML too — its escaped text, the conversation in quotes — beside the same text.

```csharp
await cases.AnswerCaseAsync(teamKey, caseId, body: null, bodyHtml: "<p>Fixed in <b>2.4</b>.</p>", attachments: []);
```

The HTML overloads of `ReplyToCaseAsync` and `AnswerCaseAsync` sanitize on the server, turn images into their alt
text, and derive the text from the HTML; `body` is used only when the HTML holds nothing readable.

For your own pages: **`RichTextEditor`** (`Value`/`ValueChanged`, `Disabled`, `Label`, `Height`, `ShowHeadings`) writes
HTML and sanitizes what is pasted into it, and **`RichTextView`** (`Html`, `Images`, `Class`) is the only way the toolkit
renders markup — it sanitizes whatever it is given, so a stored value is never shown as stored. `SafeHtml` and
`HtmlImage` are public for server code, and `HtmlText` in `Tharga.Team.Email` renders HTML as the plain text
the toolkit uses.

`ShowHeadings="true"` adds a text style picker to the editor's toolbar with **Paragraph**, **Heading** (`h2`) and
**Subheading** (`h3`), for text that is a structured document rather than a short reply. It is off by default, so
the answer boxes above keep their small toolbar. The picker offers only styles `SafeHtml` keeps as they are, and
its labels go through `IThargaTextProvider` under `team.richText.textStyle`, `team.richText.paragraph`,
`team.richText.heading` and `team.richText.subheading`. Text that already holds headings shows them whether or not
the picker is on.

## An assistant answering first

A case can be answered by an assistant before, or instead of, a person. The customer chooses when they raise
it, and can ask for a person at any point without losing the conversation.

### Any model, because the toolkit does not choose one

The toolkit consumes **`IChatClient`** — Microsoft's abstraction from `Microsoft.Extensions.AI` — and nothing
else. It ships no provider adapter, references no vendor SDK, and names no model. Which model answers your
customers is one registration in your own host:

```csharp
// Ollama, llama.cpp, a self-hosted model, OpenAI, Anthropic — all the same shape.
builder.Services.AddSingleton<IChatClient>(_ => new OllamaApiClient(uri, "llama3")
    .AsBuilder()
    .UseFunctionInvocation()
    .Build());
```

**Register nothing and there is no assistant.** No responder resolves, the choice is never offered on the
new-case form, and every case is answered by a person exactly as before. That is the default, and it is not a
degraded state — it is what a host that never wanted one gets.

This is also why provider-specific capability is not lost by the toolkit being neutral. Prompt caching,
effort levels, token counting and the rest belong to the client you construct, so they stay available to you:
you own the object the toolkit calls.

### What the customer sees

The new-case form offers the assistant, unticked, only when one is registered. A case being answered by an
assistant offers **Talk to a person**, which stops the assistant and leaves the same case, with the whole
transcript, to whoever picks it up. That transition is one-way — handing back after somebody has asked for a
human would answer a question they have already declined to have answered that way.

An answer the assistant wrote is recorded as `SupportMessageKind.Assistant` and badged in the back-office
transcript, so an agent can tell what the customer was already told by a machine from what a colleague told
them. The two carry very different weight.

### What it does to the queue

**A conversation the assistant is answering never waits for support.** It waits for the assistant, so it is in
no awaiting-an-answer count, and its answers are never unread — the customer reads them as they arrive.
Counting it would flick support's counter up and back down on every message somebody writes to the assistant.

**Once it is handed to a person it is an ordinary case**, and the customer's first message to that person puts it
in front of support like any other.

**A conversation the assistant is answering reaches no channel.** No Slack thread and no mail to support
are opened for it. Asking for a person opens nothing by itself; the customer's first message to a person
opens the channel, carrying the conversation so far, so whoever picks it up starts from what was already said. A customer who asks for a person from the
start gets the channel at once.

### It answers; it does not act

The built-in tools are reads: the caller's own cases, and one case's transcript. Closing, assigning and
reopening stay with people — an assistant that can close its own cases can close the ones it answered badly.

Every tool reads through the same scope-checked service any other caller uses, so the assistant sees exactly
what the person it is answering would see. The responder is handed the case and its transcript rather than
fetching them, so it holds nothing it could reach further with.

**Add your own tools.** The toolkit knows about teams, membership and cases; it knows nothing about the
product your customer is actually asking about. Implement `ISupportAssistantTools` to replace the built-in
set, or resolve the built-in one and add to what it returns. Keep every tool reading through a scope-checked
service — a tool that reaches the store directly is a second enforcement point, and the assistant would be
the one caller able to use it.

### Running it

Raising and replying stay fast; asking for the answer is a separate call, because a model can take tens of
seconds and burying that inside a write makes reporting a problem feel broken.

```csharp
var raised = await cases.RaiseCaseAsync(teamKey, subject, body, SupportAssistance.Assistant);

await cases.RunAssistantAsync(teamKey, raised.Id);   // returns false when there is nothing to do
```

`RunAssistantAsync` is safe to call unconditionally: it does nothing when the case has no assistant, when the
customer has asked for a person, or when the host registered no responder. The shipped components already
call it, so a host using `<SupportCasesView />` needs none of this.

### Settings

```csharp
builder.Services.AddThargaSupportCases(o =>
{
    o.Assistant.Instructions = "You answer support questions for Contoso Archive…";
    o.Assistant.Timeout = TimeSpan.FromSeconds(60);
    o.Assistant.TranscriptLimit = 20;
});
```

Configuring these does not turn an assistant on — registering an `IChatClient` does. There is deliberately no
model, endpoint or key here.

**Replace `Instructions`.** The default says only what is true of every host: answer from the tools, and say
so rather than guess. A model that knows your product answers very differently from one that does not.

A model that times out, returns nothing, or cannot be reached **declines**, and a decline is an ordinary
outcome rather than an error: the case stays waiting for a person, which is what the customer would have had
anyway. A provider being down never stops somebody reporting a problem.

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
| `GetMyUnreadCountAsync(teamKey)` | my own cases holding an answer I have not read | membership |
| `GetMyUnreadByCaseAsync(teamKey)` | the same, per case, with how many | membership |
| `GetMyOpenCountAsync(teamKey)` | my own open cases | membership |
| `GetMyAnsweredCountAsync(teamKey)` | my own open cases support answered last, read or not | membership |
| `GetAwaitingSupportCountAsync(teamKey)` | open cases whose newest entry came from the person who raised them | `support:read` |
| `GetAwaitingSupportCountAcrossTeamsAsync()` | the same, in every team | `support:all:read` |
| `GetAwaitingUnassignedCountAsync()` | the same, in the cases no team owns | `support:unassigned:read` |
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

**Only an answer is unread** — an entry by somebody else, or by support. A note that the case closed or
reopened is not, so closing a case never leaves a light on for a case the list no longer shows. The open and
answered counts are what a host shows once nothing is unread: the cases still going on, and those waiting on
the customer to reply or close them. All of them leave conversations with the assistant aside.

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

### Escalating from your own worker

The counts above answer "how many, in this team". A worker that escalates needs a different question
answered: *which cases, in any team, have waited too long?* Two store queries answer it (3.24,
Tharga/Team#293):

| Method on `ISupportCaseStore` | Returns open cases, across every team, where… |
|---|---|
| `GetCasesAwaitingSupportSinceAsync(waitingSince, limit)` | the newest entry is from the person who raised the case, and older than `waitingSince` |
| `GetCasesWithUnreadAnswerSinceAsync(answeredBefore, limit)` | the newest entry is an answer, older than `answeredBefore`, and its author has not read it |

An answer is an entry by a person other than the author, or by the assistant. A toolkit note (a reopen or
an assignment) is never an answer, and a case whose newest entry is one is not returned. Unassigned cases
are included in both queries.

```csharp
public class SupportEscalationWorker(ISupportCaseStore store, ISupportEventLedger ledger, IMyMailer mail)
{
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var unanswered = await store.GetCasesAwaitingSupportSinceAsync(DateTime.UtcNow.AddMinutes(-1), 50, cancellationToken);

        foreach (var supportCase in unanswered)
        {
            // One escalation per newest entry: the pair names exactly one, so a later message escalates again.
            if (!await ledger.TryRecordAsync("escalation", $"{supportCase.Id}:{supportCase.MessageCount}", cancellationToken)) continue;

            await mail.NotifySupportAsync(supportCase);
        }
    }
}
```

**A matching case keeps matching, so deduplication is yours.** Neither query changes what it reads. Key
"already handled" on `Id` plus `MessageCount`, as above. `ISupportEventLedger` already does this atomically
across instances.

**Results are newest first.** A case you have already handled stays in the results, so with a `limit` and
no ordering a pass could return the same handled cases every time. Newest first puts the cases that have
only just crossed the window on top.

**These are framework reads, not user-facing ones.** They name no team and check no scope, like the
inactivity sweep. Call them from a background worker and never from a page, controller or MCP provider.
A custom store that does not implement them returns nothing, so the worker simply never escalates.

## What happens when things are deleted

| Event | Effect on cases |
|---|---|
| The author is deleted, or leaves the team | **Cases and transcripts are kept**, and stay readable with the name they were written with. Nobody then satisfies the authorship check, so such a case is reachable through `support:read` / `support:manage` |
| The team is soft-deleted | **Cases remain readable to a caller whose team claim still names it.** See the limitation below. Restoring the team is unaffected |
| The team is **purged** | **Cases are destroyed with it**, by a purge participant — their files first |

> **One known limitation.** A soft-deleted team's cases are still readable to a caller whose `TeamKey` claim
> still names it, which is inconsistent with every other team read — all of which exclude soft-deleted teams.
> Purge is unaffected: it destroys the cases either way.

## Over REST

The toolkit ships no REST endpoints for support cases; a host with its own surface calls `ISupportCaseService` and
nothing else. Since 4.2 that surface needs a few more endpoints to keep up with the components: answering and
replying with files and an optional `bodyHtml`, downloading a file, sending an answer again, and where an answer
goes and what it will look like.

```csharp
[HttpPost("{teamKey}/cases/{caseId}/answers")]
public async Task AnswerAsync(string teamKey, string caseId, [FromForm] string body, [FromForm] string bodyHtml, [FromForm] IFormFileCollection files)
{
    var uploads = files.Select(f => new SupportAttachmentUpload { FileName = f.FileName, ContentType = f.ContentType, Content = f.OpenReadStream() }).ToArray();
    try
    {
        await cases.AnswerCaseAsync(teamKey, caseId, body, bodyHtml, uploads, HttpContext.RequestAborted);
    }
    finally
    {
        foreach (var upload in uploads) await upload.Content.DisposeAsync();
    }
}

[HttpGet("{teamKey}/cases/{caseId}/messages/{sequence:int}/attachments/{attachmentId}")]
public async Task<IActionResult> AttachmentAsync(string teamKey, string caseId, int sequence, string attachmentId)
{
    var file = await cases.OpenAttachmentAsync(teamKey, caseId, sequence, attachmentId, HttpContext.RequestAborted);
    if (file == null) return NotFound();

    Response.Headers["X-Content-Type-Options"] = "nosniff";
    var type = SupportAttachmentContentTypes.ServedAs(file.Attachment.ContentType);

    return SupportAttachmentContentTypes.OpensInBrowser(file.Attachment.ContentType)
        ? File(file.Content, type)
        : File(file.Content, type, file.Attachment.FileName);
}

[HttpPost("{teamKey}/cases/{caseId}/messages/{sequence:int}/resend")]
public Task<SupportMessageDelivery> ResendAsync(string teamKey, string caseId, int sequence)
    => cases.ResendAsync(teamKey, caseId, sequence, HttpContext.RequestAborted);

[HttpGet("{teamKey}/cases/{caseId}/answer-route")]
public Task<SupportAnswerRoute> AnswerRouteAsync(string teamKey, string caseId)
    => cases.GetAnswerRouteAsync(teamKey, caseId, HttpContext.RequestAborted);

[HttpPost("{teamKey}/cases/{caseId}/answers/preview")]
public Task<MailPreview> PreviewAnswerAsync(string teamKey, string caseId, [FromForm] string body, [FromForm] string bodyHtml)
    => cases.PreviewAnswerAsync(teamKey, caseId, body, bodyHtml, HttpContext.RequestAborted);
```

A client showing `MailPreview.Html` shows it isolated — a sandboxed frame without scripts — never as markup in its
own page: it carries the host template's styles.

A case's `SuggestedTeams`, `SuggestionReason` and its bindings' `MailboxAddress`, and a message's `BodyHtml`,
`Attachments` and `DeliveryError`, are worth exposing in what you return. `BodyHtml` is sanitized already; a client
showing it should still sanitize it again, as `RichTextView` does.

## Moving to 4.2

Coming from 3.x, follow *Migrating to 4.0* in the [implementation guide](implementation-guide.md) first.

> [!IMPORTANT]
> **Mail from a known sender can now open in their team.** Before 4.2 every mail opened unassigned. Now a mail
> whose DKIM-verified sender is the one account holding the address, active in exactly one team, opens in that
> team with that user as its author, and anything short of that opens unassigned with suggestions. To keep the
> old behaviour, register an `ISupportSenderDirectory` that finds nobody before `AddThargaTeamRepository`.
> Reading mail now looks up DKIM keys in DNS, so the host needs outbound DNS; without it nothing is verified.

> [!IMPORTANT]
> **Mail attachments are now stored**, in the GridFS bucket `SupportAttachment` in your database, up to 25 MB a
> file, 50 MB and 20 files a mail. Lower the limits on `MailOptions`, or register your own
> `ISupportAttachmentStore` before `AddThargaTeamRepository` to keep files elsewhere.

**For implementers and decorators:**

- **`ISupportCaseService` gained nine members**: `ReplyToCaseAsync` and `AnswerCaseAsync` with files, and with
  HTML and files; `OpenAttachmentAsync`, `GetAttachmentLimitsAsync`, `ResendAsync`, `GetAnswerRouteAsync` and
  `PreviewAnswerAsync`. A host implementing or decorating it adds them; the last two are authorized as answering.
- **`ISupportCaseStore.SetMessageDeliveryAsync` gained `string error = null` before the cancellation token.** A
  host's own store changes its signature and keeps the error only with `Failed`; a caller passing the token
  positionally names it.
- **`ISupportChannel.PostWithResultAsync`** is a default member; a channel of your own keeps compiling and may
  override it to say why a post failed.
- **`SupportCaseChange.DeliveryChanged` is new**, so a switch over changes handles it.

**For hosts with their own store or REST surface:**

- **`SupportMessage` gained `BodyHtml`, `DeliveryError` and `Attachments`; `SupportCase` gained `SuggestedTeams`
  and `SuggestionReason`; `SupportChannelBinding` gained `MessageSequence`, `Mailbox` and `MailboxAddress`.** A
  store of your own persists them. The built-in store reads documents without them as before; nothing to migrate.
- **`SupportMessage` and `SupportCase` now hold arrays**, so record equality between two of them compares the
  arrays by reference. Compare fields instead.
- **Add the endpoints above** — files, `bodyHtml`, download, resend, the answer's route and its preview — to keep
  up with the components. `SupportAnswerRoute`, `MailPolicyEffect` and `MailPreview` are new contracts in
  `Tharga.Team`.

**Behaviour:**

- **A formatted answer is mailed as HTML** beside the same text, and a mail's HTML is kept, sanitized; the shipped
  answer boxes call the HTML overloads, so a test verifying their calls updates them.
- **An answer carries the whole thread and quotes the conversation**; every message id is recorded.
- **A bounce marks the answer it names as not delivered**; one naming no answer goes to the registered
  `IMailBounceHandler`s.
- **With a [mail template](user-mail.md#the-mail-template) registered, every answer is framed in it**, a plain one
  included — in `SupportHtml` when that is set. Without one, answers are mailed as before.
- **An answer names no staff member.** It no longer starts with "{author}:", earlier support entries it quotes are
  attributed to `SupportCaseOptions.Answers.SupportName`, and each quote is introduced by "On {date}, {name} wrote:"
  instead of "{date} UTC, {name}:"; the host's signature follows the answer. See
  [How an answer speaks for support](#how-an-answer-speaks-for-support). A test matching the old text updates.
- **The support answer boxes** — in `SupportUnassignedView`, `SupportAcrossTeamsView` and `SupportQueueView` —
  show where the answer goes and offer a Preview; they call `GetAnswerRouteAsync` when a case opens and
  `PreviewAnswerAsync` on Preview, so a test verifying their calls expects them. A member's own reply box in
  `SupportCasesView` shows neither.
- **`SupportUnassignedView` carries no heading** — add your own from `SupportUnassignedViewText.Title` — and reads
  every page rather than the first twenty. Pass it the same `TimeTemplate` you give `SupportAcrossTeamsView`.
- **`SupportTranscript` injects `ISupportCaseService`**, to open files; a page using it directly has support
  cases registered already.
- **The registration's refusal of a sending address its own filter rejects now names the mailbox.**

**New text keys**, all in `ThargaTextKeys.All`: `team.support.filter.search`;
`team.support.transcript.notSentYet|notDelivered|sendAgain`;
`team.support.unassigned.noneMatch|awaiting|needsAnswer|suggested|suggested.assign|reason.severalTeams|reason.notVerified|reason.sharedAddress`;
`team.support.mailbox.cameInTo` ("to {0}");
`team.support.mail.wrote` ("On {0}, {1} wrote:") and `team.support.mail.supportName` ("Support"), in
`SupportMailText`, resolved through `ITextResolver` when an answer is mailed;
`team.support.answerRoute.mailed|mailedTo|site|redirected|withheld`;
`team.mailPreview.open|title|from|replyTo|to|toMany|subject|files|textOnly|frame|close|failed`;
`team.support.attachments.*` (attach, remove, chosen, attached, open, save, notAvailable, tooMany, tooLarge,
tooLargeTogether, unreadable); and `team.richText.*` (bold, italic, underline, bulletedList, numberedList, link,
linkAddress, linkText, linkNewWindow, linkOk, linkCancel, removeFormatting, undo, redo). No key was removed.
