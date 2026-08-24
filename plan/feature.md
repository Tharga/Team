# Feature: support cases, site-only (#142 phase 2) — slice 1

**Type:** feat (additive, non-breaking)
**Branch:** `feature/support-cases`
**Issue:** [Tharga/Team#142](https://github.com/Tharga/Team/issues/142) — the only open issue, wanted across
three consuming products
**Spec:** `plans/Toolkit/Platform/planned/04-notifications-and-support.md`, phase 2
**Target release:** 3.15 (new public API)

## Goal

A signed-in user can raise a support case for their team, reply to it, and read its history; support staff
can list a team's cases and reply. Persisted, authorized and audited — **with no Slack, no Jira, no AI and no
UI**.

## Why this slice, and where it stops

Phase 2 in the spec is *case model + history persistence + Blazor widget*. That is a large PR touching four
assemblies, so it is split:

| Slice | Contents | State |
|---|---|---|
| **1 — this one** | Contracts, port, Mongo adapter, operations, authorization, audit, scopes, tests | planning |
| **2 — next** | Blazor widget, history view, host wiring | not started |

Slice 1 deliberately ships **no user-visible surface.** That is the point: it proves the requirement the spec
calls the harder one — *a case raised on the site that never reaches Slack* — against a contract that is
tested rather than demonstrated. A widget built on an untested surface hides its defects behind rendering.

**Phases 3–5 stay out entirely** (Slack thread binding, AI bot, Jira). `SupportChannelBinding` is modelled
now because the spec is explicit that cases outlive and bypass channels, but **nothing reads or writes a
binding in this slice** — the collection exists and stays empty. Modelling it is not the same as building it.

## Placement — recommended, and the reasoning matters

The spec and the issue point in slightly different directions here (the issue sketches the case model living
in `Tharga.Team.Support`; the spec's rule 4 puts the persistence port in `Tharga.Team`). Resolved as:

| Assembly | Gets | Why |
|---|---|---|
| `Tharga.Team` | `SupportCase`, `SupportMessage`, `SupportCaseStatus`, `SupportChannelBinding`, `ISupportCaseStore` (port) | Architecture: *"Ports live in `Team.Contracts` as a namespace, not as their own package"*, and `Tharga.Team` **is** the Contracts role. These records carry no dependencies |
| `Tharga.Team.Support` | `ISupportCaseService` operations, the implementation, the authorization decorator, the audit decorator, scope registration | Already references `Tharga.Team.Service`, so it can host domain logic and reach the enforcement primitives. Keeps support out of the package every consumer installs |
| `Tharga.Team.MongoDB` | `SupportCaseEntity`, `MongoSupportCaseStore : ISupportCaseStore` | Spec rule 4, and it already references `Tharga.Team` |

**The alternative was rejected on dependency direction.** Putting the port in `Tharga.Team.Support` would
force `Tharga.Team.MongoDB` to reference `Tharga.Team.Support` — a persistence adapter depending on an
optional feature package. The architecture has adapters depending on Contracts and nothing else.

**The cost, stated plainly:** `Tharga.Team` grows four records and one interface for a feature many consumers
will never enable. That is accepted because the quarantine `Tharga.Team.Support` exists for is about
*dependencies* — inbound email, the Neurolito client, eventually `Anthropic` — and none of those are here.
Dependency-free contracts in the contracts package is what the architecture asks for.

## Ownership and lifetime — decided (user, 2026-08-24)

**The team owns the case. The author is a link that is allowed to dangle.**

A case is raised by a person, but it is *about* the team's account, and the team is what still exists a year
later. So `TeamKey` is the owning relationship and the only one authorization keys on; the author is recorded
but never required to still resolve.

**This is not a preference — a member-keyed case would be orphaned by design.** Deleting a user calls
`ITeamService.RemoveUserFromAllTeamsAsync`, which strips that person's membership from every team. A case
keyed on `MemberKey` would immediately point at a member row that no longer exists, in every team they were
in. `TeamKey` is unaffected by that path.

**Record the author twice, mirroring `AuditEntry` — which already solved this exact problem.**

| Field | Purpose | Precedent |
|---|---|---|
| stable subject | exact match, survives everything | `AuditEntry.CallerUserIdentity` — `ClaimTypes.NameIdentifier` / `IUser.Identity`, no fallback chain |
| display name **snapshot** | keeps history readable once the user is gone | `AuditEntry.CallerIdentity` — a display string |

Storing only the subject means a deleted author renders as an opaque id forever; storing only a name means no
exact match. The audit trail carries both for precisely this reason, and support history has the same
requirement.

**Consequences that follow, and are part of this slice:**

- **Deleting a user must not delete or hide their cases.** The team keeps the history. This needs a test —
  it is the requirement, not a side effect.
- **A case whose author no longer exists is still readable**, and reading it must not throw or render an
  empty name. Once the author is gone nobody satisfies the "is the author" check, so such a case is reachable
  only through `support:read` / `support:manage`. That is correct: there is no longer a self-service owner.
- **A case must survive a member being removed from the team**, not only a user being deleted — the same
  path, one team.

**Team deletion is the open half, and step 1 settles it.** Teams soft-delete and then purge; purge exists to
destroy a team's stored data. Cases plainly follow the team, but *"soft-deleted team, are its cases still
readable?"* and *"does purge take them?"* both need an answer, because leaving cases behind at purge is an
orphan nobody can reach and leaving them readable during soft-delete may be the point of soft-delete.

> **One thing to be aware of rather than build now.** Keeping a deleted user's messages verbatim is a
> deliberate **retention** decision, and support text is free-form — it is where someone pastes a password or
> a customer's details. A host under an erasure obligation will eventually need a way to scrub an author's
> content while keeping the case. **Out of scope for this slice**, and noted so the decision is visible
> rather than made by accident.

## The v4 rules, which bite for real here

This is the first substantial new surface since the architecture was written, so:

- **Rule 1 — operations, not CRUD.** `RaiseCaseAsync`, `ReplyToCaseAsync`, `CloseCaseAsync`. No
  `UpdateCase(dto)`. Each is one authorizable, auditable fact.
- **Rule 2 — one enforcement point.** Authorization lives in the decorator in `Tharga.Team.Support`, nowhere
  else. Slice 2's widget gates rendering only and re-checks nothing.
- **Rule 3 — contracts serialize by construction.** Records; **history is paged with an explicit cursor**;
  no generic methods, no interface-typed returns, **no `IAsyncEnumerable` in a contract**. A case with two
  years of messages must not be a single unbounded read.
- **Rule 4 — the port speaks the domain's language.** `ISupportCaseStore` must not inherit or expose a
  `Tharga.MongoDB` type. This is the `IApiKeyRepository : IRepository` mistake, and it is easy to repeat.
- **Rule 5 — the port expresses atomicity.** Appending a message and moving a case's status are one fact;
  if any operation spans two writes, the port has to be able to say so.
- **Rule 6** — not engaged; no new claims are issued.

## Authorization — the design to confirm

Two scopes, both **team** scopes:

| Operation | Authorized by |
|---|---|
| `RaiseCaseAsync(teamKey, …)` | **Membership, not a scope.** Raising a case about your own team is ordinary; gating it means every host must grant it to everyone, and a scope everyone holds checks nothing |
| `GetMyCasesAsync(teamKey)` | Membership; **filtered** to the caller's own cases |
| `ReplyToCaseAsync(caseId, …)` | The case's author, **or** `support:manage` |
| `GetCaseAsync` / `GetCasesAsync(teamKey)` | `support:read` — reading *other people's* cases is a privileged act |
| `CloseCaseAsync(caseId)` | The case's author, **or** `support:manage` |

`shared-instructions.md` is explicit that *"an entry point's check need not be a scope"* — the invitation
path is the precedent. **Every one of these is still checked**; two are checked by membership and authorship
rather than by a grant.

**A support case can contain anything a user types, including credentials they should not have pasted.**
`support:read` is therefore a real privilege boundary, not a formality.

## Acceptance criteria

- [x] A case can be raised, replied to and closed, and its history read back in order.
- [x] A case raised on the site is complete and trackable with **zero channel bindings** — the requirement
      this slice exists to prove.
- [x] **Deleting the author leaves the case and its full history intact and readable**, with the author's
      name still rendering from the stored snapshot. Same for removing them from the team.
- [x] Authorization keys on `TeamKey` alone; no check depends on the author still being a member.
- [x] History is paged with an explicit cursor, and no contract exposes `IAsyncEnumerable`, a generic method
      or an interface-typed return — asserted by a test, not by review.
- [x] `ISupportCaseStore` exposes no `Tharga.MongoDB` type — asserted by a test.
- [x] A member cannot read another member's case without `support:read`; a non-member cannot reach the team's
      cases at all.
- [x] Every operation is audited with the actor and the case id.
- [x] Authorization lives in exactly one place — asserted by the existing internal-service-injection guard
      extended to the new surface.
- [x] `Tharga.Team.MongoDB` does **not** reference `Tharga.Team.Support`.
- [x] Full test suite green - 2148 passed, 0 failed.

## Deferred during the build, with the reason (2026-08-24, user)

**The purge cascade is not wired, and that is a decision rather than an omission.**

Step 1d chose option A — purge calls `ISupportCaseStore` directly, *resolved from the service provider,
never from a new `TeamServiceRepositoryBase` constructor parameter*, because a subclass that forgets to
forward an optional parameter silently disables the feature and a silently-skipped purge is far worse than
a missing icon.

**Option A has nowhere to stand.** `PurgeTeamAsync` lives on `TeamServiceRepositoryBase`, whose constructor
takes `(IUserService, ITeamRepository, IMongoDbServiceFactory, IIconStore?, ITeamCache?)` — there is no
service provider at the purge site, and adding one is exactly the trap the decision ruled out. Reaching a
provider from there requires the seam option B described.

**Why it is deferred rather than bodged:** a purge that half-works is worse than one that visibly does not,
and the backlog already carries a **Critical** finding that purge does not remove a team's **API keys**
either — credentials outliving the tenant they authorized. Both are the same missing mechanism. One
purge-cascade design should fix both, and that is a feature rather than a footnote to this one.

`ISupportCaseStore.DeleteCasesForTeamAsync` exists and is tested; only the wiring is absent. Documented as a
known limitation with the workaround, so nothing claims behaviour that is not there.

**Also deferred: hiding a soft-deleted team's cases** (step 1c). Enforcing it needs a team lookup on every
case read, and the only ungated route is the internal service, which support code must not inject. Recorded
as a limitation rather than silently skipped.

## Explicitly out of scope

Slack binding, Jira, AI, presence, inbound transport, the Blazor widget, email. Also: **no `SupportOptions`
knobs invented on spec** — configuration is added when something needs configuring.

## Done condition

A host can register support cases, and a consuming product could build a UI against the contract without the
toolkit changing. Slice 2 then does exactly that.

## Package updates — held, standing decision

Only the xunit 4.0 / Microsoft.Testing.Platform pair, twice backed out and failing on the Linux runner. A
third attempt was made and rolled back on 2026-08-24. Held; it needs its own PR.
