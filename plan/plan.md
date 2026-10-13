# Plan: Directory name sync on the Users page

- [x] 0. NuGet update — `dotnet outdated` reports nothing outdated; no deps commit needed.
- [x] 0b. Invite view spinner fix committed on this branch as its own `fix:` commit (made on master before the branch existed).
- [x] 1. Contract: `DirectoryName` on `DirectoryVerificationResult`; placeholder rule (`DirectoryNames.IsPlaceholder`); tests.
  - `DirectoryNames.Differ` treats two placeholders as equal (nothing to move); comparison is ordinal after trimming.
- [x] 2. Entra adapter returns `displayName` in verification results; tests.
  - Already selected `displayName`; only the result mapping changed.
- [x] 3. `IUserManagementService.UseDirectoryNameAsync` / `WriteNameToDirectoryAsync` in `UserManagementService`; tests (`DirectoryNameSyncTests`).
  - Pull re-verifies server side (and relinks); push throws unless the option is on, the user is linked and has a real name.
- [x] 4. Authorization and auditing decorators for both operations; tests.
  - Audit actions `use-directory-name` (records the name taken) and `write-name-to-directory`.
- [x] 5. `UsersListView`: name-differs badge, the two row actions, texts in `UsersListViewText`; tests.
  - Show/offer rules live on `UserViewModel` (`NameDiffersFromDirectory`, `CanUseDirectoryName`, `CanWriteNameToDirectory`) and are unit tested (`UserRowDirectoryNameTests`); no test project renders the full list.
  - Verification results now survive the list reload after a rename/delete/pull.
- [x] 6. Full test suite, commit. 3282 passed.
- [x] 7. Docs: new "Names that differ from the directory" section in `docs/articles/user-management.md`; paragraph in `Tharga.Team.Entra/README.md`.
- [ ] 8. User tests in the sample app; then close-out (re-check NuGet, archive plan, remove `plan/`, PR).

## Decisions
- "Write name to directory" only when `o.Blazor.WriteNameToDirectory` is on — the service refuses otherwise, the UI hides it. Pull is always available.
- Differences are shown as a row badge (tooltip = directory name) with the two actions in the row's menu, not a review dialog.

## Last session
2026-10-13: steps 1-6 done and committed. Docs done. Next: user tests on the pushed branch.
