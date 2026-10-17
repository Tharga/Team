# Plan: Require every new team API key to be private

Branch: `feature/require-private-keys` (from `master` at 44dbf00; GitHub Actions flow).

## Steps

- [ ] 0. NuGet updates: re-checked at the 4.0 close-out on 2026-10-17 and nothing was outdated. Re-run once and commit only if something changed.
- [ ] 1. `ApiKeyOptions.RequirePrivateKeys` with docs. Enforce it in `ApiKeyAdministrationService.CreateKeyAsync` (D1, D2). Tests on both services, with the option on and off.
- [ ] 2. Startup validation: option on with `AdvancedMode = false` throws, naming both options (D3). Tests.
- [ ] 3. `ApiKeyView`: the create card forces private (switch on and disabled, with a caption), and private keys are listed as at least `Mine`. Tests where the existing view tests reach.
- [ ] 4. Sample: a commented line showing the option.
- [ ] 5. Full test suite; commit; push for testing (no PR yet).

## Notes

## Last session

2026-10-17: plan written and waiting for confirmation of D1–D3.

## README / docs changes needed at close

- `docs/articles/implementation-guide.md` → *Private (owner-scoped) keys*: the option, enforcement, the simple-mode conflict.
- `Tharga.Team.Service/README.md`: the `ApiKeyOptions` table.
- `.github/workflows/build.yml`: `MAJOR_MINOR: '4.1'`.
