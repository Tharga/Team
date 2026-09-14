# Feature: Role and scope pickers — search, and hide Roles when there are none

Source: GitHub Tharga/Team#275.

## Goal

Make the role and scope multi-selects usable on hosts with many scopes, and stop offering a Roles picker
that can only ever be set to nothing.

## Scope

### 1. Search inside every role and scope drop-down

Case-insensitive `AllowFiltering` on:

| File | Picker |
|---|---|
| `Tharga.Team.Blazor/Framework/RoleEditor.razor` | Roles — shared by `ApiKeyView` (create + edit) and `TeamComponent` |
| `Tharga.Team.Blazor/Framework/ScopeOverrideEditor.razor` | Scope overrides — shared by `ApiKeyView` (create + edit) and `TeamComponent` |
| `Tharga.Team.Blazor/Features/Api/SystemApiKeyView.razor` | Scopes on a system API key |
| `Tharga.Team.Blazor/Features/Roles/TenantRoleManager.razor` | Scopes a custom role grants |
| `Tharga.Team.Sample/Components/SpecialApiKeyCreator.razor` | Scopes (sample app) |

Already searchable, untouched: `AccessSimulationDialog` scope picker, audit filters. Out of scope: the
access-level and consent drop-downs (short closed enums).

### 2. Roles picker only when roles exist

One rule for both components: offer Roles when `ShowRoles` is set **and the resolved role set is non-empty**,
not merely when a role source is registered.

- `ApiKeyView` — create form, row-menu "Edit roles & scopes" entry, and edit dialog all read the resolved
  `_roleDefinitions` (per selected team) instead of `ApiKeyRolePicker.RolesAvailable`.
- `TeamComponent` — the Roles column reads the team's visible role set (`GetVisibleRoles(team.Key)`, already
  computed per team asynchronously in `BuildVisibleRolesAsync`) instead of `_tenantRoleRegistry != null`.

## Acceptance criteria

- Typing in any of the five pickers narrows the list, regardless of letter case.
- A source-scanning test fails if a role or scope multi-select in the Blazor library lacks case-insensitive
  filtering, with a self-check that it found the pickers.
- With a role source registered but no roles defined (for the team), neither `ApiKeyView` nor
  `TeamComponent` shows the Roles picker/column; the API-key row menu still offers editing when scope
  overrides are enabled.
- With roles defined, behaviour is unchanged.
- The visibility rule lives in one place and is unit-tested.
- Full test suite green.

## Done condition

Acceptance criteria met, docs reviewed (README / docs), #275 answered and closed, PR open against `master`.
