# Feature: Directory name sync on the Users page

## Goal
When the Users page (`~/developer/user`, `UsersListView`) verifies users against the external directory
(Entra), it also compares each user's name with the directory's display name. Where they differ, an
administrator can resolve it in either direction: take the directory's name into this application, or
write this application's name to the directory.

## Background
Entra External ID sign-up flows that collect no display name give the account the placeholder `unknown`.
The application then carries a real name (entered by the user or an administrator) while the directory
keeps the placeholder — or the reverse, when the name was corrected in the Entra portal.

## Scope
- `DirectoryVerificationResult` carries the directory's display name (`DirectoryName`).
- The Entra adapter fills it from the `displayName` it already selects.
- A placeholder rule: a directory name that is empty or reads `unknown` (any case) is not a real name.
- `IUserManagementService.UseDirectoryNameAsync(userKey)` — re-reads the directory and sets the local name from
  it. Refused when the directory name is a placeholder. The directory name is resolved server side.
- **Breaking:** `IUserManagementService.SetUserNameAsync(userKey, name, bool writeToDirectory)`. The caller says on
  every rename whether to write to the directory; `o.Blazor.WriteNameToDirectory` only allows it. A write that
  cannot be honoured (option off, no directory, not linked, placeholder name) is refused before anything changes.
- Authorization and auditing decorators cover both operations; the rename's audit records `directory.updated`.
- `UsersListView`: after Verify / Verify all, a "Name differs" badge beside the name, with the directory name in
  its tooltip. Rename, or a click on the badge, opens `UserNameDialog`: the name, the directory's name read
  afresh with "Use this name", and "Also update the name in the directory" (shown when the option is on and the
  user was found, unticked). Saving confirms first when the directory is involved.

## Acceptance criteria
- Verify all marks every linked user whose local name differs from the directory name.
- Taking the directory's name sets the local name and clears the mark; it is refused for a placeholder.
- Writing to the directory happens only when asked in that rename, after a confirmation, and clears the mark.
- A rename that stays local is saved without a confirmation, as before.
- A user without the `users:manage` scope cannot call either operation (decorator test).
- Both operations are audited.
- Full test suite passes.

## Done condition
Merged to master via PR, README/docs updated where the Users page and directory features are described.
