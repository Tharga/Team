# Feature: access-simulation dialog rework, team-bound simulation, top-bar indicator

Spec: `$DOC_ROOT/Tharga/plans/Toolkit/Platform/planned/12-simulation-dialog-rework.md`. Includes GitHub
Tharga/Team#276. Branched from `feature/role-scope-pickers` (PR #278, unmerged) with `origin/master` merged in.

**Invariants carried over, not up for change:** simulation only removes scope claims; the cookie is untrusted;
the exit is never gated; audit names the real person; pickers use the caller's *real* grant.

## Goal

1. One dialog that composes the simulated access from an access level, roles, hand-ticked scopes — or a user.
2. A simulation stays in the team it was started in (#276).
3. An optional compact indicator beside the team selector: "access reduced" + return.
4. Demo mode shows no on-screen sign that the caller is anything but an ordinary team user.

## Design

### Dialog

- **Every registered team scope as a checkbox list, with a search box.** Scopes the caller does not hold are
  listed **disabled**, marked as not showable (user, 2026-09-14).
- **Access level** — single choice, every level except `Custom`. Its scopes show **checked and disabled**
  (inherited), the pattern `ScopeOverrideEditor` already uses.
- **Roles** — multi-select; their scopes are added as inherited (checked, disabled).
- **Hand ticks** — any remaining scope the caller holds can be ticked.
- **User** — picking a member fills level, roles and their scope overrides (as hand ticks). Further edits allowed.
- **The limitation is stated in the dialog**: it can only show less than you have, never more. The existing
  gap warning (`AccessSimulationDifference`) stays.
- Target = level scopes ∪ role scopes ∪ ticked scopes; access level clamped as today.
- **Kind and label:** an unedited user pick stays `AccessSimulationKind.User` with the member's name; anything
  else is a new `AccessSimulationKind.Composed` with a label built from its parts (e.g. "Viewer + Editor +
  2 scopes"). Existing kinds stay in the enum for cookies and audit entries already written.
- **Dialog strings move to `IThargaTextProvider`** (`AccessSimulationDialogText`), closing the backlog item
  *AccessSimulationDialog still renders 12 hardcoded strings*.

### #276 — a simulation is bound to its team

- `AccessSimulation` gains `TeamKey`, set from the selected team when built.
- `TeamServerClaimsTransformation` and `TeamClaimRevalidator` apply the filter **only when the simulation's
  team is the selected team**. A simulation with no team key (older cookie) or another team's key is ignored —
  the caller sees their real access for that team, which is the documented safe direction for a cookie that
  does not apply.
- `TeamStateService`: while the active simulation's team is the selected team **and** claims were issued for
  that team (`TeamClaimTypes.TeamKey`, which is only issued when the *real* grant resolved and which the filter
  never removes), the selection stands — it is not re-judged from the narrowed principal, and
  `selected_team_id` is not rewritten.

### Indicator

New `AccessSimulationIndicator`: renders nothing unless a run-as simulation is active; then a compact chip
("Access reduced", with the description as tooltip) and a return button. **Renders nothing during demo mode.**
Optional — a host places it. The sample places it beside `<TeamSelector />`.

### Demo mode

Run it in the sample as a system user on a consented team and record every on-screen trace (expected
suspects: consent badges in the team list/selector, profile page). Hide those while `Kind == Demo`.
Audit keeps the real person.

## Acceptance criteria

- [ ] Dialog lists all registered team scopes; unheld ones disabled and marked.
- [ ] Access level choices exclude `Custom`; choosing one checks its scopes.
- [ ] One or more roles add their scopes; extra scopes can be ticked by hand.
- [ ] Choosing a user fills level, roles and scopes.
- [ ] The "can only reduce" limitation is visible in the dialog.
- [ ] The resulting simulation is still ⊆ the real grant, including for a forged cookie naming another team.
- [ ] #276: starting in consent-reached Team A stays in Team A, applies to Team A, and returning stays in Team A.
- [ ] A simulation is not applied on any team other than its own.
- [ ] `AccessSimulationIndicator` shows reduction + return during run-as, nothing otherwise or during demo.
- [ ] Demo mode leaves no on-screen trace found in the sample; audit still names the real person.
- [ ] Dialog text resolves through `IThargaTextProvider`; `TextCoverageTests` holds it at zero literals.
- [ ] Full suite green.

## Done condition

Criteria met, docs reviewed (README + `docs/`), #276 answered and closed, spec 12 archived to `done/`,
`plan/` removed in the close-out commit, PR open against `master`.
