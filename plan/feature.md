# Feature: TeamSelector offers the picker when nothing is selected

Fixes [Tharga/Team#214](https://github.com/Tharga/Team/issues/214).

## Goal

`TeamSelector` must never render an empty top bar. A caller holding `teams:read` who belongs to no team
currently sees nothing at all — no dropdown, no link, no message — and has no way to reach a team from the
top bar.

## Why it happens

Three individually correct pieces meet in a state none of them covers:

1. `TeamSelector.ReloadTeams` widens `_teams` to every team for a `teams:read` holder, so `_teams.Length`
   is the tenant count rather than the caller's membership count.
2. `TeamSelectionResolver.Resolve` deliberately falls back to **own memberships only**, so a teamless
   caller resolves to `null`. This decision stays intact — defaulting out of the widened set would park an
   oversight caller inside an arbitrary tenant they never picked.
3. The render tree has three branches: `Length == 0` → create link, `Length == 1 && selected` → the name,
   `Length > 1 && selected` → the dropdown.

`Length > 0 && SelectedTeam == null` matches none of them. Note this is wider than the issue states:
`Length == 1 && SelectedTeam == null` renders nothing too.

## Scope

- `TeamSelectorGate` gains the two predicates that decide between the name and the picker, so the rule is
  unit-testable (the project has no bUnit).
- `TeamSelector.razor` renders the picker whenever there are teams and no selection, with a placeholder.
- New `TeamSelectorText.SelectTeam` key so the placeholder is overridable like every other string in the
  component (it is recorded as migrated in `TextCoverageTests`, so a literal would fail the build).

## Out of scope

- Auto-selecting a team for an oversight caller. Decision (2) is deliberate and stays.
- `SixLabors.ImageSharp 3.1.12 → 4.0.0`. It is the only outstanding package update in the solution and the
  Feature Workflow would normally bundle it here; the user chose 2026-08-10 to skip it this time and keep
  the PR to the fix alone. Deliberate deviation, recorded here so it is not read as an oversight.

## Acceptance criteria

- [ ] With teams visible and no selection, the selector renders a dropdown with a "Select a team"
      placeholder — for one visible team as well as for many.
- [ ] Nothing is auto-selected: `TeamSelectionResolver` is unchanged.
- [ ] A caller with exactly one team, selected, still sees the plain name rather than a dropdown.
- [ ] A teamless caller with no `teams:read` still sees the "Create team" link, unchanged.
- [ ] A test asserts that every state renders something, and that the two branches are mutually exclusive —
      the exhaustiveness gap is the actual defect, so it is what gets pinned.
- [ ] `TextCoverageTests` still reports `TeamSelector.razor` at zero literals.
- [ ] Build clean, full test suite green.

## Done condition

The issue's repro renders a usable dropdown, the user confirms, and #214 plus the `Requests.md` entry are
closed with the shipped evidence.
