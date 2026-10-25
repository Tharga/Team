# Feature: API-key authentication without a PBKDF2 pass per candidate (#387)

GitHub: #387. Branch: `fix/api-key-auth-performance`, from master after #385.

Priority tier 2 (a consumer cannot work around it), with a tier-1 edge: an unknown key makes the server
PBKDF2-verify every stored key, a CPU amplification anyone on the internet can trigger.

## Goal

A request with a valid API key costs a few indexed reads and, after its first use, no PBKDF2 work. An unknown
key costs no PBKDF2 work over keys that have a lookup value.

## What the code does today (verified 2026-10-25)

- A key is `base64(escape(teamKey) + ":" + secret)` (`Tharga.Toolkit` `ApiKeyService.BuildApiKey`). The stored
  `ApiKeyPrefix` is its first 8 characters, which encode the first 6 bytes of the **team key** (or `system`). The
  issue says "name"; it is the team key. So the prefix finds every key of the team, and every system key shares one
  prefix (`c3lzdGVt`).
- `PickOneOrDefault` runs the predicate over every candidate, so each one costs a PBKDF2 verification
  (10,000 iterations), including after the match.
- No match by prefix falls back to `_repository.GetAsync()` and verifies every key in the store.
- Nothing is cached.

## Design

**Lookup value.** New and refreshed keys store, in `ApiKeyPrefix`, the first 16 bytes of the key's SHA-256 as 32
hex characters. It can never equal a legacy 8-character prefix, so one indexed field serves both. A fast hash next
to the PBKDF2 one is safe here: every secret is at least 24 random base62 characters (≈143 bits, enforced by
`ApiKeyOptions.KeyLengthFloor`), out of reach at any hash speed. PBKDF2 stays the verifier.

**Lookup, in order, stopping at the first verified match:**
1. Keys stored with the presented key's lookup value. Normally one.
2. Keys stored with the legacy 8-character prefix. On a match, rewrite that key's prefix to its lookup value
   (targeted field update, best-effort like `TouchLastUsedAsync`), so an existing key moves to step 1 on first
   use. No rotation needed.
3. Keys stored with no prefix at all (`ApiKeyPrefix == null`), with the same rewrite. Replaces the scan of every
   key.

**Verification cache.** `IMemoryCache`, keyed by the presented key's SHA-256, holding the stored hash it verified
against, with a sliding expiry `ApiKeyOptions.VerificationCacheDuration` (default 10 minutes, zero turns it off).
Only successes are cached. The entry records a fact that never stops being true, that this key verifies against
this hash, and the key is still read from the store on every request. So disable, delete, scope and role
changes, expiry and the owner checks take effect at once. A refresh mints a new salt, so its hash misses the cache.
There is no eviction to wire up, and it holds across several app instances.

**Duplicates (#64).** After a match, other candidates with the identical stored hash are logged as duplicates. A
string comparison, so the warning survives without verifying every candidate.

**Repository.** `IApiKeyRepository.SetPrefixAsync(key, prefix)`, a targeted update. A full replace through
`UpdateAsync` could race with a disable and quietly re-enable the key.

## Residual, stated rather than fixed

An old key that is never used after the upgrade keeps its legacy prefix, so an unknown key carrying that team's
prefix still costs one PBKDF2 per such key until it is used or refreshed. The set only shrinks.

## Done when

- A repeat request with a valid key does no PBKDF2 work while cached.
- A first request verifies at most one candidate for a key minted after the change, or for an existing key
  after its first use.
- Disable, delete, refresh and scope changes take effect on the next request.
- An unknown key verifies no key that has a prefix; `GetAsync()` is no longer on the authentication path.
- Warning baseline (14) holds and the full suite passes.
