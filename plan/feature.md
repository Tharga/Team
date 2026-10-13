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
- Two new operations on `IUserManagementService`, both `[RequireScope(SystemUserScopes.Manage)]`:
  - `UseDirectoryNameAsync(userKey)` — re-reads the directory and sets the local name from it. Refused
    when the directory name is a placeholder.
  - `WriteNameToDirectoryAsync(userKey)` — writes the local name to the directory.
  Both resolve the directory name server side; the client never supplies it.
- Authorization and auditing decorators cover both operations.
- `UsersListView`: after Verify / Verify all, a row whose name differs shows a "Name differs" badge with
  the directory name in its tooltip, and the row's actions offer "Use name from directory" (hidden when
  the directory name is a placeholder) and "Write name to directory".

## Acceptance criteria
- Verify all marks every linked user whose local name differs from the directory name.
- Pulling sets the local name and clears the mark; it is refused for a placeholder directory name.
- Pushing writes the directory name and clears the mark.
- A user without the `user:manage` scope cannot call either operation (decorator test).
- Both operations are audited.
- Full test suite passes.

## Done condition
Merged to master via PR, README/docs updated where the Users page and directory features are described.
