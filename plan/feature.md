# Scope groups

Goal: a host assigns each scope to a host-defined group (product area). ScopeView and the scope pickers show scopes under their group, in the host's order.

Scope:
- `ScopeDefinition.Group`, `ScopeGroup`, `ScopeRegistry.DefineGroup(key, displayName)`, `Register(..., group)` and `RegisterGrantOnly(..., group)` overloads, `IScopeRegistry.Groups` (default member).
- Group display name resolves through the text provider (`team.scopeGroup.<key>`, default = the display name given).
- ScopeView renders one section per group (heading + grid), ungrouped last. TenantRoleManager and ScopeOverrideEditor order options by group and tag each with its group (Radzen dropdowns have no group headings).
- A scope naming an undefined group is shown under the key as heading, after defined groups.

Acceptance criteria:
- No groups configured: nothing changes.
- Groups render in the order defined; ungrouped last; empty groups (all scopes hidden for the team) show no heading.
- Existing binary signatures (ScopeDefinition ctor, Register, RegisterGrantOnly) stay.

Done condition: tests green, docs updated, PR to master, issue closed.
