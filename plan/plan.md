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
- [x] 7b. Rebased onto master (2026-10-25; 303 commits behind, never pushed, so nothing shared was rewritten).
  - Conflicts in 7 files, all both-sides-added: master had gained `SetUserEmailAsync` and `GetSignInIdentitiesAsync` at the same places. Kept both everywhere.
  - Decision: `UseDirectoryNameAsync` and `WriteNameToDirectoryAsync` got default bodies that throw `NotSupportedException`, the convention master adopted for new `IUserManagementService` members since, so a host's own implementation keeps compiling. Pinned by `HostImplementation_WithoutNameSync_SaysWhatItDoesNotImplement`.
  - Build at the warning baseline (14). Full suite green: Service 1530, Blazor 2308, Email 985, Support 814, MongoDB 468, Mcp 121, Entra 58, Support.Anthropic 12, Images 11.
- [~] 8. User tests in the sample app; then close-out (re-check NuGet, archive plan, remove `plan/`, PR).
  - The sample has the Entra client secret on this machine, so Verify shows. `o.Blazor.WriteNameToDirectory` is off in the sample, so "Write name to directory" stays hidden unless it is turned on for the test.

## Decisions
- "Write name to directory" only when `o.Blazor.WriteNameToDirectory` is on — the service refuses otherwise, the UI hides it. Pull is always available.
- Differences are shown as a badge beside the name (tooltip = directory name) with the two actions in the row's menu, not a review dialog.
  - Moved there from the actions column on 2026-10-25, during user testing: the actions column is a fixed 200px and its cells do not wrap, so after Verify the directory badge plus this one pushed the split button out of sight. The actions column now wraps too, so no combination of badges can hide the button.

## Last session
2026-10-13: steps 1-6 done and committed. Docs done. Next: user tests on the pushed branch.

2026-10-25: rebased onto master (4.4 line), conflicts resolved, default bodies added for the two new interface
members, full suite green. Next: user tests in the sample, then close-out.
