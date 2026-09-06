# Plan: A member can leave a team

Branch `feature/leave-team`, from `master` at 7ce1294.

## Steps

- [x] 1. NuGet package sweep (mandatory, before any feature code).
      `dotnet outdated Tharga.Team.sln` — **no outdated dependencies**. Nothing to apply, nothing to
      commit. Baseline verified: `dotnet build -c Release` clean (16 pre-existing warnings, 0 errors),
      `dotnet test -c Release` **2527 passed, 0 failed**. SDK 10.0.302 reports a real count, so the
      "zero tests ran" trap from shared-instructions does not apply here.

- [x] 2. `ITeamService.LeaveTeamAsync(string teamKey)` — a default interface method that throws
      `NotSupportedException`, matching `RestoreTeamAsync` / `PurgeTeamAsync` / `SetMemberSuspendedAsync`,
      so a host implementing the store contract directly keeps compiling. XML docs state that the
      operation names no user because it acts only on the caller.

- [x] 3. `TeamServiceBase.LeaveTeamAsync` — the domain. Resolve the current user, refuse a non-member,
      then the guards that already exist: Owner refused ("Transfer ownership first"), last
      administrator refused. Extract the shared refusal-and-removal tail out of `RemoveMemberAsync` so
      there is one removal path rather than two that can drift.

- [x] 4. `AuthorizationTeamServiceDecorator.LeaveTeamAsync` — forwards with **no scope check**, with
      XML docs stating why: the signature carries no user key, so the caller can only remove
      themselves, and a suspended member holds no scope any gate could accept. Without this override
      the decorator inherits the throwing default and never reaches the inner service.

- [x] 5. `AuditingTeamServiceDecorator.LeaveTeamAsync` — a `leave-team` action, distinct from
      `remove-member`, following the existing try/catch/Log shape. Same reason as step 4: the override
      is required for the call to reach the store at all.

- [x] 6. `ITeamDirectoryService.LeaveTeamAsync` — the first-level surface, ungated like its neighbour
      `IsSuspendedAsync`, which is ungated for exactly this reason. `TeamManagementService<TMember>`
      already implements that facet and is already in `TeamServiceFacets.All`, so registration needs no
      change. Forward to `_inner.LeaveTeamAsync`.

- [x] 7. Blazor: the Leave action calls `TeamDirectoryService.LeaveTeamAsync` rather than
      `RemoveMemberAsync`, with its own confirmation text (`ConfirmLeaveTeam`) and an `ActionLeave`
      label key, since every other action in that menu uses one. `TeamActionGate.CanLeave` needs no
      change — `member && !owner && selected` becomes correct rather than optimistic.

- [x] 8. Tests. Service: regular user leaves, viewer leaves, suspended member leaves, owner refused,
      last administrator of an ownerless team refused, non-member refused. Authorization: the decorator
      admits `LeaveTeamAsync` for a caller holding no scopes, and still refuses `RemoveMemberAsync` of
      another member. Audit: a `leave-team` entry is written. Blazor: the gate tests already cover
      visibility.

- [~] 9. Full suite green, `dotnet outdated` re-checked, README/docs reviewed for a surface this
      changes.

## Notes

- Decorator order is Authorization (outermost) → Audit → store, per `ThargaBlazorRegistration.cs:299`
  and `:460`. A default interface method is **not** forwarded by a decorator that does not override it,
  which is why steps 4 and 5 are required rather than optional.
- Suspension is a `SuspendedAt` flag on the member, not a `MembershipState`, so `IsMemberOf` in the
  Blazor component is already true for a suspended member and the button already renders for them.
- Version: a fix plus purely additive API needing no consumer action — a patch bump.

## Decisions made while implementing

- **`RemoveTeamMemberAsync` in `TestTeamService` was a no-op**, so every existing leave test asserted
  only that nothing threw. Made it actually drop the member, which is what lets the new tests assert
  *who* left. All 881 tests in that project still pass, so nothing depended on the no-op.
- **`LeaveTeamAsync` reads the roster through `GetMembersAsync`**, not the reflected `Members` property
  `RemoveMemberAsync` uses. It is virtual, so a store can answer it properly, and where nothing can the
  sequence is empty and leaving refuses. Leaving must fail closed: an owner slipping past the guard
  strands a team only `teams:set-owner` can repair.
- **`TeamActionGate.CanLeave` no longer takes the selected team.** Its own docs said Leave was confined
  to the selection *because* the service wanted `member:manage` on the team being left. That reason is
  gone, and keeping it would make somebody select each of five teams in turn to leave them. Widened
  deliberately — flag to the user.
- **Three hard-coded action labels** ("Leave", "Delete", "Rename") became text keys, matching every
  other action in that menu. A duplicate `@inject ITeamManagementService` was removed.

## Last session

2026-09-06 — Steps 1-8 done. Build clean, full suite **2539 passed, 0 failed** (2527 before; +13 new
tests, −1 from collapsing a gate theory). Step 9 in progress: docs review, backlog note for the Delete
inconsistency, then close-out.
