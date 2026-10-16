# Plan: Sign-in methods in UsersView (#322)

Branch: `feature/user-sign-in-methods` (from `master`, GitHub Actions flow).

## Steps

- [x] 0. NuGet updates — `dotnet outdated` on the whole solution: nothing outdated (2026-10-16), no deps commit.
- [x] 1. Batch read on `IUserIdentityDirectory` (default interface member, falls back to per-id calls) + result record. Tests for the fallback.
  - Named `GetIdentitiesByIdsAsync`, not an overload of `GetIdentitiesAsync`, so a `null` argument is never ambiguous. Unknown ids (the single read's `InvalidOperationException`) are skipped; other failures propagate. Returns the concrete `DirectoryUserIdentities[]` (target rule 3). Tests: `UserIdentityDirectoryBatchFallbackTests`, using a hand-written fake, since a substitute would replace the default member.
- [x] 2. Entra override: `$filter=id in (...)&$select=id,identities`, chunked at 15; missing ids yield empty. Tests on the request URL, chunking and parsing.
  - Follows `@odata.nextLink`, returns results in the order requested, escapes `'`. 7 new tests in `EntraUserDirectoryServiceTests`; 58/58 pass.
- [~] 3. `IUserManagementService.GetSignInIdentitiesAsync(userKeys)` with `[RequireScope(users:manage)]`, in the implementation and the authorization decorator. Tests: scope refusal, key→directory-id mapping, users without a directory id.
- [ ] 4. `SignInMethod` + `SignInMethodOptions` on `ThargaBlazorOptions`, registered as a singleton; a pure `SignInMethodResolver` (first match, federated → issuer host, emailAddress → "Email", UPN hidden, dedupe). Tests.
- [ ] 5. `UsersListView` "Sign-in" column: gate, lazy per-visible-page load with a cache, refresh on page/sort/filter, degraded rendering on failure. Text keys + coverage. Gate tests.
- [ ] 6. Sample site: map an example provider so the column is visible.
- [ ] 7. Run the full test suite; commit.
- [ ] 8. Push the branch for the user to test (no PR yet).

## Notes

- Decided from the issue: the filter (optional item 4) is out of scope for this feature; propose a follow-up.

## Last session

2026-10-16 — Branch created, no NuGet updates available, plan written. Next: confirm the plan, then step 1.

## README / docs changes needed at close

- `docs/articles/user-management.md` — the batch read, the column and the `SignInMethods` option.
- `Tharga.Team.Entra/README.md` — the batch identity read and its Graph call.
- `Tharga.Team.Blazor/README.md` — if it lists UsersView options.
