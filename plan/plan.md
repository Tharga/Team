# Plan: SupportPanel fixes (#418, #419, #420)

## Steps

- [x] 1. NuGet update — SkiaSharp and SkiaSharp.NativeAssets.Linux.NoDependencies 4.153.1 → 4.154.0 (the tool
      bumped the first and errored on the second; set by hand, the pair must match). Build green, 6371 tests pass.
      Commit `chore(deps): nuget update`.
- [x] 2. Confirm this plan with the user. Approved 2026-10-29, three branches as proposed; ships as a **patch**
      (4.6.2), so `MAJOR_MINOR` stays `4.6`.
- [x] 3. #418 — test first (the stylesheet wraps the header and pushes the actions right), then the two CSS rules.
      `SupportPanelStyleTests` reads the top-level rules of `SupportPanel.razor.css` (media queries skipped, so a
      wrap only on narrow screens would not pass). Header got `flex-wrap: wrap`; `.support-panel__actions` its own
      rule with `margin-inline-start: auto`. Visual check is step 6.
- [~] 4. #419 — tests first (default renders no drawer class and no floating button; `Drawer` adds the class, renders
      the floating button with the unread count, and the button opens the panel; the injected hide rule keeps its
      selector), then `SupportPanelNarrowScreen`, the parameter, the markup and the CSS.
- [ ] 5. #420 — tests first (`FromRemote` reaches subscribers; a remote change for the open case refreshes the panel),
      then `FromRemote` and the XML docs on the notifier.
- [ ] 6. Check it in a browser: the sample app at 320 and 280 pixels with Swedish labels, and at a phone width with
      `Drawer`, open and collapsed.
- [ ] 7. Docs: `docs/articles/support-cases.md` (the panel section, the notifier section, the unread-count note), and
      `README.md` / `Tharga.Team.Blazor/README.md` if they cover any of it.
- [ ] 8. Full test suite, push the branch for the user to test.

## Notes

- Versioning: `MAJOR_MINOR` in `.github/workflows/build.yml` is `4.6`. The change is additive (a parameter, an enum,
  an optional property), so nothing breaks.

## Last session

2026-10-29: Branch created from `master` at 4.6.1, dependencies updated and committed, plan written. Next: the user
confirms the plan, then #418.
