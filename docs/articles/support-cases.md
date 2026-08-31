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
| Read, reply to or close **your own** case | **Authorship** |
| Read or list **anyone's** case | `support:read` |
| Reply to or close **anyone's** case | `support:manage` |

**Not everything is a scope, deliberately.** Raising a case about your own team is what an ordinary member
does; gating it would mean every host granting that scope to everybody, and a scope everyone holds checks
nothing. Membership and authorship are still *checks* — they are simply not grants.

> **`support:read` is a real privilege boundary.** A support case contains whatever a user typed into it,
> which is exactly where somebody pastes a password, a token or a customer's details. Grant it as carefully
> as you grant `audit:read`.

Both scopes are team scopes registered at `Administrator`. A case is always loaded through its team, so
holding a valid case id from another tenant gains nothing.

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

1. Create a Slack app and install it to the workspace; copy the **bot token** into `Slack:BotToken`
   (`AddThargaSupport`) and the **signing secret** into `SigningSecret`.
2. Invite the bot to the support channel — it cannot post to a channel it is not in.
3. Under **Event Subscriptions**, point the request URL at `/_tharga/support/slack/events` and subscribe to
   `message.channels` (or `message.groups` for a private channel).
4. Slack sends a one-off verification challenge when you enable it. The endpoint answers it automatically.

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
