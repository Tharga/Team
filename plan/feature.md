# Feature: SupportPanel fixes — header width, narrow screens, other instances

Branch: `feature/support-panel-fixes` (public repo, so no issue number in the name)

Closes Tharga/Team#418, #419 and #420. All three change `SupportPanel`, so they ship as one release.

## Goal

`SupportPanel` works at its default width with every header button reachable, can be offered on a phone instead of
disappearing, and shows changes made on another instance once the host relays them.

## Scope

### #418 — the close button is pushed out of the panel

- `.support-panel__header` wraps (`flex-wrap: wrap`), and `.support-panel__actions` keeps to the right when it wraps
  (`margin-inline-start: auto`). This is the fix the reporter applies from their own stylesheet today.

### #419 — a drawer on narrow screens

- New parameter `NarrowScreen` of a new enum `SupportPanelNarrowScreen { Hide, Drawer }`. **`Hide` stays the default**,
  so nothing changes for a host that does not set it.
- `Drawer`, at 768 pixels or less:
  - **Open**, the panel overlays the content at full height, docked right (`position: fixed; top: 0; bottom: 0;
    right: 0`), at most 85% of the viewport wide. The resize handle is hidden.
  - **Collapsed**, the rail gives way to one floating button in the bottom-right corner, carrying the unread count.
    It opens the panel on the channel the panel would open with anyway.
- The existing hide rule keeps its exact selector, so a host already overriding it from its own stylesheet keeps
  working until it switches to `Drawer`. The drawer rule is one class more specific and wins over the hide rule.
- Above 768 pixels `Drawer` changes nothing.

### #420 — changes made on another instance

- `SupportCaseUpdatedEventArgs.FromRemote` (`bool`, `init`, not `required`, default `false`): set by a host relaying
  a change that was made on another instance. A host's broadcaster publishes only notifications where it is `false`,
  so a relay does not echo back between instances.
- `ISupportCaseNotifier.Notify` is documented as the way a host relays, not only something the toolkit calls.
- `SupportPanel` (and every other listener) already reacts to any `CaseUpdated`, so a relayed change refreshes an
  open panel with no further change.
- Chosen over the alternative in the issue (a public `RefreshAsync()` on the panel) because relaying into the shared
  notifier refreshes every view and chip at once, while a `RefreshAsync()` would have to be added to, and called on,
  each of them. `SupportUnassignedView.RefreshAsync()` stays as it is.

## Acceptance criteria

- [ ] At 320 pixels with both tabs labelled ("Assistent", "Support"), every header button is inside the panel and
      clickable; at 280 too.
- [ ] Without `NarrowScreen`, the panel is hidden at 768 pixels or less, exactly as before.
- [ ] With `NarrowScreen="SupportPanelNarrowScreen.Drawer"` at 768 pixels or less: collapsed shows one floating
      button with the unread count, open overlays the content at full height and at most 85vw, close returns to the
      button. Above 768 pixels it behaves as it does today.
- [ ] A `CaseUpdated` with `FromRemote = true` for the open case re-reads the conversation and the unread count.
- [ ] `FromRemote` reaches every subscriber unchanged.
- [ ] Docs (`docs/articles/support-cases.md`) describe `NarrowScreen` and how to relay across instances, replacing
      "needs a backplane, which is not built" where case changes are concerned.
- [ ] Full test suite passes.

## Done condition

All acceptance criteria met, the user has tested from the pushed branch, and the PR closes #418, #419 and #420.

## Out of scope

- The typing notifier and the support desk presence are still in-process (their docs say so); not asked for.
- #421, #422 and #378 follow on their own branches, in that order.
