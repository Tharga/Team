# Plan: Support case authorization

Feature scope: `plan/feature.md`. Branch `feature/support-case-authorization`, off `master` at `094ead0`.

## Status

In progress. Steps 1 through 10 done. What remains is step 11 — push the branch for testing. Do **not**
open the PR until the user confirms it works.

## Steps

- [x] **1. NuGet update pass** — `dotnet outdated Tharga.Team.sln` run 2026-09-21 before branching and
      reported *"No outdated dependencies were detected"*. No `chore(deps)` commit is needed. Re-run at
      close-out per the workflow.

- [x] **2. Scope constants** — add `support:all:read` and `support:all:manage` to `SystemSupportScopes`
      (`Tharga.Team/SystemSupportScopes.cs`), and correct the class summary, which currently says these
      scopes are for cases belonging to no team. XML docs say why the pair is separate from
      `support:unassigned:*` rather than a widening of it.
      *Tests:* none — constants only. `Tharga.Team.Support.Tests` re-run as a regression check: 363
      passed, 0 failed.
      **Done 2026-09-21.** Named `AllRead` / `AllManage`, matching the names #291 proposed, so the reporter
      finds what they asked for. Nothing enumerates the system-scope set, so no existing test moved. Two
      things recorded in the XML docs rather than left implicit: `AllManage` satisfies a read, exactly as
      `support:manage` does on a team; and it does **not** grant assignment, which stays on
      `SystemSupportScopes.Manage` because assigning decides which tenant an unassigned case joins.

- [x] **2b. Document the scope-name grammar** — added 2026-09-21 (user), after a discussion of whether a
      three-segment name was a new pattern and how it lands in the audit log.

      Written as `{feature}:{action}` and `{feature}:{reach}:{action}`. The part names are the codebase's
      own: **feature** and **action** are what `AuditEntry` already calls the two halves and what the audit
      log labels its filters, and **reach** is already the README's column heading for how far a grant
      extends. Documented in the `ScopeDefinition` remarks (the API home) and as *Naming a scope* in
      `docs/articles/implementation-guide.md` Step 6 (the prose home, where a host names its own scopes).

      Three findings recorded there rather than left to be rediscovered: `AuditEntry.ParseScope` splits on
      the **first** colon only, so `support:all:read` audits as feature `support` / action `all:read`,
      which is why reach belongs in the middle; a front-loaded `support-all:read` would fork one feature
      into two on the audit filter bar and the charts, and fall outside a `support:*` notification route;
      and `teams:read` and `apikey:system-manage` express reach two other ways, predate the grammar, and
      are staying, because a renamed scope silently authorizes nothing — the reason `RetiredScopeCheck`
      exists.

      *Tests:* none — documentation only. `Tharga.Team.Service.Tests` re-run as the check on the claims
      made about `ParseScope`: 965 passed, 0 failed.

- [x] **3. Register them** — in `SupportRegistration.AddThargaSupportCases`, beside the existing
      `AddThargaSystemScopes` block, with descriptions in the voice of the two already there.
      *Tests:* new `SupportScopeCatalogueTests` in `SupportRegistrationTests.cs` — 374 passed, 0 failed
      (11 new).
      **Done 2026-09-21.** Three guards rather than one. Every system support scope is registered *and*
      carries a description, because an undescribed scope reaches the role editor as a bare string.
      Exactly four `support:`-prefixed system scopes exist, so a refactor that collapsed the cross-team
      pair into the unassigned one — silently widening whoever holds either — fails here. And every
      support scope, team and system, parses to feature `support`: that is the regression guard for the
      naming discussion, and it is what would fail if someone rewrote `support:all:read` as
      `support-all:read` and quietly split support across two features in the audit log.
      The comment above the system-scope block said "The unassigned queue", which was no longer the whole
      truth; widened without losing its reasoning about why these are registered here.

- [x] **4. Split the gate into read and write** — give `RequireCaseAccessAsync` an explicit verb kind
      rather than inferring it from the `systemScope` argument, then order the checks as
      `feature.md` → Design → The gate describes. This step carries both #291's cross-team branch and
      the unfiled read/write defect, because they are the same six lines.
      **Decide first — carried from 2026-09-21:** whether a support audit entry records the *basis* the
      caller got in on (author / team scope / cross-team system scope). After #291 ships, a staff
      cross-team read and a team administrator's read are indistinguishable in the log —
      `AuditingSupportCaseServiceDecorator` hard-codes feature `support` and `IAuditEntryFactory.Create`
      takes no scope, so `ScopeChecked` is null on every support row and the log view renders them italic
      "not scope checked". Recommended: record the basis as a small enum rather than plumbing a scope
      string, since the check accepts several scopes and "which one matched" is less useful than "on what
      footing". The wrinkle needing a decision is that auditing wraps authorization, so the outer decorator
      does not learn what the inner one matched — closing that needs a scoped seam between the two.
      *Tests:* three added to `SupportCaseAuthorizationTests`, eight in a new `CrossTeamSupportAccessTests`
      — criteria 1, 2, 3b, 4, 5 and 8. Support 385 passed; full solution 2812 passed, 0 failed.
      **Done 2026-09-21.** `CaseAccessKind` is a required parameter with no default, so the next method
      added here cannot inherit the defect by forgetting it — the old signature inferred the verb from
      which system scope the caller passed, and the team branch never read it.

      **Ordering is the rule, not a detail.** The teamless branch runs *before* the cross-team check, so a
      cross-team grant cannot fall through onto the unassigned queue.
      `TheCrossTeamGrant_ReachesNothingInTheUnassignedQueue` is what fails if someone reorders them, and
      `TheUnassignedGrant_ReachesNothingInATeam` guards the other direction.

      **Two methods beyond the plan**, and named in `feature.md` as criterion 3b:
      `GetCasesAsync(teamKey)` and `GetAwaitingSupportCountAsync(teamKey)` now go through a shared
      `RequireTeamCaseReadAsync` that accepts the cross-team grant. Without it a staff holder could open a
      case by id but not list the team's cases, and the registered description promises listing.

      **The audit question was not built.** Step 4 shipped without it, and it did not need to block: the
      basis would be recorded at the gate's exit points, which is additive to what is there now rather
      than a restructure of it. The earlier claim that deciding late would mean rewriting the gate twice
      was overstated.

      The refusal message now names the scopes that would actually have worked for that verb. The old one
      offered `support:read` for a write, which described the defect rather than the rule.

- [x] **5. Cross-team listing** — `ISupportCaseService.GetAllCasesAsync(cursor, pageSize)` gated on
      `support:all:read`, mirroring `GetUnassignedCasesAsync`, with a matching
      `ISupportCaseStore.GetAllCasesAsync` as a **default member returning an empty page** so a store
      written before this feature keeps compiling (criterion 9). Implement in `MongoSupportCaseStore` and
      `InMemorySupportCaseStore`.
      *Tests:* four added to `CrossTeamSupportAccessTests` — criteria 3 and 9. Support 389 passed; full
      solution 2816 passed, 0 failed.
      **Done 2026-09-21.** Named `GetCasesAcrossTeamsAsync`, not `GetAllCasesAsync` as planned: it
      deliberately excludes the unassigned queue, so "all" would have been a promise the method does not
      keep and the one most likely to be misread by whoever implements the port next.

      The Mongo filter is `Exists(TeamKey, true)` — the exact complement of the unassigned one, and written
      that way for the reason already recorded beside it: `TeamKey` is `[BsonIgnoreIfNull]`, so a
      not-equal-null would also return cases written with an explicit null, which belong to the other
      queue and the other grant.

      **No `SupportContractShapeTests` entry was needed.** That class scans
      `typeof(ISupportCaseStore).GetMethods()`, so the new port member is already covered by the
      wire-shape rules rather than needing to be listed.

      `AStoreWithoutTheListing_AnswersAnEmptyPage` uses the existing `StoreWithoutReopen` double — the
      file whose whole purpose is to be "the host that must not break". Note the default member is
      reachable only through the interface, not the concrete type, which the test shows.

      **No UI.** #291 mentions a queue view; the toolkit ships plumbing and optional components, and a
      staff queue component was not in scope here. The service method is what a host needs to build one.

- [x] **6. The registration option** — `SupportCaseOptions.TeamScopeAccessLevel` (`AccessLevel?`, default
      `AccessLevel.Administrator`), honoured in `SupportRegistration`'s `AddThargaScopes` block by choosing
      `Register` or `RegisterGrantOnly`. XML docs state that the default is unchanged behaviour and that
      `null` means "grantable through a role or an override only".
      *Tests:* new `SupportTeamScopeLevelTests` in `SupportRegistrationTests.cs`, seven facts and theories
      driven through `AddThargaSupportCases` rather than a hand-built registry, so the option is proved to
      reach `AddThargaScopes`. Support 401 passed; full solution 2828 passed, 0 failed.
      **Done 2026-09-21.** One `RegisterTeamScope` helper expresses the choice once rather than an if/else
      duplicating both registrations, and the two descriptions became constants so the grant-only and
      levelled paths cannot drift apart.

      **Documented a trap the option makes reachable.** Owner and Administrator are granted every
      registered scope regardless of the declared minimum, so `AccessLevel.Owner` behaves exactly as the
      default — the values that actually differ are `Administrator`, `User`, `Viewer` and `null`. And
      `AccessLevel.Custom` must not be used to mean "nobody": it grants the scope to every level, which is
      the trap `RegisterGrantOnly` exists to avoid. Both are in the option's XML docs.

      Verified rather than assumed while writing those docs: `AuthorizationTeamServiceDecorator` really
      does refuse a tenant-defined custom role naming a grant-only scope
      (`AuthorizationTeamServiceDecorator.cs:390`), and the three scope pickers really do filter on the
      flag. The tests assert the `GrantOnly` flag, which is the input both behaviours read; each has its
      own guard already.

- [x] **7. Decorator default-member guard** — check whether `DecoratorDefaultMemberTests` (the #272 guard)
      covers the support decorators. If it does, extend it to the new members; if it does not, say so in
      the PR rather than widening scope here.

- [x] **8. Version line** — `MAJOR_MINOR: '3.21'` → `'3.22'` in `.github/workflows/build.yml`, in the same
      commit as the read/write split or later, never in a PR of its own (a merge to `master` queues a gated
      release).
      **Done 2026-09-21.** `3.21` → `3.22`; latest tag is `3.21.2` and no `3.22.*` exists, so CI will cut
      `3.22.0`. The reason is recorded in the file beside the value, because the next person doing an
      upgrade pass is looking at the version line rather than at a PR description.

      **Corrected the comment above it, which contradicted the rule it is meant to carry.** It read "Bump
      by hand when a release breaks compatibility **or adds public API**" — but adding public API is
      explicitly *not* a bump here; the rule is that a consumer must have to act. Under the old wording
      steps 2, 5 and 6 would each have called for a bump on their own, and none of them should. The stale
      half is removed and the rule stated as it actually is.

      Checked for other version references: every `3.21` elsewhere in the docs and README is a historical
      note about a past release and correct as written.

- [x] **9. Full suite** — `dotnet build -c Release` then `dotnet test -c Release` from the repo root. Read
      the **test count**, not the exit colour: a zero-test run reports success in a couple of hundred
      milliseconds. If local reports zero, check `dotnet --version` — SDK 10.0.301 does this to every
      Toolkit repo.

- [x] **10. Documentation** — the scope-name grammar landed early in step 2b; what remains is
      `docs/articles/support-cases.md` and `Tharga.Team.Support/README.md`: the new
      scope pair, the option, and a note that `support:read` no longer authorizes replying. Decide whether
      the scope model deserves a section of its own rather than edits to existing ones. Root `README.md`
      only if it mentions support scopes. Land as a `docs:` commit.

- [~] **11. Push and hand over for testing** — push the branch, do **not** open the PR yet, and ask for
      confirmation before closing out.

## Close-out (only once the user says it is done)

- [ ] Re-run `dotnet outdated` across the solution and apply everything, majors included.
- [ ] Comment on #291 and #295 with what shipped and what the reporter can now delete, then close both.
- [ ] File the PlutusWave general scope-override request's outcome: leave the `Requests.md` row open, and
      add a line noting that #295 was solved with a module option so the general gap is still real.
- [ ] Decide whether the unfiled read/write defect needs a retrospective issue for the record, or whether
      the PR description carries it.
- [ ] Archive `plan/feature.md` to `$DOC_ROOT/Tharga/plans/Toolkit/Platform/done/support-case-authorization.md`.
- [ ] `git rm -r plan`, commit `fix: support-case-authorization complete`, push, open the PR.

## Decisions

- **2026-09-21 — #295 solved with a module option, not a general registry override.** `AddThargaScopes`
  mutates the registry eagerly in call order, so a host-side `ScopeRegistry.Replace` would only work when
  called after the module registers. The general capability stays open as the PlutusWave request.
- **2026-09-21 — the unfiled read/write defect ships in this feature.** It lives in the same six lines
  #291 rewrites, so splitting it would mean editing the one authorization gate twice.
- **2026-09-21 — `MAJOR_MINOR` moves to 3.22** because of that defect fix alone; the other two parts are
  additive and would not have justified it.

## Last session

2026-09-21 — branch created and the plan written from the two issues plus a verification pass over
`AuthorizationSupportCaseServiceDecorator`, `SupportRegistration`, `ScopeRegistry` and `ISupportCaseStore`.
Step 2 landed: `SystemSupportScopes.AllRead` / `AllManage`, and the class summary corrected — it claimed
the whole class was about cases belonging to no team, which half of it no longer is. Step 2b then
documented the scope-name grammar after a discussion of the three-segment shape.

Step 3 landed: both scopes registered with descriptions, and three catalogue guards including the naming
one. Step 4 then landed the gate itself — #291's cross-team branch and the unfiled read/write defect, plus
the two team-wide reads. Full solution suite 2812 passed, 0 failed.

Step 5 landed the cross-team listing through the whole stack; step 6 landed the registration option.
**Both issues are now implemented** — full solution suite 2828 passed, 0 failed.

Step 7 confirmed the decorator guard already covers the support pair; step 8 moved the version line to
3.22; step 10 updated both documentation surfaces. Full suite 2829 passed, 0 failed after the docs commit.

**The feature is code-complete.** Next is step 11: push the branch so it can be tested from origin, and
**do not open the PR** — the close-out commit (issue comments, `Requests.md`, `plan/` removal) has to be
the last commit on the branch, and that only starts once the user says the feature is done.

**Still open:** whether a support audit entry records the basis the caller got in on.

**Still open:** whether a support audit entry records the basis the caller got in on. It remains an
additive change at the gate's exit points whenever it is wanted. Next: step 8 (version line), step 10
(documentation), then push for testing.
