# Feature: "View as another user…" works for a team Owner/Admin

## Goal

A team Owner or Administrator holding `simulation:use` can open the access-simulation
member picker and see who they are choosing between — without holding the `users:manage`
system scope.

## The defect

`AccessSimulationState.GetMemberTargetsAsync` resolves each member's display name through
`DisplayNameAsync`, which calls `IUserService.GetUserByKeyAsync(member.Key)`. That member
carries `[RequireScope(SystemUserScopes.Manage)]`, so `AuthorizationUserServiceDecorator`
throws:

```
GetUserByKeyAsync requires the 'users:manage' system scope.
```

`simulation:use` is a **team** scope registered at `AccessLevel.Administrator` — its own
XML docs say "checking what a Viewer sees before inviting one is an ordinary thing for a
team owner to want". `users:manage` is a **system** scope that no team access level grants.
So the feature is gated on a scope its intended audience structurally cannot hold: a
customer's own team owner can open the dialog and never populate it.

This is the pattern `shared-instructions.md` → *Service layering and authorization* names:
a first-level surface reaching for a gated member that does not fit the caller, when the
correct member already exists.

`member.Name` is a per-team *override* and is usually null, so the short-circuit at the top
of `DisplayNameAsync` almost never saves the call in production.

## Why no test caught it

`AccessSimulationStateTargetsTests.FakeMember` declares `public string Name => Key`, so the
per-team override is always non-empty in the suite and `DisplayNameAsync` returns before it
reaches the user store. The fake is the inverse of production, where the override is
usually null.

## Scope

- `AccessSimulationState` only. No change to any scope registration, to
  `AuthorizationUserServiceDecorator`, or to what `users:manage` means.
- The fix uses the existing `IUserService.GetTeamMemberUsersAsync()` projection — already
  documented as "the identity source for the team member list when the caller lacks
  `SystemUserScopes.Manage`" — selected through the existing `UserDirectoryGate`, exactly as
  `TeamComponent` and `AuditLogView` already do.

## Out of scope

- Widening `GetUserByKeyAsync`'s gate. It is correct as a cross-user administrative read.
- Any change to who may *start* a simulation. `simulation:use` and `simulation:demo` stay as
  they are.

## Acceptance criteria

- [ ] A caller holding `simulation:use` but not `users:manage` gets a populated member
      picker with real display names, and no `UnauthorizedAccessException`.
- [ ] A caller holding `users:manage` keeps reading the full directory, so a consent-reaching
      administrator who is not a member of the selected team still resolves names.
- [ ] The per-team `ITeamMember.Name` override still wins when it is set.
- [ ] A member with no user record still falls back to the member key rather than throwing.
- [ ] Name resolution costs one user-store read per dialog open, not one per member.
- [ ] Tests cover the no-`users:manage` path with a member whose `Name` override is null —
      the case the existing fake hid.

## Done condition

`dotnet test -c Release` green, docs reviewed, user confirms the picker works for a plain
team Owner.
