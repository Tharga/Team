# Granting is limited to what the granter holds (#409)

Tier 1, security and authorization.

## Problem

Whoever holds `apikey:manage` or `member:manage` can grant more than they hold. Nothing compares what is granted with the
granter's own access level, roles or scopes. Latent while only Administrators hold those scopes; an escalation path as soon as a
host hands either scope to a lower level, a role or an override.

Covered: API keys (`ApiKeyManagementService` over `ApiKeyAdministrationService`) and members (`AuthorizationTeamServiceDecorator`).

## Rule

For a caller who is **not** an Administrator or Owner of the team, and does not hold `apikeys:oversee`:

1. What a new or changed key or member would hold (level scopes + role scopes + overrides, resolved through the one grant
   resolver) must be a **subset** of what the caller holds on that team.
2. A key or member that holds more than the caller cannot be changed, refreshed, locked, disabled, deleted or have its value shown.
3. No raising one's own row through `member:manage`.

For every caller:

4. `AccessLevel.Owner` is refused for a key.
5. Unregistered scope names are refused as overrides.
6. A refusal says why, naming the scopes the caller lacks.

## Where it lives

One enforcement point per surface, in the service layer, so the Blazor circuit, REST and MCP all pass through it (architecture rule 2):
a new `GrantCeiling` in `Tharga.Team.Service`, called from `ApiKeyManagementService` and `AuthorizationTeamServiceDecorator`.
It reads the caller's scopes from the principal (`TeamScopePolicy`) and resolves the target's through `TeamGrantResolver`, so
effective scopes still have one copy (`TeamGrantSingleEnforcementTests` guards that).

The UI only filters what it offers (level dropdown, role and scope pickers); the service refuses regardless.

## Out of scope

#412 (scopes for members only) and #405 (relevel the toolkit's own scopes) are separate mechanisms in the same area and stay separate.

## Open decisions

See plan.md.
