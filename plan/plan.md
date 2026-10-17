# Plan: Require every new team API key to be private

Branch: `feature/require-private-keys` (from `master` at 44dbf00; GitHub Actions flow).

## Steps

- [x] 0. NuGet updates: re-checked at the 4.0 close-out on 2026-10-17 and nothing was outdated. Re-run once and commit only if something changed.
  - Re-run 2026-10-17: nothing outdated, so there's no deps commit.
- [x] 1. `ApiKeyOptions.RequirePrivateKeys` with docs. Enforce it in `ApiKeyAdministrationService.CreateKeyAsync` (D1, D2). Tests on both services, with the option on and off.
  - `PrivateKeyRequirement` (internal) holds the check and both messages. `ApiKeyAdministrationService.CreateKeyAsync` checks first. `ApiKeyManagementService` takes an optional `IOptions<ApiKeyOptions>` and checks before resolving the owner, so a host's own administration service doesn't open a gap. Simple-mode auto-creation also makes no keys while the option is on, as a backstop to step 2.
- [x] 2. Startup validation: option on with `AdvancedMode = false` throws, naming both options (D3). Tests.
  - `RequirePrivateKeysCheck` (`IHostedService`) is registered once by `AddThargaApiKeyAuthentication` through `TryAddEnumerable`, and reads the bound options. `RequirePrivateKeysTests` has 13 cases, all green.
- [x] 3. `ApiKeyView`: the create card forces private (switch on and disabled, with a caption), and private keys are listed as at least `Mine`. Tests where the existing view tests reach.
  - The decisions live in `ApiKeyPrivacy` (internal, static, `Framework/`): `Listed` (None becomes Mine when required), `ShowSwitch`, and `CreatesPrivate`. `ApiKeyPrivacyTests` has 12 cases. The switch is the same single control, locked on when required; it reuses the existing caption and tooltip because `ApiKeyView` is still pending in the #204 text ratchet (44 literals), and a new literal fails that test. A "required" caption waits for the view's catalogue migration. Also fixed the stale `AllowPrivilegedAccess` doc (Developer role → `apikeys:oversee`).
- [x] 4. Sample: a commented line showing the option.
- [x] 5. Full test suite; commit; push for testing (no PR yet).
  - Blazor 1452, Service 1256, MongoDB 190, Support 402, Email 189, Mcp 116, Entra 58, Images 11: 3674 in all, none failed or skipped.

## Notes

- 2026-10-17 (user): "lets continue", so D1–D3 are confirmed as proposed (refuse, enforce at the administration layer, fail at startup in simple mode).

## Last session

2026-10-17: all steps are done and pushed for testing. Next: the user tests in the sample (uncomment `o.ApiKey.RequirePrivateKeys = true`: the create card's Private switch is locked on, new keys are listed, and the tagged-key buttons are refused). Then close-out: NuGet re-check, docs, version line `4.1`, archive the plan, remove `plan/`, and open the PR closing #314.

## README / docs changes needed at close

- `docs/articles/implementation-guide.md` → *Private (owner-scoped) keys*: the option, enforcement, the simple-mode conflict.
- `Tharga.Team.Service/README.md`: the `ApiKeyOptions` table.
- `.github/workflows/build.yml`: `MAJOR_MINOR: '4.1'`.
