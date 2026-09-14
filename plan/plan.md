# Plan: Role and scope pickers (#275)

- [x] 1. NuGet update — all packages current (Tharga.Blazor 2.3.4, bunit 2.11.3, xunit.v3 4.0.1, Tharga.MongoDB 2.18.0, MailKit 4.18.0); build + full suite green (2,675 tests). Commit `chore(deps): nuget update`.
- [x] 2. Source-scan test: every role/scope multi-select `RadzenDropDown` in `Tharga.Team.Blazor` sets `AllowFiltering="true"` and case-insensitive filtering; self-check that the known pickers were found. Added `RoleScopePickerFilteringTests`; failed on 5 pickers before the fix (incl. `AccessSimulationDialog`, filterable but case-sensitive).
- [x] 3. Add filtering to `RoleEditor`, `ScopeOverrideEditor`, `SystemApiKeyView`, `TenantRoleManager`, and the sample `SpecialApiKeyCreator`. Also made `AccessSimulationDialog` scope picker case-insensitive. Scan test green.
- [x] 4. Single visibility rule: replace `ApiKeyRolePicker.RolesAvailable(service, registry)` with a rule over the resolved role set (`ShowRoles && roles.Count > 0`); unit tests replace the `RolesAvailable_*` tests. Added internal `Framework/RolePickerGate.ShowRoles` + `RolePickerGateTests`; `RolesAvailable` and its 3 tests removed.
- [x] 5. Apply it in `ApiKeyView` (create form, row menu, edit dialog) and `TeamComponent` (Roles column via `GetVisibleRoles(team.Key)`). `ApiKeyView` uses a `ShowRolePicker` property over `_roleDefinitions`. Side effect in `TeamComponent`: dynamic roles without a code-role registry now show the column when the team has custom roles (previously hidden; `ApiKeyView` already showed them). Full suite green, 2,686 tests.
- [x] 6. Full suite; manual check in the sample app (search in each picker; Roles hidden with no roles defined). Verified in Chrome: Custom roles scopes ("ORDERS" → orders:*), API Keys Roles ("sup" → Support) and Scopes ("Content" → content:*, inherited stay disabled), System API Keys ("TEAMS" → teams:*). Roles column/picker still shown on Team and API Keys (sample registers code roles). Hidden-when-empty covered by `RolePickerGateTests`, not visually checked — the sample always has code roles.
- [~] 7. Push branch for user testing.

## Notes

- Radzen's drop-down filter is case-sensitive by default, hence `FilterCaseSensitivity.CaseInsensitive` alongside `AllowFiltering`.
- `RolesAvailable` is `internal`, so replacing it is not a public API change.
- README/docs changes when complete: mention that role pickers only appear when roles are defined (check `docs/` and README sections covering `ShowRoles`).
