# Plan: TeamSelector offers the picker when nothing is selected

Branch: `feature/teamselector-unselected-picker` (from `master`).

## Steps

- [x] 1. Package updates up front. `dotnet outdated` is not installed on this machine; used
      `dotnet list package --outdated` across the whole solution instead. One update available:
      `SixLabors.ImageSharp 3.1.12 → 4.0.0` in `Tharga.Team.Images` (major). Called out to the user, who
      chose to skip it this time — recorded in `feature.md` under Out of scope.
- [x] 2. Added `TeamSelectorGate.ShowSelectedTeamName` and `ShowPicker`. `ShowPicker` is defined as
      "teams visible and not the name case", so the pair is exhaustive by construction rather than by two
      conditions that must be kept in agreement — which is the shape of the original defect.
- [x] 3. Added `TeamSelectorText.SelectTeam` ("Select a team", key `team.selector.selectTeam`) to `All`.
- [x] 4. Rewrote the `TeamSelector.razor` branches around the two predicates. One dropdown, not two: the
      picker branch covers the selected-many and the unselected cases alike, so the placeholder is simply
      always set and Radzen shows it while the bound value is null. Net effect on the markup is one added
      attribute — the fix is the branch conditions, not new UI.
- [x] 5. Eight tests in `TeamSelectorGateTests`, incl. `WithTeamsVisible_ExactlyOneBranchRenders` (the
      exhaustiveness property — the defect was a missing branch, so that is what gets pinned) and
      `TheBranchesActuallyDependOnBothInputs` as its self-check.
- [x] 6. `dotnet build -c Release` clean (6 pre-existing warnings, 0 errors). `dotnet test -c Release`:
      **1917 passed, 0 failed** across all 7 test projects. `TextCoverageTests` still reports
      `TeamSelector.razor` at zero literals.
- [~] 7. Commit, push the branch, ask the user to verify against the repro.

## Close-out (only once the user confirms)

- [ ] 8. Re-check package updates.
- [ ] 9. Docs review — `README.md` and `docs/`.
- [ ] 10. Close the records: `Requests.md` entry under `## Tharga.Team.Blazor`, the backlog, and GitHub
      issue #214 (comment naming what shipped and that PlutusWave can delete its wrapper component).
- [ ] 11. Archive `feature.md` to the Plan directory `done/`, `git rm -r plan`, close-out commit, PR.

## Last session

2026-08-10 — issue confirmed against the code, branch created, fix implemented and committed. Build clean,
1917 tests green. Awaiting the user's verification against the #214 repro before close-out (steps 8-11).

Worth carrying forward: the report understates the defect slightly. It describes "several teams visible,
none selected", but one visible team and no selection rendered nothing for the same reason — the old
`Length == 1` branch also required a selection. Both are covered.
