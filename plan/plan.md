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

## Notes

- 2026-10-17: the issue mentions a ready patch "that can be offered as a PR". It wasn't found locally, so this
  is implemented here following the issue's suggested shape.

## Last session

2026-10-17: steps 0–4 done and pushed for testing. Next: the user tests (a provider hiding a feature's scopes:
the custom role picker leaves them out, saving a role keeps them, the access page lists no rows for them).
Then close-out. The guide says "Added in 4.1", so this ships after, or with, PR #335 (which sets 4.1).

## README / docs changes needed at close

- `Tharga.Team.Blazor/README.md` (scope pickers paragraph, ~line 103): add `TenantRoleManager` and `ScopeView`.
- `docs/articles/implementation-guide.md` (~line 2366): the same, plus the runtime-refusal note.
- `Tharga.Team/README.md` line 31.
- Version: decide at close-out together with PR #335 (4.1).
- Propose closing comments for #318 and #331 (public repo: confirm first).
