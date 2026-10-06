# Feature: configurable user identity claim

## Goal
Let a consumer choose which claim identifies a user, so a host on Microsoft Entra can key users on the
tenant-wide `oid` instead of the pairwise `sub`.

## Why
The user identity is resolved by `GetIdentity()` from Tharga.Toolkit: NameIdentifier, sub, oid, nameid, uid,
first non-empty wins. With OIDC inbound claim mapping NameIdentifier is filled from `sub`, and in Entra
(workforce and External ID) `sub` is pairwise: unique per user and application. Replacing the app registration
therefore re-keys every user (orphaned TeamUser/member records, duplicate users), and two applications in one
tenant can never share user records. `oid` is the same for every application in the tenant.

## Scope
- `UserIdentityResolver` in Tharga.Team: one place that turns a principal into the user identity and the
  audit subject. Unconfigured, it reproduces today's behaviour exactly at every site.
- `ThargaTeamOptions.UserIdentityClaimTypes`: ordered claim types; `oid` matches both the raw and the mapped
  (`http://schemas.microsoft.com/identity/claims/objectidentifier`) form.
- Every place Team derives a user identity from a principal goes through it: user resolve + cache
  (UserServiceBase), find-or-create (UserServiceRepositoryBase), TeamAuthorizer.GetSubjectAsync (support
  cases), audit CallerUserIdentity (AuditHelper, AuthAuditEntries sign-in and user-created), MCP UserId.
- README documentation of the option and why.

## Out of scope
- Display-name chains (`CallerIdentity`, `GetDisplayNameAsync`) - human-readable, not keys.
- Migrating existing user records from `sub` to `oid` - a host decision; documented.
- Changing Tharga.Toolkit.

## Acceptance criteria
- Default behaviour unchanged (existing tests green, explicit default tests).
- Configured `oid` is chosen over `sub`; mapped and unmapped forms both work.
- A principal with different sub and oid resolves to the oid-keyed identity in the user repository, the
  claims path (GetCurrentUserAsync), the authorizer subject, audit entries and MCP.

## Done condition
All tests pass, docs updated, PR open to master.
