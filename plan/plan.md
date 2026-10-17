# Plan: TenantRoleManager and ScopeView honour ITeamScopeVisibilityProvider

Branch: `feature/scope-visibility-role-manager-and-scope-view` (from `master` at e2f79b4; GitHub Actions flow).

## Steps

- [x] 0. NuGet updates: checked 2026-10-17 on the #314 branch, nothing outdated; this branch is cut from
  master after #332, which changed no packages. No deps commit.
- [x] 1. `TenantRoleManager`: resolve the team's hidden scopes; leave them out of the picker; the role model
  keeps a role's hidden scopes on save (`EditableTenantRole`, `TenantRoleManagerModel.Load` overload). Tests.
  - `EditableTenantRole(role, hiddenScopes)` shows only visible scopes and saves through `ScopeVisibility.Persist`
    with no inherited scopes. Like #332's pickers, a provider fault surfaces rather than being swallowed.
  - Tests: 5 in `TenantRoleManagerModelTests`, 1 in `ScopeVisibilityTests` (the custom-role persist).
- [x] 2. `ScopeView`: resolve the hidden scopes; `ScopeReference.WithoutHidden` filters the rows; the "No
  scopes are configured" alert reads the registry, not the rows; no flash with a host provider. Tests.
  - The first synchronous render keeps its code-role rows only with the default provider; a host provider
    waits for the team. The team is resolved once and shared with the role resolution. The provider fails
    open here (an access page with a switched-off scope beats an error page).
  - Tests: 3 in `ScopeReferenceTests`; `ScopeVisibilityWiringTests` (5) pins all four components to the provider.
- [x] 3. Docs: `ITeamScopeVisibilityProvider` XML doc, Blazor README, implementation guide (runtime refusal
  note), core README line.
- [x] 4. Full test suite; commit; push for testing (no PR yet).
  - Blazor 1461, Service 1246, MongoDB 190, Support 402, Email 189, Mcp 116, Entra 58, Images 11: 3673, all green.
- [x] 5. The user asked for #318, #331 and #334 all to be completed. Gaps against the older two:
  - #318 "anything derived from it (role bar counts...)": `ScopeReference.WithVisibleScopes` drops a role whose
    every scope is hidden from `ScopeView`'s role bar and pre-selection. A role with no scopes is kept. 2 tests.
  - #331 "filter their role lists through `ITenantRoleVisibilityProvider`": done for `ApiKeyView` in #332.
    Not applied to `TenantRoleManager`'s grid, which lists the team's own custom roles for editing: hiding one
    would make it uneditable (and `SetTeamCustomRolesAsync` replaces the whole set). Documented. #334, written
    later by the same reporter, also scopes TenantRoleManager's remaining work to the scope list.
  - #331 "kept and still shown on the key or role": #113's `RoleEditor` keeps a hidden role without showing
    it, and #332 did the same for scopes; custom roles now match. Treated as met.
  - #318's `ScopeFilter` parameter: served by the per-team provider, which the reporter accepted ("either works").
  - Sample: `SampleFeatureScopeVisibility` hides `billing:manage` / `case:read` unless the team's name contains
    "Billing" / "Case". `MAJOR_MINOR` set to 4.1 here too, so the branch can't ship as 4.0 if it merges before #335.
  - All 3675 tests green.

## Notes

- 2026-10-17: the issue mentions a ready patch "that can be offered as a PR". It wasn't found locally, so this
  is implemented here following the issue's suggested shape.

## Last session

2026-10-17: steps 0–5 done and pushed for testing. Next: the user tests in the sample (team name without
"Billing"/"Case": no `billing:manage` in the pickers or on /scopes, no `case:read` row and no CaseOfficer on
the role bar; a custom role holding a hidden scope keeps it when saved; rename the team to switch a feature on).
Then close-out: one PR closing #318, #331 and #334.

## README / docs changes needed at close

- `Tharga.Team.Blazor/README.md` (scope pickers paragraph, ~line 103): add `TenantRoleManager` and `ScopeView`.
- `docs/articles/implementation-guide.md` (~line 2366): the same, plus the runtime-refusal note.
- `Tharga.Team/README.md` line 31.
- Version: decide at close-out together with PR #335 (4.1).
- Propose closing comments for #318 and #331 (public repo: confirm first).
