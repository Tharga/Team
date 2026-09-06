# Feature: A member can leave a team

## Goal

Any member of a team can leave it, without holding `member:manage`. The Owner cannot — they must
transfer ownership first — and the last administrator of an ownerless team cannot. Both refusals
already exist in the domain and stay exactly where they are.

## The defect

Leaving is not an operation. It is `RemoveMemberAsync(teamKey, self)`, gated on `member:manage` in two
places (`ITeamManagementService.cs:25` via `ScopeProxy`, and `AuthorizationTeamServiceDecorator.cs:190`).
That scope is registered at `AccessLevel.Administrator`, and only Owner and Administrator receive every
registered scope — so `User` and `Viewer` hold nothing that satisfies it.

Meanwhile `TeamActionGate.CanLeave` offers the button on membership alone. A regular member sees
**Leave** and gets an `UnauthorizedAccessException`. The button and the gate disagree.

The policy is already implemented one layer down, in `TeamServiceBase.RemoveMemberAsync:435`. That is
why `LeaveTeamTests.RegularUser_CanLeaveTeam` passes: it exercises the base service, below the gate.

## Why leaving cannot be authorized by a scope

A suspended member holds **no** team scopes at all — not even `team:read`. Any scope-gated leave
excludes them silently, making "suspended people cannot leave" an accident of the gate rather than a
decision anyone took. Suspension is the state in which someone most wants out, and letting them go
strands nothing: the Owner cannot be suspended (`TeamServiceBase.cs:520`), so a suspended member is
never the owner, and the last-administrator guard still runs.

`shared-instructions.md` sanctions this: *"An entry point's check need not be a scope … the rule is that
a first-level call is checked, not that it is checked by a scope."* The check here is structural — the
operation takes no user key, so there is nothing to point at anyone else.

## Scope

In:

- `LeaveTeamAsync(string teamKey)` through every layer: store contract, domain, both decorators, the
  facade, and `ITeamDirectoryService` as the first-level surface.
- A distinct `leave-team` audit action, so the log can tell "left" from "was removed".
- The Blazor Leave button calls it, with its own confirmation text.
- Tests: regular user, viewer, suspended member, owner refused, last administrator refused, and an
  assertion that the operation is reachable without `member:manage`.

Out:

- `RemoveMemberAsync` keeps `member:manage` unchanged. It stays the administrator's tool for removing
  *other people*.
- The Delete inconsistency (UI offers it to the Owner only, the service allows any `team:manage`
  holder). Deliberately left alone this round; recorded in the backlog as a separate decision.

## Acceptance criteria

- [ ] A member at `User` or `Viewer` can leave the team they belong to.
- [ ] A suspended member can leave.
- [ ] The Owner is refused, with a message naming transfer of ownership.
- [ ] The last administrator of an ownerless team is refused.
- [ ] A non-member is refused.
- [ ] Leaving cannot remove anyone but the caller — the operation takes no user key.
- [ ] `member:manage` still governs removing another member.
- [ ] Leaving writes a `leave-team` audit entry, distinct from `remove-member`.
- [ ] Full test suite passes.

## Done condition

A regular member can press **Leave** and it works; the owner still cannot; the audit log distinguishes
the two ways a member leaves a team.
