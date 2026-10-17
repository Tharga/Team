# Plan: Personal API keys, for team and system keys

Branch: `feature/require-private-keys` (from `master` at 44dbf00; GitHub Actions flow). The branch name
comes from the first scope. It stays as is: renaming a pushed branch means deleting the remote one.

## Steps

- [x] 0. NuGet updates: re-run 2026-10-17, nothing outdated, so there's no deps commit.
- [x] 1. Revert the "require every key to be private" commit (d9e49d5 → revert 481db00).
  - The user re-scoped #314: "I do not want every new api key to be private, I want the option to create private api keys. It should be a checkbox."
- [x] 2. `ApiKeyView`: `ShowPrivateKeys` defaults to `Mine`, the switch becomes a checkbox, and the parameter doc says so. Restore the `AllowPrivilegedAccess` doc fix (Developer role → `apikeys:oversee`). A test pins the default.
  - The checkbox reuses the existing "Private" label and "only me" caption, so the #204 text ratchet for `ApiKeyView` stays at 44. `ApiKeyViewPrivateKeysTests` pins the `Mine` default and the checkbox. Only the sample embeds `ApiKeyView`, and it shows the checkbox with no change.
- [x] 3. Full test suite; commit; push for testing (no PR yet).
  - Blazor 1442, Service 1243, MongoDB 190, Support 402, Email 189, Mcp 116, Entra 58, Images 11: 3651 in all, none failed or skipped.
- [x] 4. Options: `ApiKeyOwnership` (`Both` default, `Shared`, `Personal`), `ApiKeyOptions.TeamKeys` / `SystemKeys`. `ApiKeyManagementService.CreateKeyAsync` enforces `TeamKeys`. Tests.
  - `ApiKeyManagementService.ApplyOwnership` is shared with the system service. `ApiKeyOwnershipTests` (8). The `GetKeysAsync` doc no longer claims the Developer role sees every private key.
- [x] 5. Personal system keys: `IApiKeyAdministrationService.CreatePersonalSystemKeyAsync` (default throws) in the store and both decorators; refresh keeps the owner; `ISystemApiKeyManagementService` list/create parameters, owner-or-oversee guard, `SystemKeys` enforcement; authentication owner check; cleanup on user deletion. Tests.
  - The caller's user key is the member key claim when a team is selected, else `IUserService.GetCurrentUserAsync`; a caller authenticated by an API key is never an owner. `UserManagementService` takes the key store and `OwnerRemovedAction` (registered in `ThargaBlazorRegistration`). Authentication adds the `ApiKeyOwner` claim to a personal system key.
  - Tests: `PersonalSystemKeyStoreTests` 8, `SystemApiKeyPersonalKeysTests` 28, `PersonalSystemKeyAuthenticationTests` 6, `PersonalSystemKeyUserDeletionTests` 3. Service 1296, Mcp 116, all green.
  - Seen, not changed: the MCP `team://team/apikeys` and system key resources list every key, private ones included (metadata only, no secret). Personal system keys inherit that. `ApiKeyAuthenticationHandler` logs disabled-key and deleted-team failures with the key id and team key swapped (lines ~170 and ~176).
- [x] 6. UI: Personal checkbox (no "Private" heading) and Personal badge in both views, gated by the options; `SystemApiKeyView.ShowPrivateKeys` (default `Mine`).
  - `PersonalKeyChoice` (internal) holds the three rules both views share: when the checkbox shows, which personal keys are listed, and whether a new key is requested as personal. The checkbox is a label beside the box, sized to sit level with the inputs.
- [x] 7. Translations: `ApiKeyViewText`, `SystemApiKeyViewText`, `ApiKeyRevealDialogText`; move the three components from Pending to Migrated.
  - `SystemApiKeyView` resolves `ApiKeyViewText` too and keeps only what differs in its own catalogue, so a shared label is translated once. The reveal dialog loses its two bold words (a translated string can't carry markup safely). `ApiKeyViewPersonalKeysTests` (18) replaces `ApiKeyViewPrivateKeysTests`.
- [x] 8. Full test suite; commit; push for testing (no PR yet).
  - Blazor 1458, Service 1296, MongoDB 190, Support 402, Email 189, Mcp 116, Entra 58, Images 11: 3720 in all, none failed or skipped. No new build warnings.

## Notes

- 2026-10-17 (user): the first plan (D1–D3, require private keys) was built and then rejected. The user chose "Always, by default" for the checkbox, so `ShowPrivateKeys` defaults to `Mine`.
- 2026-10-17 (user): sign-in to the sample failed because its MongoDB client had stopped connecting about 15 minutes after startup (an empty server list, while mongod was fine). A restart fixed it; the cause is unknown and outside this branch.
- 2026-10-17 (user): expanded scope. A host option for team and system keys (shared / personal / both, default both); "Private" heading removed and "only me" becomes "Personal"; translatable like everything else. The user chose to migrate both key views fully (81 strings), not just the Personal strings.
- Decisions taken without asking, following the team-key precedent: `Shared` refuses a personal request (fail closed); `Personal` forces personal; personal system keys stop with their owner (#323) and are cleaned up on user deletion using `OwnerRemovedAction`; the list badge also becomes "Personal".

## Last session

2026-10-17: steps 0–8 done and pushed for testing. Next: the user tests in the sample (both API key pages: Personal checkbox, Personal badge, options `TeamKeys` / `SystemKeys`). Then close-out: NuGet re-check, docs, version line `4.1`, a proposed #314 comment, archive the plan, remove `plan/`, and open the PR.

## README / docs changes needed at close

- `docs/articles/implementation-guide.md` → *Private (owner-scoped) keys*: the options, personal system keys, the `ShowPrivateKeys` default (`Mine`) and the Personal checkbox; rename the wording to "personal" where it describes the UI.
- `Tharga.Team.Blazor/README.md`: the `ShowPrivateKeys` default, and `SystemApiKeyView.ShowPrivateKeys`.
- `Tharga.Team.Service/README.md`: personal system keys and the options.
- `.github/workflows/build.yml`: `MAJOR_MINOR: '4.1'`.
- PR / release notes: the upgrade notes in `feature.md`.
- #314's text describes the rejected scope. Propose a comment for the user to confirm before posting (public repo).
