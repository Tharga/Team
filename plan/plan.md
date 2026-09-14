# Plan: simulation dialog rework (#276)

- [x] 1. Branch `feature/simulation-dialog-rework` off `feature/role-scope-pickers`; merged `origin/master` (#277), resolved `Tharga.Team.Support.csproj` (MailKit 4.18.0 + Microsoft.Extensions.AI 10.10.0). No outdated packages. Build + full suite green (2,698).
- [x] 2. #276 core: `AccessSimulation.TeamKey`; filter applied only for the matching selected team in both claim paths. Tests first: applied on own team, ignored on another team, ignored without a team key, forged team key never adds. `AccessSimulationCookie.ReadForTeam` is the single decision, used by `TeamServerClaimsTransformation` (also gates the marker stamp, so `IsActive` stays truthful) and `TeamClaimRevalidator`. `StartAsync` binds to the selected team, overwriting any carried key; refuses with no team. `AccessSimulationTeamBindingTests` (13).
- [x] 3. #276 selection: `TeamStateService` keeps the simulated team selected when claims were issued for it; no cookie rewrite. Tests on the extracted decision (pure function beside `TeamSelectionResolver`). `SimulatedTeamSelection.KeepsSelection` (simulation for this team + `TeamClaimTypes.TeamKey` issued for it); `TeamStateService.WithSimulatedTeamAsync` adds the team via `GetTeamByKeyAsync` so the selection stands. `SimulatedTeamSelectionTests` (8). Suite 2,719 green.
- [~] 4. Target composition: pure builder for level ∪ roles ∪ ticked scopes → `AccessSimulation` (kind `User` when an unedited user pick, else new `Composed`; label from parts); `Custom` excluded from level targets; member candidates carry roles and overrides. Tests.
- [ ] 5. Dialog rewrite on the builder: scope checklist with search, unheld disabled, inherited checked/disabled, level + roles + user pickers, limitation text, gap warning kept. `AccessSimulationDialogText` catalogue; `TextCoverageTests` migrated.
- [ ] 6. `AccessSimulationIndicator` component + sample placement beside `<TeamSelector />`; hidden during demo. Tests on the visibility decision.
- [ ] 7. Demo mode trace sweep in the sample; hide what is found while `Kind == Demo`.
- [ ] 8. Manual verification in the sample: #276 repro, composed dialog, indicator, demo. Full suite.
- [ ] 9. Push for user testing.

## Notes

- `AccessSimulationKind.Composed` is an additive enum member; audit readers matching on kind see a new value.
- A cookie written before this release carries no team key and stops applying after upgrade — the caller sees their real access until they start a new simulation. Worth one line in the release notes.
- README/docs when complete: `docs/articles` access-simulation section (dialog, indicator component, team binding).
