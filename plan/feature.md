# Feature: system-scoped owner change

**Issue:** [Tharga/Team#225](https://github.com/Tharga/Team/issues/225) — *No system-scoped way to change
the owner of a team that already has one.*

**Branch:** `feature/system-owner-change` (from `master`)

## Goal

Give a system operator a supported way to change the owner of a team that **already has an owner**, so a
host no longer has to write the team entity directly.

## Why this one, and why it matters more than the missing convenience

Today the two existing operations each rule themselves out:

- `TransferOwnershipAsync` moves ownership but requires the caller to **be** the owner
  (`TeamServiceBase.cs:673`). Staff are not.
- `AssignOwnerAsync` is system-scoped and is the right entry point — but refuses when the team already has
  an owner (`TeamServiceBase.cs:653`), because it is the repair path for an *ownerless* team.

So the one operation with the system scope declines the case, and the one that handles the case demands an
identity the operator does not have.

The reporter's workaround is the part worth fixing: they write the team entity directly, and therefore now
enforce *"a team must not be left without an owner"* **in their own code**. An invariant left the domain and
landed in a host. That is target-architecture rule 2 (one enforcement point) inverted, so closing this gap
pulls enforcement back where v4 says it belongs — the safe path is currently closed while the unsafe one is
open.

## Scope

### In

1. **`SetOwnerAsync<TMember>(teamKey, newOwnerUserKey)` — "make X the sole owner of T".** One operation
   covering every case, because they are one primitive (see the table below). `AssignOwnerAsync` becomes an
   `[Obsolete]` forwarder to it, removed in 4.0.
2. **The existing `teams:assign-owner` scope, widened.** No new scope — decided by the user 2026-08-18.
3. **Outgoing owners are demoted to `Administrator`**, matching `TransferOwnershipAsync:683` and what the
   reporter's own workaround chose. **Every** other `Owner` is demoted, not just one, so a team synced from
   a system that permits several owners is reduced to exactly one in a single call.
4. **Idempotent.** When the candidate is already the sole owner: succeed, change nothing, write no audit
   entry. The driver is a *repeated* legacy sync — an operation that throws whenever the state is already
   correct forces the sync to swallow exceptions, which is how real errors get swallowed too.
5. **Audited** as its own event, alongside `transfer-ownership`.
6. **UI**: two affordances on the Teams tab over the one operation — *Reduce to a single owner* when a team
   has several, *Change owner* when it has one — extending the existing action at `TeamsListView.razor:253`
   and `UserAdminGate.cs:97`.
7. **Docs**: rewrite, not amend — see *Documentation that becomes false* below.

### The cases, all one primitive

| Starting state | `SetOwnerAsync(team, X)` | Scenario |
|---|---|---|
| Owners A, B, C — X among them | X stays Owner; A, B → Administrator | legacy multi-owner cleanup |
| Owner A only, X ≠ A | X → Owner; A → Administrator | operator-driven transfer |
| No owner, X is a member | X → Owner | the old `AssignOwnerAsync` repair case |
| X already sole owner | nothing, no audit entry | repeated sync |

**"Cannot demote leaving zero owners" is structural, not a check.** There is no bare demote-an-owner
operation, so demotion only ever happens as the tail of a promotion. `SetMemberRoleAsync`'s refusal to grant
or revoke `Owner` stays exactly as it is, so no other path can produce it either.

### Out

- Any change to `TransferOwnershipAsync` or to in-team `team:manage`.
- Making `AccessLevel.Owner` assignable through `SetMemberRoleAsync` — that guard stays exactly as it is.
- Multi-owner support. This reduces to one owner; it does not make several a supported state.

## The decision that needed making, and how it was settled

**What happens to the outgoing owner** — the issue explicitly leaves this open, and the reporter had to
invent an answer (`Administrator`). Settled as **demote all other Owners to Administrator**, because
`TransferOwnershipAsync` already does exactly that at `TeamServiceBase.cs:683`. The consumer and the
codebase independently reached the same answer, so consistency costs nothing here.

## One scope, decided — and what it costs

**Decided by the user 2026-08-18: reuse `teams:assign-owner`. No second scope.**

A separate `teams:set-owner` was proposed and rejected. Recording the trade-off, because the consequences
land in the release notes rather than in the code:

- **The grant genuinely widens**, from *"repair an ownerless team"* to *"make anyone the sole owner of any
  team"*. Every host that has already granted `teams:assign-owner` gains the ability to depose a sitting
  owner **on upgrade, with no action on their part**. Practical exposure is near zero — the scope shipped in
  3.9.0 on 2026-08-01 and the consumers in the follow-up list are still on 3.8.x — but the release notes must
  say *"this grant now authorizes more than it did"*, not describe it as a new capability.
- **The narrow operation stops being a safety boundary.** `AssignOwnerAsync`'s refusal was worth keeping only
  while it was tied to a separate grant. Sharing one scope, anyone who can call the narrow operation can call
  the wide one, so the refusal protects nothing — which is why the operation is being collapsed rather than
  kept as a sibling.
- **The scope *string* stays `teams:assign-owner`** even though it now authorizes more than assignment.
  Renaming it would break every host's role mapping for no functional gain. Its documentation carries the
  widened meaning instead.

## Documentation that becomes false

Not amendments — these three currently assert the property being removed, and must be rewritten:

- `SystemTeamScopes.AssignOwner` XML docs: *"The operation refuses when the team already has an owner, which
  is what keeps this a repair rather than a way to take over a healthy team."*
- `TeamOwnership` type and member remarks, which frame ownerless-ness as *"the only state in which assigning
  an owner is a repair rather than a takeover"*.
- `docs/articles/user-management.md`: *"an attempt to 'repair' a team that is not broken is what taking one
  over would look like."*

Leaving any of these standing would document a safety property the code no longer has, which is worse than
having no note at all.

## Acceptance criteria

- [ ] An operator holding `teams:assign-owner` can reduce a team with owners A, B, C to X alone; the others
      end up `Administrator`.
- [ ] The same operator can move ownership on a single-owner team: X becomes Owner, A becomes Administrator.
- [ ] The same operator can still repair an ownerless team — the old `AssignOwnerAsync` case.
- [ ] Calling it when X is already the sole owner succeeds, changes nothing, and writes **no** audit entry.
- [ ] A caller without the scope is refused; an **in-team** claim of the same name does not satisfy it.
- [ ] The candidate must already be a member; a non-member is refused by name.
- [ ] The team is never ownerless at any point — the promotion is applied before any demotion.
- [ ] Every affected member's cache entry is dropped, so claims do not keep reading the old level.
- [ ] One audit entry names actor, team, new owner and **every** demoted owner.
- [ ] `AssignOwnerAsync` still compiles and still works for existing callers, now as an `[Obsolete]`
      forwarder — proven by its existing tests passing against the new implementation.
- [ ] `SetMemberRoleAsync` still refuses to grant or revoke `Owner`.
- [ ] No documentation still claims the scope cannot take over a healthy team.
- [ ] Full suite green (974 tests at branch point, plus the new ones).

## Done condition

All acceptance criteria met, docs updated, `Requests.md` and the backlog closed out with evidence, issue
#225 answered and closed with what shipped, and `plan/` removed in the close-out commit.
