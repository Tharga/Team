# Plan: team reads honour consent and custom roles

Feature scope in `plan/feature.md`. Issue: [Tharga/Team#248](https://github.com/Tharga/Team/issues/248).

## Package updates first — mandated, and bundled into this PR

`shared-instructions.md` → *Feature Workflow → Starting a feature* requires all available updates applied up
front, in this feature's own PR. `dotnet outdated` on 2026-09-05 found:

| Package | From | To | Note |
|---|---|---|---|
| `xunit.v3` | 3.2.2 | 4.0.0 | **Major.** Drops the VSTest bridge; this is the Microsoft.Testing.Platform migration |
| `xunit.runner.visualstudio` | 3.1.5 | 4.0.0 | Removed outright by the migration, not upgraded |
| `SkiaSharp` (+ `.NativeAssets.Linux.NoDependencies`) | 4.151.1 | 4.151.2 | Patch, `Tharga.Team.Images` |

The major was called out to the user before starting, per the same rule; the user chose to bundle it.

**Watch for the failure that looks like a pass.** MTP reports *"Zero tests ran"* and exits 5 when a requested
extension is not referenced — a couple of hundred milliseconds that scrolls past looking green. Read the
count on every run. Local SDK here is **10.0.302**; `shared-instructions.md` records unexplained local zero-test
runs on 10.0.301, so if a local run finds nothing, check `dotnet --version` before believing it and fall back
to running the test executable directly (xunit's own CLI — `-trait`, not MTP options).

## Steps

- [x] **1. `global.json` with the MTP test runner.** New file at the repo root. Resolved from the *current
  working directory*, not the project path, so it must sit beside the solution. **No SDK version pinned** —
  CI runs 10.0.400 and this machine runs 10.0.302, so a pin would break one of them.
- [x] **2. Migrate the 7 test projects.** `Tharga.Team.Blazor.Tests`, `.Entra.Tests`, `.Images.Tests`,
  `.Mcp.Tests`, `.MongoDB.Tests`, `.Service.Tests`, `.Support.Tests`. Per project: add
  `<OutputType>Exe</OutputType>` (now required — "xUnit.net v3 test projects must be executable"), bump
  `xunit.v3` to 4.0.0, drop `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` and `coverlet.collector`,
  and add the MTP extensions the options in use actually need — `Microsoft.Testing.Extensions.CodeCoverage`
  for `--coverage`, `Microsoft.Testing.Extensions.TrxReport` for `--report-trx`. An option whose extension is
  missing is exactly what produces "Zero tests ran". **Only `CodeCoverage` was added** — `TrxReport` is not
  referenced because nothing here passes `--report-trx`. That asymmetry is deliberate: an option needs its
  extension, but an unused extension is dead weight. If CI ever gains `--report-trx`, it must gain the
  package in the same commit. (Azure DevOps injects that flag on its own; GitHub Actions does not, and this
  repo is GitHub Actions.)
- [x] **3. CI test step.** `build.yml` now runs
  `dotnet test -c Release --no-build --verbosity normal --coverage --coverage-output-format cobertura --results-directory ./coverage`.
  `--verbosity normal` was verified to still be accepted under MTP rather than assumed. Coverage files land
  **flat** as `./coverage/<guid>.cobertura.xml`, where coverlet wrote `./coverage/<guid>/coverage.cobertura.xml`
  — the Codecov step points at the directory and searches it recursively, so it needed no change. Verified by
  probing locally and listing the emitted files, not by reading docs.
- [x] **4. SkiaSharp patch bump** in `Tharga.Team.Images` — 4.151.1 → 4.151.2, both packages.
- [x] **5. Verify the migration before touching feature code.** `dotnet build -c Release` succeeds.
  **Full suite: 2462 passed, 0 failed, 0 skipped.** A real count, deliberately recorded here — the failure
  this migration risks is a *fast green*, and 2462 is what distinguishes it from one. The SDK 10.0.302
  zero-test problem recorded in `shared-instructions.md` did not appear.
  Also fixed the three new `xUnit2031` analyzer warnings the 4.x analyzers introduced
  (`Assert.Single(x.Where(p))` → `Assert.Single(x, p)`) in `ApiKeyAuditWiringTests` and `AddThargaTeamTests`,
  so the upgrade does not push CI toward its 35-warning threshold.

- [x] **6. A failing test that reproduces #248.** Done, and verified red/green properly: with the old
  member-row logic temporarily restored, **9 of the new tests fail**; with the fix in place all pass. The
  reproduction was written after the fix rather than before it, so the red run was done deliberately rather
  than observed by accident — worth knowing when reading the history.
- [x] **7. Move `TeamGrantResolver` into `Tharga.Team`.** It is `internal` and depends only on `ITeamService`,
  `IScopeRegistry` and `ITenantRoleService`, all already in that package. Add `InternalsVisibleTo` on
  `Tharga.Team` for `Tharga.Team.Service`, `Tharga.Team.Blazor`, `Tharga.Team.Mcp` and the test projects that
  need it, so the existing callers (the claims builder, the MCP context accessor) keep compiling unchanged.
  Its namespace became `Tharga.Team` (it is internal, and every call site is nested under that namespace, so
  no `using` had to change). `ITeamPrincipalAccessor` moved too, but **kept its `Tharga.Team.Service`
  namespace** with a `TypeForwardedTo` — it is public, and a host may implement it. Same pattern as the
  `ISupportCaseService` move in #245.
- [x] **8. Rewire `TeamManagementService`.** `RequireTeamReadAsync` and the `GetTeamsAsync<T>()` filter both go
  through the resolver; delete both `GrantsTeamRead` overloads. The resolver needs a `ClaimsPrincipal` for the
  consent branch and a default consent level — take `ConsentOptions` via `IOptions<ConsentOptions>` (already in
  `Tharga.Team`) rather than threading a bare `AccessLevel`, and get the principal from the same accessor the
  mutation path uses. Keep the existing "no scope registry means the app does not use scopes" escape intact —
  an app not using scopes must not start refusing reads.
  **Registration changed too**, and this was the subtle part: `services.AddScoped(managementServiceType)` let
  the container pick the constructor, and it picks the greediest one it can *fully* satisfy. With the new
  dependencies optional, one unregistered service would have silently selected the old overload and gone
  straight back to refusing reads — a bug that would pass every test written against a fully-wired container.
  It is now an explicit factory using `GetService` for the optional ones.
- [x] **9. Regression tests for the rest of the acceptance criteria** — 19 new tests in
  `ConsentedTeamReadTests` and `TeamGrantSingleEnforcementTests`.
- [x] **10. Architecture test.** Reads IL rather than grepping, so a rename cannot slip past it, and carries
  the required self-check (`TheScan_FindsTheResolver`) plus a staleness check on its own allowlist.
  **The first draft was too strict and failed**, which turned out to be useful: it found three other types
  computing effective scopes. All three are legitimate — `TenantRoleService` is the composition primitive the
  resolver itself calls, and `TeamContextResolver` / `ApiKeyAuthenticationHandler` are the API-key path, where
  a key holds no roles and consent is deliberately the team's level rather than a role match. They are now an
  explicit allowlist with a stated reason each, which is stronger than the blanket ban would have been.
- [ ] **11. Docs.** Review both surfaces: `README.md` and `docs/`. Consent behaviour on reads is
  consumer-visible, so this likely warrants real content rather than only an edit. Land as its own `docs:`
  commit.
- [ ] **12. Close-out.** Comment on and close #248 citing the type, member and test that prove it; sweep
  `Requests.md` and the backlog; archive `plan/feature.md` to the Plan directory `done/`; `git rm -r plan`;
  final commit `fix: team reads honour consent and custom roles complete`; push; open the PR.

## Notes

**`plan/` was already on `master`** when this branch was cut — PR #247 merged without its close-out commit, so
`plan/feature.md` and `plan/plan.md` from the support-email-channel feature were tracked on `master`. That
feature's spec has been archived to `done/support-email-channel.md`, and step 12's `git rm -r plan` repairs
the violation as a side effect of finishing this one. No separate `chore/finalize-` branch is needed.

**Still outstanding from #247, and not this feature's job:** issue #142 has no comment describing what the
support-email-channel PR shipped. Its latest comments still read "Email (6) — transport and inbound pipeline
built, not yet wired" and "Cases with no team selected — specified", both of which that PR delivered.

## Last session

2026-09-05 — Branch cut, defect diagnosed and confirmed against the code, plan written. Nothing implemented
yet; step 1 is next.
