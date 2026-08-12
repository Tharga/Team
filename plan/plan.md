# Plan: AuditLogView caller-name source (#222)

Branch: `feature/audit-caller-name-gate` (from `master`).

## Steps

- [x] 1. **Package updates up front.** `dotnet list package --outdated` across the whole solution reports
      nothing outstanding — 10.0.11 / NSubstitute 6.2.0 went in with the soft-delete feature two commits ago
      and nothing has published since. Nothing to apply.
- [x] 2. **Routed through `UserDirectoryGate`**, exactly as `TeamComponent` has since #139. No new
      dependency — `AuthStateProvider` was already injected.
- [x] 3. **Cache build is non-fatal.** A cosmetic lookup cannot decide whether the audit log renders, and
      this also covers a host's own `IUserService` failing for a reason no gate could predict — the half of
      #222 the gate alone does not cover.
- [x] 4. **Tests: a guard over the whole class**, not this instance. `FullDirectoryReadGuardTests` asserts
      every component reading the full directory decides on `SystemUserScopes.Manage` first — either through
      the gate, or by gating its whole surface as the two `UsersView` tabs do. Verified by removing the real
      decision and watching it name `AuditLogView.razor.cs`, not merely against a fixture.

      **The first version of the guard was case-sensitive and missed the very file it was written for** —
      `AuditLogView` calls through a lowercase local (`userService.GetAsync()`), `TeamComponent` through an
      injected property (`UserService.GetAsync()`). Caught by its own self-check, which is the argument for
      writing one.
- [x] 5. **Sweep done — there is no third instance.** `TeamComponent` (#139) and `AuditLogView` (#222) are two of a
      shape: *a team-scoped surface whose behaviour is decided by a system-scoped call*. Grep the remaining
      `UsersListView` and `TeamsListView` also call `GetAsync()`, but both gate on `users:manage` before
      loading and render an explanatory alert otherwise — correctly gated, not the defect. `UserManagementService`
      is a service behind its own decorator, not a component.

      So the class is exactly two, both now fixed, and the guard from step 4 is what keeps it at two. That
      changes the argument for the Roslyn analyzer on the roadmap: it is prevention rather than a live
      bleed, which makes it less urgent than it looked.
- [ ] 6. `dotnet build -c Release` + `dotnet test -c Release`, full suite.
- [ ] 7. Docs — only if the sweep or the fix changes anything a consumer configures. Likely none: this
      restores intended behaviour rather than adding surface. **Say so explicitly rather than skipping
      silently.**
- [ ] 8. Commit, push, ask the user to verify.

## Close-out (only once the user confirms)

- [ ] 9. Re-check package updates.
- [ ] 10. Close the records: **GitHub #222**, `Requests.md`, and `$DOC_ROOT/Eplicta/requests.md` — FortDocs'
      own file, where this is recorded as customer-visible. Their entry asks whether widening their
      `users:manage` grant is needed; answer it — **it is not, and doing so would be the wrong fix**.
- [ ] 11. **Update the roadmap.** `#222` is the entry point of the Access correctness lane and the only item
      marked *broken now*; leaving it drawn that way is exactly the drift the new `## Roadmap` instruction
      warns about. Regenerate from the sweep rather than editing the page, and keep the same URL.
- [ ] 12. Archive `feature.md` to the Plan directory `done/`, `git rm -r plan`, close-out commit, PR.

## Version

`MAJOR_MINOR` stays **3.13**; this is a defect fix with no new public surface, so it releases as **3.13.2**.

## Last session

2026-08-12 — branch created, packages verified current (step 1 done). Issue read in full; the fix pattern
and both helper members confirmed present in the codebase. Plan awaiting confirmation before step 2.
