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
- [ ] 4. **The model.** `IsDeleted` / `DeletedAt` / `DeletedBy` on the team entity, stored by name (the
      persistence rule — enums by name, and a `DateTime?` needs no attribute). `TeamDeleteMode` option,
      defaulting to `Soft`.
- [ ] 5. **The seam.** `RestoreTeamAsync` and `PurgeTeamAsync` as `virtual` members on `TeamServiceBase`
      with working defaults — **never `abstract`**, or every derived host breaks at compile time in a patch.
      `DeleteTeamAsync` becomes soft when the mode says so.
- [ ] 6. **`teams:purge` scope**, registered beside `teams:delete`, enforced in
      `AuthorizationTeamServiceDecorator` — the single enforcement point, where the other team mutations are
      already gated.
- [ ] 7. **The read-path sweep.** `GetTeamsAsync`, `GetAllTeamsAsync`, `GetTeamByKeyAsync`,
      `GetConsentedTeamsAsync`, `TeamStateService`/`TeamSelector`, the MCP `team://` resources, and the
      claims pipeline. **Filtering goes behind the port**, not into the Mongo implementation.
- [ ] 8. **The enumerating guard.** A test that walks the read surface and fails when a read is added
      without filtering — with a fixture proving it catches a violation, or it passes forever while
      checking nothing. This is what the patch-release risk rests on.
- [ ] 9. **Key reservation.** Creating a team whose key belongs to a soft-deleted team is refused, and the
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
