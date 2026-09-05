# Plan: A team names itself (#254)

Branch: `feature/team-accessible-label` (from `master`, at 3.20.0)

## Steps

- [x] 0. **NuGet updates up front.** `dotnet outdated Tharga.Team.sln` — *No outdated dependencies were
      detected*. Nothing to apply, so no upgrade commit. Re-checked at close-out per the workflow.
- [x] 1. **Guard tests first, and failing.** Three of them:
      - `TeamEntityBase` derived-record `ToString()` returns the name (Tharga.Team.MongoDB.Tests),
        asserted against `DefaultTeamEntity` **and** a test-local record carrying extra properties.
      - A `TeamSelector` render test (Tharga.Team.Blazor.Tests, bUnit) asserting the markup contains
        no `{ Id = ` dump. Needs `JSInterop.Mode = JSRuntimeMode.Loose` — Radzen's dropdown calls
        `Radzen.preventArrows` on first render and strict mode throws.
      - Confirm test 1 fails with a plain `override` and passes only with `sealed override`, so the
        reason for `sealed` is recorded by a test rather than by a comment.
- [x] 2. **Implement.** `public sealed override string ToString() => Name;` on
      `TeamEntityBase<TTeamMemberModel>`, with XML docs saying why it is sealed and what it costs
      (a host can no longer override `ToString()` on its team entity — deliberate).
- [x] 3. **Verify.** Guard tests green, then the full suite in Release.
- [x] 4. **Version — settled, no action.** Ships as **3.20.1** on the existing `MAJOR_MINOR: 3.20`;
      the workflow is not edited. Decided by the user 2026-09-05 with the trade-off stated: sealing
      `ToString()` is a source break for any host that overrides it, and it lands in a patch anyway
      because no host is known to. **The PR description must say so** — it is release-note material
      precisely because the version number will not signal it.
- [x] 5. **Docs.** Review `README.md` and `docs/articles/` for anything describing team entities or the
      selector. State explicitly if there is no consumer-visible surface to update.
- [~] 6. **Close-out** (only once the user says it is done): re-run `dotnet outdated`, comment on and
      close #254 citing the type and tests, archive `plan/feature.md` to the Plan directory `done/`,
      `git rm -r plan`, final commit `fix: a team names itself complete`, push, open PR.

## Notes

- The Radzen markup above was established empirically with a throwaway bUnit spike on this branch,
  then deleted. It is not guesswork, and it is why the fix is on the entity rather than the component.
- Nothing in the repo depends on the record dump: no logging, no test, no serialization path asserts
  `ToString()` on a team.

## Progress

- **Step 1 done.** 8 guard tests, all failing first: `TeamEntityToStringTests` (3, MongoDB suite) and
  `TeamSelectorLabelTests` (5, Blazor suite). The Blazor test project gained a `Tharga.Team.MongoDB`
  project reference so the render guard binds the real `DefaultTeamEntity` — a test-local record would
  only re-prove Radzen's behaviour rather than ours. Before the fix: 3 failed / 0 passed, and 4 failed /
  1 passed, the single pass being `TheSelectorShowsTheTeamName` — the visible text was never the defect.
- **Step 2 done.** `public sealed override string ToString() => Name;` on `TeamEntityBase`.
- **The `sealed` claim is proven, not asserted.** Dropping the keyword and re-running put all 3 entity
  tests back to failing — including the one against the shipped `DefaultTeamEntity` — then restoring it
  turned them green. That is the compiler re-synthesizing `ToString()` on the derived record, observed.
- **Step 3 done.** Full suite in Release across all 7 test projects: **2527 passed, 0 failed**
  (MongoDB 103, Blazor 1042, Service 874, Support 352, Mcp 107, Entra 38, Images 11).
  Note: `Tharga.Blazor.Tests/` in the repo root holds only a stale `obj/` and no project — pre-existing,
  unrelated, left alone.
- **Step 5 done.** `docs/articles/implementation-guide.md` — "Using your own entities" now states that
  `ToString()` is sealed and why, since that is the section a host reads while declaring the record that
  would otherwise fail to compile. No `README.md` change: it does not cover entity declaration.

- **Branch pushed** 2026-09-05 (`origin/feature/team-accessible-label`), no PR yet, awaiting user testing.
  **CI does not build it.** `build.yml` triggers only on `push: [master]` and `pull_request: [master]`,
  so a feature branch with no PR runs no workflow; and `dotnet nuget push` sits in a job gated on
  `github.ref == master && event_name == push`, so no pre-release package is published even from a PR.
  Testing this branch therefore means the sample app or a local package feed, not a package from origin.
