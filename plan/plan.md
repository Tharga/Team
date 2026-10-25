# Plan: API-key authentication performance (#387)

- [x] 1. Package updates: `dotnet outdated` over the solution (2026-10-25). Nothing outdated, so no `chore(deps)` commit.
- [x] 2. Tests first, failing on master: `ApiKeyLookupTests` (16), against the real `Tharga.Toolkit` hasher wrapped in a verification counter and an in-memory repository.
  - On the old lookup 12 failed, e.g. an unknown key cost 20 verifications for 10 stored keys (the team's prefix, then the full scan). The 4 that passed are the fresh-read guarantees (refresh, disable, delete, scope change) and must keep passing.
- [x] 3. `IApiKeyRepository.SetPrefixAsync`, a targeted `$set` like `SetLastUsedAsync`. `GetByPrefixAsync(null)` documented as "keys stored without one".
- [x] 4. Lookup value: `GetLookupValue` = first 16 bytes of SHA-256 as lower-case hex (32 chars), stored in `ApiKeyPrefix` by `BuildKey` and `BuildSystemKey`; `ApiKeyPrefix` XML doc rewritten.
  - Decision: reuse `ApiKeyPrefix` rather than add a field. Same purpose (indexed narrowing), the existing sparse index serves both, and the lengths (32 vs 8) keep the two kinds apart.
- [x] 5. `GetByApiKeyAsync`: lookup value, then legacy prefix, then no prefix; first verified match wins; a match from the last two is moved to its lookup value (best-effort, logged on failure). Duplicates by identical hash still go through `PickOneOrDefault`, so the #64 warning survives at the cost of a string comparison.
  - The full read (`GetAsync()`) is gone from authentication. Checked that the prefix computation never changed (`git log -L`, unchanged since the copy from Tharga.Api), so a key whose prefix does not match its own first 8 characters cannot exist; keys without a prefix are covered by the third query.
- [x] 6. Verification cache: optional `IMemoryCache` constructor parameter, `ApiKeyOptions.VerificationCacheDuration` (default 10 min sliding, `Zero` = off), `AddMemoryCache()` in `AddAuditedApiKeyAdministrationService` (TryAdd, so a host's own cache wins). Entries set `Size = 1`, so a host cache with a `SizeLimit` does not throw.
- [x] 7. Checked end to end in the scratchpad (not committed):
  - Local MongoDB, throwaway database, dropped afterwards: `{ApiKeyPrefix: null}` from the same expression the repository uses finds only the document stored without the field; the legacy and lookup values find theirs; the targeted update moves the document.
  - Timing, 50 keys in one team, real PBKDF2: published 4.4.0 vs this branch: first request 207 ms → 4.2 ms, repeat 197 ms → 0.0 ms, unknown key 375 ms → 0.0 ms (medians of 5).
- [x] 8. Full build at the warning baseline (14; a `<param>` tag on the constructor first raised it to 18 via CS1573 and was folded into the summary). Full suite green: Service 1487 (+17), Blazor 2236, Email 985, Support 814, MongoDB 468, Mcp 121, Entra 58, Support.Anthropic 12, Images 11.
- [x] 9. Docs: `VerificationCacheDuration` in the Service README options table and the implementation guide's options block; a short "What authenticating a key costs" section in the Service README.
  - Found stale and fixed: the README listed the key-length defaults as `24` / `32` with a random length; the code defaults to a fixed 32 (`MaxKeyLength = null`).

## Notes

- Public surface: `IApiKeyRepository` gains `SetPrefixAsync` (implemented internally; a host implementing the interface itself would need it), `ApiKeyAdministrationService` gains an optional constructor parameter, `ApiKeyOptions` gains `VerificationCacheDuration`. Additive, so a minor version.
- Residual, documented in the README: an old key never used after the upgrade keeps its team-shared prefix, so an unknown key carrying that prefix still costs one PBKDF2 per such key until it is used or refreshed.

## Last session

2026-10-25: all steps done and committed on `fix/api-key-auth-performance`, not pushed. Next: push for testing on
the user's word, then close out (archive `feature.md`, remove `plan/`, PR with release notes, comment on and close #387
once released).
