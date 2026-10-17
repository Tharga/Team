# Plan: A "Private" checkbox on the API key create card, shown by default

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

## Notes

- 2026-10-17 (user): the first plan (D1–D3, require private keys) was built and then rejected. The user chose "Always, by default" for the checkbox, so `ShowPrivateKeys` defaults to `Mine`.

## Last session

2026-10-17: all steps are done and pushed for testing. Next: the user tests in the sample (the API keys page shows a Private checkbox; a key created with it ticked is private and listed). Then close-out: NuGet re-check, docs, version line `4.1`, a proposed #314 comment, archive the plan, remove `plan/`, and open the PR.

## README / docs changes needed at close

- `docs/articles/implementation-guide.md` → *Private (owner-scoped) keys*: the `ShowPrivateKeys` default is `Mine`, and the checkbox.
- `Tharga.Team.Blazor/README.md`: the `ShowPrivateKeys` default.
- `.github/workflows/build.yml`: `MAJOR_MINOR: '4.1'`.
- PR / release notes: the upgrade note in `feature.md`.
- #314's text describes the rejected scope. Propose a comment for the user to confirm before posting (public repo).
