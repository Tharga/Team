# Plan: idempotent system-scope registration (#237)

Feature scope: `plan/feature.md`. Conventional-commit prefix for this branch: **`fix:`**.

## Steps

- [ ] **0. Package updates — HELD, awaiting user override.**
      Only xunit.v3 4.0.0 / xunit.runner.visualstudio 4.0.0 are outstanding; that is the twice-backed-out
      Microsoft.Testing.Platform migration, which the backlog says needs its own PR because it fails on the
      Linux runner only. Reasoning in `feature.md` → *Package updates*. No action unless overridden.

- [x] **1. Reproduce before fixing — DONE 2026-08-24. It is registration order, not repeated construction.**
      Four tests added to `SystemScopeRegistrationTests`. Two fail with the consumer's exact message
      (`System scope 'simulation:demo' is already registered.`) and two pass, and the passing pair is what
      settles the diagnosis:
      - **Fails:** `AHostRegisteringAScopeTheLibraryAlreadyRegistered_DoesNotThrow` — library first, then the
        host's own registration. This is the defect.
      - **Fails:** `AScopeRegisteredTwice_AppearsOnceInTheCatalogue` — same cause, throws before asserting.
      - **Passes:** `BuildingTheSameHostTwiceInOneProcess_DoesNotThrow` — **repeated host construction is not
        the cause.** The registry is per-collection exactly as it looks, and the `BuildServiceProvider()`
        probe is not implicated. Step 3 has nothing to do.
      - **Passes:** `TheLibraryAndAHostDescribeTheSameScopeDifferently` — confirms the two descriptions
        differ, so the issue's proposed name+description rule would not have fixed this.
      **Both of the issue's framings are wrong** — not static/process-wide state, and not "the second host
      build". It is ordering within one container, which is why it looked like the latter. Say so in the
      issue reply, because it changes what the reporter should expect: the fix makes ordering irrelevant
      rather than making repeated builds work.

- [x] **2. Make `SystemScopeRegistry.Register` idempotent on name — DONE 2026-08-24.** Duplicate name is now
      a no-op; the throw is gone. XML docs carry the reasoning and the #237 reference.
      Duplicate name → no-op, keeping the first registration's description (decision recorded in
      `feature.md`). `Register` is on the concrete class and not on `ISystemScopeRegistry`, so this is not an
      interface change.

      **Correction, 2026-08-24 — this step as first written was self-contradictory.** It said duplicates
      no-op *and* that "materially different metadata still throws". For a system scope those are the same
      case: `SystemScopeDefinition` carries only `Name` and `Description`, so "different metadata" means
      "different description" — exactly the reported failure. Keeping that throw would have shipped a fix
      that does not fix it.
      **There is no conflict left to detect.** The name is the scope's identity and the description is
      catalogue text, so a duplicate name is never ambiguous about *which capability* is meant. `Register`
      therefore stops throwing on duplicates altogether.
      This does not lose typo protection: a typo produces a *new* name, not a duplicate, and unregistered
      role scopes are caught separately by `UnregisteredRoleScopeCheck`. The genuine-conflict idea only
      applies to team scopes, where `ScopeDefinition` also carries `DefaultMinimumLevel` and `GrantOnly` —
      and team scopes are deliberately out of scope for this patch.

- [x] **3. Whatever step 1 exposed about repeated host construction — NOTHING TO DO.**
      `BuildingTheSameHostTwiceInOneProcess_DoesNotThrow` passes against unfixed code, so repeated
      construction never was the mechanism and the `BuildServiceProvider()` probe is not implicated. The test
      stays as a regression guard and as the evidence for the issue reply. Step closed 2026-08-24 without a
      code change.

- [x] **4. Make the tests from step 1 pass — DONE 2026-08-24, 11/11 in `SystemScopeRegistrationTests`.** The
      conflict case was withdrawn rather than added; see step 2's correction.
      - Duplicate name with differing descriptions → no throw, one entry in `All`.
      - `All` contains exactly one `simulation:demo` after a double registration, so the scope catalogue
        renders no duplicate row.

- [x] **5. The now-redundant per-site guards — REMOVED 2026-08-24, with one principled exception.**
      All six in `ThargaBlazorRegistration.cs` and the system-scope one in `McpTeamBuilderExtensions.cs` are
      gone. The **team**-scope guard in `McpTeamBuilderExtensions.cs` stays, because `ScopeRegistry.Register`
      still throws — a team scope carries an access level and a grant-only flag that two registrations can
      genuinely disagree about. The plan said "all or none", written before the two registries diverged; the
      asymmetry is now the point and is stated in a comment at the surviving site. A false comment claiming
      "both registries throw on a duplicate" was corrected in the same pass, as were the class remarks on
      `SystemScopeRegistrationTests`.

- [x] **6. Full test suite — DONE 2026-08-24. 2110 passed, 0 failed, across all seven projects.**
      (Images 11, Entra 38, MongoDB 70, Support 74, Service 814, Mcp 107, Blazor 996.)

- [~] **7. Docs.**
      - `docs/articles/access-simulation.md` — state that the toolkit registers `simulation:demo` from
        3.14.0, and that a host that registered it itself is safe and may delete its own line.
      - `docs/articles/implementation-guide.md` — same note where the scope catalogue is described.
      - Land as a separate `docs:` commit before close-out, per the workflow.

- [ ] **8. Close the records this fixes — in this PR, not after.**
      - Comment on [#237](https://github.com/Tharga/Team/issues/237) with what shipped, that the static-state
        hypothesis was not the cause, and that the host may delete its own `simulation:demo` registration.
        Close it.
      - Backlog `Toolkit/Team.md` — record that `ScopeRegistry` (team scopes) carries the same bug class and
        was deliberately left, linking it to the #232 residual.
      - `Requests.md` — no entry exists for this; nothing to update. Do not invent one.

- [ ] **9. Close out.**
      Archive `plan/feature.md` to `$DOC_ROOT/Tharga/plans/Toolkit/Platform/done/`, `git rm -r plan`, final
      commit `fix: idempotent system-scope registration complete`, push, open PR.
      **Bump `MAJOR_MINOR` only if the patch line does not carry it** — this adds no API, so 3.14.1 should
      fall out of the existing patch counter. Verify rather than assume.

## Notes and decisions

- **2026-08-24** — Branch created from `master` at `4b84f5e`. Tree clean, level with origin.
- **2026-08-24** — Established before planning: the issue's stated cause (registry became static/process-wide)
  is wrong — neither registry holds static state. The real change is that 3.14.0 began registering
  `simulation:demo` toolkit-side, colliding with hosts that already registered it themselves.
- **2026-08-24** — Established before planning: the issue's proposed fix (idempotent when *name and
  description* match) would not fix the reported case, because the toolkit's and the host's descriptions
  differ. Idempotency must key on name alone.

## Last session

Branch created, `feature.md` and `plan.md` written, awaiting plan confirmation. No code changed yet.
