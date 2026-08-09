# Feature: close #204 — TeamComponent and the UsersView surface

**Issue:** [Tharga/Team#204](https://github.com/Tharga/Team/issues/204)
**Branch:** `feature/text-team-and-users-surfaces`
**Follows:** [PR #212](https://github.com/Tharga/Team/pull/212), which shipped `AuditLogView` and rebuilt the
coverage ratchet after finding it understated the work threefold.

## Goal

Route the last two of the four components #204 names — `TeamComponent` and the `UsersView` tabs — plus the
dialogs they open, so a consumer registering an `IThargaTextProvider` sees **no English** on any of the four
surfaces they named. This is the feature that lets #204 be closed truthfully.

## Scope — 184 strings across 14 files

**The `UsersView` surface (102).** `UsersListView` 50, `TeamsListView` 30, `DirectoryOnlyUsersView` 6,
`UserIconDialog` 8, `DeleteUserDialog` 6, `AssignOwnerDialog` 2. The wrapper is already at zero; these are
the tabs and dialogs a user actually reads, and the gap that made the old record wrong.

**The team surface (82).** `TeamComponent` 60, `TeamIconDialog` 7, `InviteUserDialog` 4, `TeamInviteView` 3,
`RoleEditor` 3, `SuspendedTeamNotice` 2, `ScopeOverrideEditor` 2, `TeamDialog` 1.

## Out of scope — 135 strings, staying on the ratchet

`ApiKeyView` 44, `SystemApiKeyView` 35, `ScopeView` 15, `UserProfileView` 13, `AccessSimulationDialog` 12,
`TenantRoleManager` 11, `AccessSimulationBar` 3, `ApiKeyRevealDialog` 2. None is named by #204. They stay
recorded with exact counts so closing #204 does not read as "the toolkit is fully localizable".

**`SixLabors.ImageSharp` 3.1.12 → 4.0.0 stays held** — decided 2026-08-09, unchanged since, and it is the
only outstanding package update. It gets its own pass.

## Acceptance criteria

1. All 14 files scan to zero and move into `Migrated`; `Pending` holds only the 8 out-of-scope components.
2. Every new catalogue's keys are asserted present in `ThargaTextKeys.All`, per catalogue, not by sample.
3. Messages that name something use `TextSet.Format` with positional placeholders — never interpolation,
   which cannot be translated at all.
4. Build clean; full suite green (1,906 is the floor).
5. The coverage table in `docs/articles/implementation-guide.md` updated to match, including that the four
   named surfaces are complete and what remains outside them.
6. #204 closed, citing the zero counts; `Requests.md` and `Eplicta/requests.md` set Done with the version.

## Done condition

FortDocs registers a provider and sees **no English and no "Team"** in `TeamSelector`, `TeamComponent`,
`UsersView` (both tabs) or `AuditLogView` — including dialog titles, notifications and confirmation prompts,
which is most of what a user reads.
