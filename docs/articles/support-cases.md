# Support cases

A signed-in member can raise a support case for their team, reply to it, and read its history. Cases are
persisted, authorized and audited.

> **What this is not, yet.** There is **no Slack thread, no Jira link, no AI responder and no UI component**.
> This is the case model and its operations — the half that has to be right before a widget is built on it.
> See [issue #142](https://github.com/Tharga/Team/issues/142) for the phases that follow.

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

## What happens when things are deleted

| Event | Effect on cases |
|---|---|
| The author is deleted, or leaves the team | **Cases and transcripts are kept**, and stay readable with the name they were written with. Nobody then satisfies the authorship check, so such a case is reachable through `support:read` / `support:manage` |
| The team is soft-deleted | **Cases remain readable to a caller whose team claim still names it.** See the limitation below. Restoring the team is unaffected |
| The team is **purged** | **Cases are destroyed with it**, by a purge participant |

> **One known limitation.** A soft-deleted team's cases are still readable to a caller whose `TeamKey` claim
> still names it, which is inconsistent with every other team read — all of which exclude soft-deleted teams.
> Purge is unaffected: it destroys the cases either way.
