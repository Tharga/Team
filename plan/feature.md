# Feature: refresh a user's name and email from trusted sign-ins

Issue: Tharga/Team#320. Branch: `feature/refresh-profile-from-claims` (from `master`).

## Goal

Let a host refresh a stored user's `Name` and `EMail` from the sign-in claims, for sign-ins the host trusts.
Today both are written once, at creation, and never read from the claims again.

## Scope

- `UserServiceBase.ShouldRefreshProfile(ClaimsPrincipal)` — protected virtual, default `false`. Opt-in only.
- Evaluated in the resolve path that already runs on every resolve (`TouchUserAsync`, beside the `DirectoryId`
  backfill), so it works whether or not the user is cached.
- When it returns true and a non-empty claim value (display name, email — the same helpers creation uses)
  differs from the stored one: `SetUserProfileAsync(userKey, name, email)`, then the user's cache entry is
  dropped and `UserProfileRefreshedEvent` is raised. Empty claim values never overwrite.
- One attempt per distinct value per user per process, so a store that does not persist does not write and
  invalidate on every request.
- `UserServiceRepositoryBase.SetUserProfileAsync` writes through `IUserRepository`: the existing `SetNameAsync` and
  a new `SetEMailAsync`. `IUserRepository.SetEMailAsync` has a default body that throws (the `DeleteAsync`
  precedent: silently skipping would hide a missing implementation); the built-in `UserRepository` implements it.
- `UserServiceBase.SetUserProfileAsync` default throws `NotSupportedException` for the same reason; a failure is
  logged and never fails the resolve.
- Audited: `AuthAuditEntries.UserProfileRefreshed`, subscribed in `ThargaBlazorRegistration` exactly like
  `UserCreated`, never failing the sign-in.
- Docs: `docs/articles/user-management.md` and `README.md` where they cover user records.

## Out of scope

- #316 (administrator sets an email) — shares `SetEMailAsync`, otherwise separate. With refresh off, its rule that an
  administrator-set address stays still holds.
- Writing anything back to the directory.

## Acceptance criteria

1. Unconfigured hosts behave exactly as before: no reads of the claims for profile, no writes.
2. Opted in: a differing non-empty name or email from the claims is written, the cache entry dropped, the event
   raised and audited; equal or empty values cause no write.
3. A store that cannot persist (`SetEMailAsync` not implemented) logs a warning, does not fail the resolve, and is
   not retried on every request.
4. Full test suite green, counted from the test executables (local SDK 10.0.301 reports zero tests otherwise).

## Done when

PR to `master` opened after the close-out, #320 answered with what shipped.
