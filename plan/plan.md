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
- [x] 4. #419 — tests first (default renders no drawer class and no floating button; `Drawer` adds the class, renders
      the floating button with the unread count, and the button opens the panel; the injected hide rule keeps its
      selector), then `SupportPanelNarrowScreen`, the parameter, the markup and the CSS.
      The floating button reuses the rail's labels (Assistant / Support, whichever it opens), so no new text keys.
      It opens `_support`, the channel the panel already defaults to. Drawer and button sit at
      `calc(--rz-dialog-mask-zindex - 1)`: over the page and header, under any dialog. Rules added by `install()`
      are now a multi-line template literal; the hide rule's line is unchanged character for character.
- [x] 5. #420 — tests first (`FromRemote` reaches subscribers; a remote change for the open case refreshes the panel),
      then `FromRemote` and the XML docs on the notifier.
      Only the six Blazor views subscribe to `CaseUpdated`, and each just reads again, so a relayed change repeats no
      side effect. `TwoInstancesRelayingEachOthersChanges_DoNotEcho` shows the relay a host writes. The panel needed
      no change; its new test pins that a remote change redraws the conversation and the unread count.
- [x] 6. Checked in the sample (signed in, own team). Expanded at 320 and 280 px, English and with the tab labels
      swapped to "Assistent" / "Support": the header wraps, the actions move to a second row on the right, and the
      close button stays inside. Drawer, with the sample switched to `Drawer` for the check only (reverted): Chrome
      would not resize the maximized window, so the sample ran in a 400 px iframe. Open, it was fixed over the page at full height and
      320 wide with the close button inside. Collapsed, the rail was hidden, a floating assistant button sat bottom
      right and the sidebar was transparent around it. The button opened it again. At 1440 px the drawer class
      changes nothing.
- [x] 7. Docs: `support-cases.md` panel section gained a `NarrowScreen` bullet and example, and its redraw line names
      relayed changes. The notifier's "in-process only, not built" callout became the relay with `FromRemote` and
      an example. The unread-chip note points to it, and the typing note no longer says case changes are
      in-process. `Tharga.Team.Blazor/README.md` mentions the drawer. The root README never mentioned narrow screens.
- [~] 8. Full suite green (6383 passed). Push the branch for the user to test.

## Notes

- Versioning: `MAJOR_MINOR` in `.github/workflows/build.yml` is `4.6`. The change is additive (a parameter, an enum,
  an optional property), so nothing breaks.

## Last session

2026-10-29: Branch created from `master` at 4.6.1, dependencies updated and committed, plan written. Next: the user
confirms the plan, then #418.
