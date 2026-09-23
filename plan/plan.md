# Plan: host extension points on TeamComponent's member grid (#294)

Spec: `plan/feature.md`. Branched from `origin/master` at `1dac7e8`; packages clean at branch time.

- [x] 1. `MemberRowAction<TMember>(string Action, TMember Member)` beside `TeamRowAction` / `UserRowAction`,
      with XML docs matching theirs. A test that the record carries what a handler needs.
- [x] 2. The three action hooks on `TeamComponent`: `MemberActionsTemplate` (beside the built-in button),
      `MemberActionItems` (inside the split-button dropdown), `MemberActionInvoked` (the click). Doc each
      one by pointing at its `TeamsListView` counterpart, so the family reads as one thing.
- [x] 3. Wire them into the member action column. **Confirmed both layouts are covered by one edit** — the
      member grid lives in the shared `TeamDetail` fragment (line 710), rendered by the card layout at 144
      and the grid layout at 165. **Two cases the issue did not raise, found while wiring:** a row with
      exactly one built-in action plus host items must become a split button rather than a plain one, or
      the host's items have nowhere to go; and a row with *no* built-in actions — the caller's own, or the
      owner's — must still render the menu for host items alone, icon-only, since the primary half would
      otherwise have to invent a label for a menu the toolkit knows nothing about. Its left half is a
      handle rather than a command, so a null action returns instead of raising the callback. Originally:  **Both layouts** — the component renders a card and a grid
      through a shared fragment, so check the hook lands in each rather than assuming.
      - A host item must be distinguishable from a built-in one when the click arrives: the built-ins are
        matched by their own constants first, and anything else is forwarded to `MemberActionInvoked`.
      - With only host items and no built-ins, the button must still render.
- [x] 4. `MemberColumns` appended inside the grid's own `<Columns>`, after the built-in columns and before
      the action column — an appended column should not displace the actions to the middle of the row.
- [x] 5. Tests. The decisions that can be tested without bUnit are which actions exist for a member and how a
      click is routed; extract anything decided in markup, as `TeamAccessRequestGate` did. Cover: a host
      item reaches `MemberActionInvoked`; a built-in still routes internally; nothing supplied leaves the
      action list exactly as it is today.
- [x] 6. Confirm `TeamComponent.razor` still reports **zero** literals to the text ratchet — it is
      `Migrated`, so any display string added here fails the build.
- [x] 7. Docs: the implementation guide's component parameter reference, and the README where `TeamComponent`
      is described. Show the member-column case, since that is the reported need.
- [x] 8. Full suite (3,012). **Verified in the sample in a browser, 2026-09-23.** The sample's `/team` page
      now demonstrates all three hooks: a **Project** column, a **Next project** item in the member's action
      menu, and a handler receiving `MemberRowAction<TeamMember>` — the sample's own type, no cast. Clicked
      it: the menu item rendered below the built-in Audit log, the click arrived, and the cell went from
      `—` to `Apollo` and survived a reload. No server-side error. Demo state is a singleton held in
      memory — persisting a host's own member field is the host's job and out of scope, as the issue says.
- [ ] 9. Push for user testing.

## Notes

- **No new concept.** `TeamsListView` and `UsersListView` already ship this trio; this mirrors it on the one
  component that lacks it. Where a naming choice arises, match theirs rather than improving on it.
- `TeamComponent` is at **zero** literals in the text ratchet. Nothing added here may carry display text.
- Persistence for host-added member fields is explicitly out of scope — see `feature.md`.
- No `MAJOR_MINOR` bump: additive and opt-in.
- Close #294 with what shipped when the feature closes; it came from a consumer report.
