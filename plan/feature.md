# Feature: Only the Owner may delete a team

## Goal

Deleting a team from inside it becomes an **Owner** act. The cross-team `teams:delete` system scope is
untouched — that is the operator path and has nothing to do with membership.

## Why

The UI and the service disagree, and have since `teams:delete` was introduced:

- `AuthorizationTeamServiceDecorator.RequireDeleteAsync` admits `teams:delete`, **or** `team:manage` on the
  team with `AllowTeamCreation`. `team:manage` is registered at `AccessLevel.Administrator`, so **any
  administrator can delete their own team** through the service.
- `TeamActionGate.CanDelete` additionally requires `isOwner`, pinned by a test. So the button is never
  offered to an administrator, who can nonetheless reach the operation.

Raised 2026-09-06 while fixing the leave-team defect. The user chose Owner-only: deleting a team is the
one in-team act that removes the team itself, and it belongs with the person who cannot leave without
first handing the team on.

## Scope

In:

- `TeamAuthorizer.IsOwnerOfAsync(teamKey)` — the `TeamKey` claim equals the target **and** the
  `AccessLevel` claim is `Owner`. Both halves, for the same reason `HasTeamScopeAsync` binds to the team:
  the access level is emitted for the selected team only.
- `RequireDeleteAsync` uses it in place of the `team:manage` check. Restore follows, as it already does —
  restoring undoes deleting.
- Docs and the XML on `ITeamManagementService.DeleteTeamAsync`, which currently states the old rule.
- `MAJOR_MINOR` 3.20 → 3.21.

Out:

- **The system `teams:delete` path.** Unchanged, including its independence from `AllowTeamCreation`.
- **`teams:purge`.** Already its own system scope with no in-team path at all.
- **The UI.** `TeamActionGate.CanDelete` already requires the Owner; the service is what moves. Its tests
  stay green untouched, which is the point.

## Acceptance criteria

- [ ] The Owner of a team can delete it, with `AllowTeamCreation` enabled.
- [ ] An Administrator of that team is refused, and the message says the Owner is required.
- [ ] A holder of `team:manage` who is not the Owner is refused — the scope alone no longer suffices.
- [ ] A holder of the `teams:delete` system scope can still delete any team, member or not, and regardless
      of `AllowTeamCreation`.
- [ ] The Owner of team A cannot delete team B.
- [ ] With `AllowTeamCreation` disabled, even the Owner is refused.
- [ ] Restore behaves exactly as delete does.
- [ ] Full test suite passes.

## Done condition

Deleting a team from inside it requires being its Owner, in the service as well as in the UI, and the
operator path is unaffected.
