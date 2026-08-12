# Plan: soft-delete teams + fix the delete path (#224)

Branch: `feature/team-soft-delete` (from `master`).

## Order

The two cheap fixes from #224 first — they stand alone, help FortDocs even if soft delete slipped, and
put the readable-error and safe-ordering machinery in place before purge needs it. Then the model, then
the read-path sweep, then the UI.

## Steps

- [x] 1. **Package updates up front.** Applied across the whole solution: `Microsoft.*` 10.0.10 → 10.0.11
      (8 projects) and `NSubstitute` 6.1.0 → 6.2.0 (5 test projects). Build clean, full suite green,
      `dotnet list package --outdated` now reports nothing.
- [x] 2. **#224 defect 1 — a readable failure.** New `TeamStorageException` in `Tharga.Team` (additive),
      carrying the team key and the store's own exception. The adapter wraps a drop refusal with a message
      naming what the *deployment* must grant — Atlas `readWriteAnyDatabase` does not include
      `dropDatabase`. Deliberately **not** `UnauthorizedAccessException`: a missing scope and a missing
      database privilege are different failures with different fixes, and collapsing them sends an operator
      to the wrong place.
- [x] 3. **#224 defect 2 — safe ordering.** The team record is deleted *before* the database is dropped.
      Pinned by `TeamDeleteOrderingTests` with a journal recording the actual order, including the case
      that matters: when the drop fails, the record is already gone, so what survives is an orphaned
      database rather than a live team pointing at deleted data. **The ordering is the half that would have
      lost data** — FortDocs only reported an error-handling problem because the drop happened to throw
      first. 66 tests in that project, up 4.
- [x] 4. **The model.** `DeletedAt` / `DeletedBy` on `ITeam` as **default interface members** (the pattern
      `ConsentedRoles` already uses, so a host implementing `ITeam` directly keeps compiling), and on
      `TeamEntityBase` as `BsonIgnoreIfNull` properties — a live team's document is byte-identical to one
      written before the feature, so nothing migrates. **`IsDeleted` is derived from `DeletedAt`, never
      stored**: two fields that must agree eventually disagree, and then every read depends on which one it
      happened to consult. `TeamDeleteMode` enum + `o.TeamDeleteMode` defaulting to `Soft`.
- [x] 5. **The seam.** `SupportsSoftDelete`, `SoftDeleteTeamAsync`, `RestoreTeamAsync`, `PurgeTeamAsync` —
      all `virtual`, matching the `SetTeamMemberSuspendedAsync` precedent already in this class.
      **`SupportsSoftDelete` defaulting to false is what keeps this a patch**: a host that predates the
      feature cannot mark a team deleted, so its delete resolves to the hard one it already had rather than
      failing on an operation it cannot perform. `PurgeTeamAsync` defaults to the existing
      `DeleteTeamAsync`, so every store gains a working purge for free.

      The mode reaches the service through an **optional** constructor parameter read by a **virtual**
      property — neither a derived host that never passes it nor one that wants to decide the mode itself
      has to change.

      Mongo side: `SetDeletedAsync` / `GetIncludingDeletedAsync` / `GetAllTeamsIncludingDeletedAsync` on
      `ITeamRepository` as throwing default interface methods (the `GetAllTeamsAsync` precedent), and
      **four repository reads now filter `DeletedAt == null`** — `GetAsync`, `GetTeamsByUserAsync`,
      `GetTeamsByConsentAsync`, `GetAllTeamsAsync`. The unfiltered reads are separately *named* rather than
      a defaulted boolean, because one forgotten argument would resurrect a deleted team into an ordinary
      list.
- [ ] 6. **`teams:purge` scope**, registered beside `teams:delete`, enforced in
      `AuthorizationTeamServiceDecorator` — the single enforcement point, where the other team mutations are
      already gated.
- [~] 7. **The read-path sweep.** Storage-level reads are done (step 5): the four repository reads filter
      `DeletedAt == null`, which also covers **both claims paths** — membership resolves through
      `GetTeamsByUserAsync` and consent through `GetTeamsByConsentAsync`, so a soft-deleted team stops
      granting access without a special case in the claims pipeline.

      ### ✅ DEFECT FOUND AND CLOSED 2026-08-12 — the member cache kept a deleted team authorizing

      `TeamServiceBase.GetTeamMemberAsync` consults `ITeamCache.GetMemberAsync(teamKey, userKey)` **before**
      reading the store (`TeamServiceBase.cs:297`). Soft delete clears only custom roles
      (`AfterTeamRemovedFromUseAsync` → `RemoveCustomRolesAsync`), so a **cached member entry survives the
      delete and keeps that caller authorized on a deleted team until the entry expires**. The repository
      filter is never consulted, so none of step 5's work helps here.

      This is precisely the leak that makes default-on soft delete risky *as a patch* — it is silent, and it
      is an authorization failure rather than a display bug.

      **Why it is not fixed in this commit:** `ITeamCache` has no "evict every member of this team"
      operation, only per-user and per-team-custom-roles. Adding one is an interface change that must be a
      default interface method to stay non-breaking — and a **no-op default would leak silently for every
      custom cache implementation**, which is the same failure wearing a different hat. The alternatives
      (read the team including deleted and evict each member; or have soft delete write through the cache)
      each have consequences worth choosing deliberately rather than at the end of a long session.

      **Closed the same day, with no interface change.** `TeamServiceBase` reads the team's roster
      *before* the delete and evicts each member through the existing
      `ITeamCache.RemoveMemberAsync(teamKey, userKey)` — an operation every implementation already has, so
      no custom cache has to change and none can silently skip it. The ordering is required: after a soft
      delete the filtered read returns null, so there would be no roster left to evict from.

      **Restore evicts too, for the opposite reason.** `GetTeamMemberAsync` caches a miss as well as a hit
      (`cached.Found` distinguishes them), so a lookup made while the team was deleted leaves a cached
      `null` that would go on denying access to a team that is live again.

      Pinned by `SoftDeleteCacheEvictionTests` (5 tests), including one that fails if the roster is read
      after the delete rather than before — the version that would evict nothing while passing every other
      assertion.

      **Worth noting:** every other member-changing operation on `TeamServiceBase` already evicted this way;
      deletion was simply the omission. The fix follows the established pattern rather than inventing one.

- [ ] 7b. **The remaining above-store read paths.** `GetTeamsAsync`, `GetAllTeamsAsync`, `GetTeamByKeyAsync`,
      `GetConsentedTeamsAsync`, `TeamStateService`/`TeamSelector`, the MCP `team://` resources, and the
      claims pipeline. **Filtering goes behind the port**, not into the Mongo implementation.
- [ ] 8. **The enumerating guard.** A test that walks the read surface and fails when a read is added
      without filtering — with a fixture proving it catches a violation, or it passes forever while
      checking nothing. This is what the patch-release risk rests on.
- [ ] 9. **Key reservation. — CALL SITE IDENTIFIED 2026-08-12, and it is urgent.**
      `TeamCustomRolesCacheTests` documents it exactly: *"A deleted team's key is handed out again —
      `GetRandomUnsusedTeamKey` only checks that no team currently holds it."* That check now reads through
      the filtered team read, so **a soft-deleted team is invisible to it and its key will be reissued** —
      pointing a brand-new team at the deleted team's database in a `DatabasePart` deployment, which is the
      corruption #224 worries about arriving by another route. `GetRandomUnsusedTeamKey` must consult
      `GetIncludingDeletedAsync`. This is not optional and it is the reason keys stay reserved.

- [ ] 9b. **Key reservation on explicit create.** Creating a team whose key belongs to a soft-deleted team is refused, and the
      message names that team so the operator can restore or purge instead of guessing.
- [ ] 10. **Audit.** Delete, restore and purge each audited with metadata, via the existing
      `AuditingTeamServiceDecorator`.
- [ ] 11. **UI.** Restore and purge on the `UsersView` Teams tab, gated on the scopes from step 6; a
      soft-deleted team shown as such rather than hidden from an operator who holds `teams:delete`.
- [ ] 12. `dotnet build -c Release` + `dotnet test -c Release`, full suite.
- [ ] 13. Docs: the team lifecycle, the new scope, the option, and **the `dropDatabase` requirement stated
      explicitly on Purge** — #224 asks for that by name. Land as a `docs:` commit.
- [ ] 14. Commit, push, ask the user to verify.

## Close-out (only once the user confirms)

- [ ] 15. Re-check package updates.
- [ ] 16. Close the records: **GitHub #224**, the **Soft-delete teams** backlog entry, `Requests.md`, and
      `$DOC_ROOT/Eplicta/requests.md` — FortDocs' own file, where #224 is recorded as blocking their CI.
      Their entry also asks whether the wide Atlas grant is still needed; answer it.
- [ ] 17. Archive `feature.md` to the Plan directory `done/`, `git rm -r plan`, close-out commit, PR.

## Standing constraints for this feature

- **No new `abstract` member on `TeamServiceBase`.** It breaks every derived host at compile time, which a
  patch must not do. `virtual` with a working default, as the cross-team visibility work did.
- **`MAJOR_MINOR` stays `3.13`** — the user's call. Ships as 3.13.1.
- **The read-path sweep is the risk.** Soft delete on by default in a *patch* is only honest if no read
  path leaks a deleted team. Step 8 is not optional and not a formality.
- **Purge states its privilege requirement in the docs**, per #224's closing paragraph.

## Version

`MAJOR_MINOR` stays **3.13**; this releases as **3.13.1**. Deliberate — see `feature.md`.

## Last session

2026-08-12 — branch created, packages updated (step 1 done, suite green), decisions confirmed with the
user: soft delete default-on shipped as a patch, `teams:purge` as a distinct scope with restore under
`teams:delete`, and keys reserved until purge. Plan awaiting confirmation before step 2.
