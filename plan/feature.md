# Feature: a team's credentials and data do not outlive the team

**Type:** fix (security)
**Branch:** `feature/purge-cascade`
**Backlog:** *"Purging a team leaves its API keys behind…"* in `Toolkit/Team.md`
**Target release:** 3.15.x

## Goal

Deleting a team stops its API keys working. Purging a team destroys the data the toolkit holds for it.

## What investigation found, and why the order changed

The backlog entry described a purge-and-reuse crossover. Reading `ApiKeyAuthenticationHandler` turned up a
**simpler and more likely defect underneath it**, so this feature has two parts and the security one comes
first.

### Part A — an API key outlives its team (the security fix)

`ApiKeyAuthenticationHandler` validates that the key exists, that `DisabledAt` is null, and branches on
system-versus-team. **It never looks the team up.** Effective scopes come from the key's own `TeamKey`:

```csharp
foreach (var scope in await _tenantRoleService.GetEffectiveScopesAsync(key.TeamKey, accessLevel, roleNames, scopeOverrides))
```

So a key whose team has been **soft-deleted** — the ordinary `teams:delete` path, not an exotic one — keeps
authenticating and keeps carrying that team's scope claims. Every other read in the toolkit excludes
soft-deleted teams; authentication does not.

**This is the more serious half**, because it needs no purge and no key reuse: delete a team the normal way
and its credentials still work.

### Part B — purge leaves the team's data behind (the cleanup)

`PurgeTeamAsync` deletes the team record and drops the **host's** per-team database. The toolkit's own
collections are not per-team — there is exactly one `DatabasePart` usage in the repo, inside
`DropTeamDatabaseAsync` — so purge reaches none of them. Three stores are affected:

| Store | State |
|---|---|
| API keys | `IApiKeyRepository` has only `DeleteAsync(string key)` — no delete-by-team exists |
| Icon references | shared collection keyed by team |
| Support cases | `ISupportCaseStore.DeleteCasesForTeamAsync` exists and is tested; **nothing calls it** |

With Part A in place the *credential* risk is closed even if a purge is never run; Part B stops a purged
tenant's data lingering, and closes the reuse crossover completely.

## The obstacle that deferred this once, and how it is solved

Wiring a cascade into `TeamServiceRepositoryBase.PurgeTeamAsync` has nowhere to get the stores from — its
constructor is `(IUserService, ITeamRepository, IMongoDbServiceFactory, IIconStore?, ITeamCache?)`, and
adding an `IServiceProvider` or another optional store repeats the pattern that already silently disables a
feature when a subclass forgets to forward it.

**The composition root is where DI exists.** `ThargaBlazorRegistration` builds the `ITeamService` decorator
chain inside a factory with `sp` in hand (`ThargaBlazorRegistration.cs:425-439`). A purge-cascade decorator
composed there can resolve every participant, with no constructor change to any base class a host derives
from.

**Consequence to state plainly:** a host that constructs `TeamServiceBase` directly rather than resolving
`ITeamService` bypasses the cascade — exactly as it already bypasses authorization. That is the existing
trust boundary, not a new one.

## Design

- **`ITeamPurgeParticipant`** in `Tharga.Team` — a port. One method returning how many records it removed,
  plus a name for the log. Ports live in the contracts package as a namespace; this is one.
- **Participants** for API keys, icon references and support cases, registered by the packages that own each
  store.
- **`PurgeCascadeTeamServiceDecorator`** wrapping `ITeamService`, running participants **before** delegating
  to purge.

**Ordering and failure direction.** Participants run first, then the team record — the same reasoning
`DeleteTeamAsync` documents for record-then-drop: the writes cannot be atomic, so choose the failure. A
participant that throws **aborts the purge before the team record is deleted**, so the team survives and can
be purged again. Some earlier participants may already have removed their data; that is visible and
recoverable, whereas deleting the team first and then failing leaves data nothing can find or clean up.

## Acceptance criteria

- [ ] An API key belonging to a **soft-deleted** team no longer authenticates, and the refusal is audited
      with a reason that names the cause.
- [ ] An API key belonging to a **purged** team no longer authenticates.
- [ ] A **system** key — which has no `TeamKey` — is unaffected. Asserted, because it is the obvious way to
      break this fix.
- [ ] Restoring a soft-deleted team makes its keys work again, so the check follows the team's state rather
      than latching.
- [ ] Purging a team removes its API keys, icon references and support cases.
- [ ] A participant that throws aborts the purge before the team record is deleted.
- [ ] No new constructor parameter on `TeamServiceBase` or `TeamServiceRepositoryBase`.
- [ ] The `support-cases.md` limitation about purge is removed, because it stops being true.
- [ ] Full test suite green.

## Out of scope

- **Any change to how keys are minted, scoped or stored.**
- **Cascading on `RemoveUserFromAllTeams` or member removal** — this is about the team's lifetime, not a
  member's.
- **A background sweeper for already-orphaned data.** Hosts that have purged teams before this release still
  have orphans; a cleanup utility is a separate, additive piece of work. Say so in the release notes rather
  than pretending the upgrade repairs history.

## Package updates — held, standing decision

Only the xunit 4.0 / Microsoft.Testing.Platform pair. Twice backed out, and `shared-instructions.md` now
documents that `dotnet test` discovers **zero tests on Windows** with xunit.v3 4.x and exits 5. Needs its own
PR.
