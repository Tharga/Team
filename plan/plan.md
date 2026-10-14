# Plan: support across teams and per-case visibility

- [x] 0. NuGet update — Microsoft.* 10.0.12 → 10.0.13 patch updates; all 3234 tests pass. `e05a959`.
- [x] 1. Confirm the plan. Confirmed 2026-10-14: new cases start Private under PerCase; only the author changes visibility.
- [x] 2. Counts: `GetAwaitingSupportCountAcrossTeamsAsync` (all:read/manage), `GetAwaitingUnassignedCountAsync` (unassigned read/manage). Store members default to 0 so older stores compile. Tests: `SupportStaffCountTests` (7), `SupportStaffCountQueryTests` (2).
- [x] 3. `SupportCaseVisibility { Team, Private }` (nullable on `SupportCase`; null = nobody chose, read as Private under PerCase) and `SupportCaseOptions.Visibility` (`TeamWide` default / `PerCase`). Stored by name; pinned in `PersistedEnumRepresentationTests`.
- [x] 4. `SetCaseVisibilityAsync` — author only (staff excluded), open only (conditional write), PerCase only; audited as `visibility`; `SupportCaseChange.VisibilityChanged`.
- [x] 5. Enforcement in `AuthorizationSupportCaseServiceDecorator`: team scopes reach a case only when shared; team listing and count go through internal `ISupportCaseTeamQueries` (shared + caller's own). Store members default to nothing/zero/false. Tests: `SupportCaseVisibilityTests` (13), `SupportCaseVisibilityQueryTests` (3).
- [~] 6. Cross-team queue view in Tharga.Team.Blazor; texts; tests.
- [ ] 7. Visibility control in the case view for its author; texts; tests.
- [ ] 8. Docs: `support-cases.md`, Support README.
- [ ] 9. Close-out: re-check NuGet, archive plan, remove `plan/`, PR.

## Notes
- Developed alongside a consuming application linked to this source locally; nothing here names it.

## Last session
- 2026-10-14: Branch `feature/support-across-teams` from `origin/master`; NuGet patch update committed. Plan written, awaiting confirmation.
