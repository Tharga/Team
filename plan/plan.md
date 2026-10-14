# Plan: support across teams and per-case visibility

- [x] 0. NuGet update — Microsoft.* 10.0.12 → 10.0.13 patch updates; all 3234 tests pass. `e05a959`.
- [~] 1. Confirm the plan.
- [ ] 2. Counts: `GetAwaitingSupportCountAcrossTeamsAsync`, `GetAwaitingUnassignedCountAsync` — contract, service, store, decorators; tests first.
- [ ] 3. `SupportCaseVisibility` on `SupportCase` (stored by name, BSON representation test) and `SupportCaseOptions.Visibility`.
- [ ] 4. `SetCaseVisibilityAsync` — author only, open cases only; decorators; tests.
- [ ] 5. `PerCase` enforcement in the authorization decorator, team listing and team count filtered; tests for every path in the acceptance criteria.
- [ ] 6. Cross-team queue view in Tharga.Team.Blazor; texts; tests.
- [ ] 7. Visibility control in the case view for its author; texts; tests.
- [ ] 8. Docs: `support-cases.md`, Support README.
- [ ] 9. Close-out: re-check NuGet, archive plan, remove `plan/`, PR.

## Notes
- Developed alongside a consuming application linked to this source locally; nothing here names it.

## Last session
- 2026-10-14: Branch `feature/support-across-teams` from `origin/master`; NuGet patch update committed. Plan written, awaiting confirmation.
