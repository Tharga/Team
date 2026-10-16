# Feature: Sign-in methods in UsersView

Source: [Tharga/Team#322](https://github.com/Tharga/Team/issues/322)

## Goal

Show an administrator **how each user signs in** (email + password, a SAML IdP, a custom OIDC provider such
as an e-ID broker) in `UsersView`, with labels the host defines and a bounded number of directory calls.

## Scope

- **A batch identity read on the directory.** `IUserIdentityDirectory` gains a read for many directory ids at
  once. It is a default interface member that falls back to one call per id, so custom directories keep
  compiling and working. `EntraUserDirectoryService` overrides it with
  `GET /users?$filter=id in (...)&$select=id,identities`, chunked at Graph's 15-value limit for `in`.
- **One gated read for the component.** `IUserManagementService.GetSignInIdentitiesAsync(userKeys)` with
  `[RequireScope(users:manage)]`. It resolves user key → directory id and makes one batch call. The component
  never injects the unchecked `IUserIdentityDirectory` (architecture rule 2: one enforcement point). It returns
  a concrete record array, not an interface or `IAsyncEnumerable` (rule 3).
- **Host-defined labels.** `o.Blazor.SignInMethods.Add(identity => ... ? new SignInMethod("e-ID", iconUrl) : null)`.
  The first match wins. Unmatched `federated` identities fall back to the issuer's host; `emailAddress` falls back
  to "Email"; `userPrincipalName` is hidden by default (option to show it). Duplicate labels collapse.
- **A "Sign-in" column in `UsersListView`**, shown when the viewer passes the existing directory gate
  (`users:manage` + a configured directory) **and** an `IUserIdentityDirectory` is registered. It renders chips
  per method. Identities load **for the rows on the visible page only** (page size 10, within one chunk), cached
  per user, and are refreshed on page, sort and filter changes. A user with no directory id shows "Not linked".
- Localizable strings go in `UsersListViewText` and follow the text-coverage tests.

## Out of scope

- Changing identities (read-only display only, as the issue says).
- **The "has sign-in method X" filter (the issue's optional item 4).** Filtering needs every user's identities
  up front, which conflicts with the bounded-call acceptance criterion. Offer it as a follow-up issue.

## Acceptance criteria

- The column appears only when a directory with identities is registered and the viewer holds `users:manage`.
- Labels and icons come from the host's mapping; nothing provider-specific is built into the library.
- Rendering a page of the list makes a bounded number of directory calls (one per visible page), independent
  of the number of users.
- The read is authorized in the service layer (`users:manage`); a caller without the scope is refused.
- A directory failure degrades the column (an error chip or tooltip), not the whole grid.
- Tests cover the mapping, the fallbacks, the gate, the Entra batch request and chunking, the default
  interface fallback, and the service's scope and key→directory-id mapping.

## Done condition

All acceptance criteria are met, the full test suite passes, the docs are updated (`docs/articles/user-management.md`,
`Tharga.Team.Entra/README.md`, the Blazor README if it lists UsersView options), and #322 is answered and closed.
