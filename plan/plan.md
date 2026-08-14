# Plan: access card renders without a gap, access bar is translatable

Branch: `feature/simulation-card-and-bar` (from `master`, CI = GitHub Actions). Issues #219 and #221.

## Steps

- [x] 1. **Package updates up front.** `dotnet outdated` across the whole solution: **no outdated
      dependencies** — the 2026-08-14 sweep on the previous feature already brought everything current.
      Nothing to apply, nothing to commit.

- [x] 2. **Tests first** — `AccessSimulationCanSimulateTests` (8) and `AccessSimulationBarTextTests` (12).

- [x] 3. **`CanSimulateAsync` claims fast path** (#219). Guarded by a new `ClaimsCanAnswer`: no simulation
      active **and** a `TeamClaimTypes.TeamKey` claim matching the selected team. The second condition was
      not in the plan and turned out to be load-bearing — without it an absent scope claim cannot be told
      apart from claims never having been issued, which is exactly the state the existing
      `AccessSimulationConsentAccessTests` fakes are in. Those four tests pass unchanged and now cover the
      fallback path.

- [x] 4. **Card renders in two phases** (#219). `_render` is decided from the principal before the text is
      fetched, so a caller who cannot simulate never sees a placeholder appear and vanish; a caller who
      will get the card sees `<Loading />` — the same convention eight other components in the library
      already use — instead of a gap.

- [x] 5. **`AccessSimulationBarText`** (#221) with `ViewingAs`, `Stop`, `ViewAs`, `TargetRole`,
      `TargetAccessLevel`. Named `Stop` rather than the reporter's suggested `returnToMyAccess` to match
      `AccessSimulationCardText.Stop`, which is the same string; their list was explicitly approximate.
      Their suggested separate `.reduced` key is folded into `ViewingAs` so word order stays with the
      translator.

- [x] 6. **Bar renders from the text set** (#221). `AccessSimulationBannerSentence` splits the sentence
      around `{0}` so the target keeps its `<strong>`, and `Describe` moved there too — it was composing
      "the X role" in English. The dialog title now comes from the card's `ViewAsUser` key so both entry
      points open the same screen under the same wording.

- [x] 7. **Verify** — `dotnet build -c Release` 0 errors; full suite green, 2000 passed / 0 failed
      (Blazor 932 → 952). `TextCoverageTests` ratcheted: `AccessSimulationBar.razor` moved from Pending(3)
      to Migrated, which the test itself demanded.

- [x] 8. **Docs review** — three surfaces, all of which had a claim this feature made false:
      - `docs/articles/access-simulation.md` — new **Translating it** section (both key catalogues, why the
        banner is one sentence, what is still literal), plus a paragraph under *Who can use it* on the
        controls being gated from claims and what that trades away.
      - `docs/articles/implementation-guide.md` — `AccessSimulationBar` moved into the fully-routed list;
        the still-literal tally corrected from 134 across 8 components to 131 across 7; the parameter
        table now says `Text` overrides the resolved key rather than being the only way to set the label.
      - `Tharga.Team.Blazor/README.md` — the access-simulation bullet now states that both components
        translate.

- [ ] 9. **Close-out** (only after the user says it is done) — package re-check, `plan/` archived and
      removed, records closed: GitHub #219 and #221, the central `Requests.md`, the project backlog, and
      both FortDocs "Watching" entries.

## Decisions (2026-08-14)

- **Localization scope: the bar only, as filed.** `AccessSimulationDialog` stays untranslated in this
  feature. Raise it as its own issue rather than widening #221.
- **#219: remove the wait, not mask it.** Claims fast path in `CanSimulateAsync`, plus the loading frame
  while text resolves. The placeholder-only alternative was declined.

## Notes

- CI computes the package version from tags; nothing to bump by hand.
- Bug fixes, so `fix:` throughout.

## Last session

2026-08-14 — implemented and committed (`d2af718` fix, `5b4b44f` docs). Full suite green at 2000. Next:
push for the user to test. Step 9 (close-out) waits for the user to say the feature is done.

**Worth raising at close-out:** `AccessSimulationDialog` is now the only untranslated part of the feature,
at 12 literal strings, and it is the screen the newly-translated banner button opens. File it as its own
issue rather than letting it sit in the coverage ratchet unnoticed.
