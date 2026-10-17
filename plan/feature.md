# Feature: TenantRoleManager and ScopeView honour ITeamScopeVisibilityProvider

Source: [Tharga/Team#334](https://github.com/Tharga/Team/issues/334), the remaining part of
[#331](https://github.com/Tharga/Team/issues/331) and [#318](https://github.com/Tharga/Team/issues/318).
Driver: Quilt4Net's per-team feature areas (Atlas, Monitoring, Value groups, ...).

## Goal

A team with a feature switched off is not offered that feature's scopes when it builds a custom role, and its
access page does not list them as access it holds or could hold.

## Today

4.0 added `ITeamScopeVisibilityProvider` (#332). `TeamComponent`'s scope overrides and `ApiKeyView` apply it.
Two places still show every registered scope:

- `TenantRoleManager`: the scope picker a custom role is built from (`IScopeRegistry.All`, minus grant-only).
- `ScopeView`: every scope row on the access page.

## Scope

- **Keep the name `ITeamScopeVisibilityProvider`.** It is released; renaming would break hosts for no gain.
- **`TenantRoleManager`**: hidden scopes are left out of the picker. A role that already holds a hidden scope
  keeps it when edited (`ScopeVisibility.Persist` with no inherited scopes), so switching a feature off never
  strips scopes from a team's roles. The picker shows only the role's visible scopes.
- **`ScopeView`**: hidden rows are left out of the table and everything derived from it (grants, legends).
  Unlike role visibility, which this page deliberately ignores (#292), a hidden scope is one that doesn't apply
  to the team. A page with every scope hidden doesn't claim "No scopes are configured".
  - No flash: when a host registers its own provider, the first render waits for the team's hidden set rather
    than showing hidden rows for a moment. With the default provider nothing changes.
- Docs: a host hiding a scope should also refuse it at runtime while the feature is off, or the access page
  understates access. `ITeamScopeVisibilityProvider`'s own doc lists the new consumers.
- Tests: the custom-role persist case in `ScopeVisibilityTests`, the role model keeping hidden scopes, the
  row filter, and the wiring of both components.

## Out of scope

- #330 / PR #333 (open `UsersView` on a tab and team from a link). The issue wants it in the same 4.x release;
  it's a separate PR.
- `ScopeView`'s `Roles` parameter can render the page for a team other than the selected one, but the hidden
  set follows the selected team. No team parameter is added.
- `TenantRoleManager` and `ScopeView` are still on the #204 Pending list; no new literal text is added.

## Acceptance criteria

- With a provider hiding `atlas:*` for a team, the custom role picker doesn't offer `atlas:*`, and saving a role
  that held `atlas:manage` keeps it.
- The access page lists no `atlas:*` rows for that team.
- With no provider registered, both components behave exactly as before.
