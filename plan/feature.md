# Feature: Grant-only scopes

Closes the residual half of [Tharga/Team#232](https://github.com/Tharga/Team/issues/232).
Part 1 of that issue (`o.Blazor.HiddenAccessLevels`) shipped in 3.13.1; this is part 2.

## Goal

Let a host register a scope in `IScopeRegistry` **for documentation and validation** while excluding it
from every automatic grant path. Holding such a scope becomes a recorded decision — a code-registered
tenant role you ship — rather than a side effect of being a team Owner or Administrator.

## Background

`ScopeRegistry.GetScopesForAccessLevel` returns the whole registry for Owner and Administrator, and
`GetEffectiveScopes` unions roles and overrides *in*, so there is no deny path. A scope reaching
regulated records (the reporter's case: `case:read`, governing secrecy-classified records under the
Swedish OSL/TF) is therefore held by every team administrator automatically.

The issue was answered with a **role-only workaround** — define the scope on a code-registered tenant
role and never register it — documented in `implementation-guide.md` under *Grant-only scopes*. That
works, and nothing is blocked. What it gives up is **visibility**: no catalogue entry, no description in
`ScopeView` / `TenantRoleManager`, and no typo safety, so a misspelled scope name in a role definition
grants nothing and reports nothing. For a security scope that is indistinguishable from one nobody
needed.

## The design constraint that shapes this

Registering the scope is what re-opens the grant paths. Custom-role management is gated by
`DynamicTenantRoleOptions.ManageScope` (default `team:manage`) and the per-member override picker by
`team:member:manage` — both held by every Administrator under the all-scopes rule. So a `grantOnly` flag
that only exempts `GetScopesForAccessLevel` would let any team administrator define a custom role
containing the scope, or tick it in the override picker, and grant it to themselves.

**Grant-only must therefore exempt three paths, not one:**

1. `GetScopesForAccessLevel` — no access level grants it.
2. `ValidateCustomRoles` — a tenant-defined custom role may not reference it.
3. `ScopeOverrideEditor` — the per-member and per-API-key pickers do not offer it.

What it keeps is exactly the visibility the role-only approach loses: the catalogue entry, the
description, and validation.

## Scope

**In scope**
- `ScopeDefinition.GrantOnly` and `ScopeRegistry.RegisterGrantOnly(name, description)`.
- The three exemptions above.
- `ScopeReference` / `ScopeView`: grant-only scopes appear in the catalogue, marked, with no granting
  access level.
- `TenantRoleManager`: grant-only scopes not offered when defining a tenant custom role.
- Typo safety for **code-registered** roles: a startup check naming role scopes absent from the registry.
- Docs (`implementation-guide.md`, README if it covers scopes) and a sample demonstration.
- Minor version bump to 3.14.

**Out of scope** (decided 2026-08-24 with the user)
- **Releveling the toolkit's own registrations.** `AddThargaTeamBlazor` registers `team:read`,
  `team:manage`, `team:member:manage`, `apikey:manage`, `audit:read` and `simulation:use` before the
  host's `ConfigureScopes` runs, and `Register` throws on a duplicate, so a host cannot make those
  grant-only. The issue's closing paragraph asks for this; it needs a re-registration or override
  mechanism and is deferred. The backlog entry keeps it.
- **The xunit 4.0 / Microsoft.Testing.Platform migration** (3.2.2 → 4.0.0 is outstanding). Attempted
  twice on 2026-08-18 and backed out both times on a Linux-only `BadImageFormatException` in CI; the
  backlog's recorded recommendation is that it be its own PR. `Tharga.MongoDB` 2.15.0 → 2.15.1 is
  applied here.

## Acceptance criteria

- [ ] A grant-only scope is returned by `IScopeRegistry.All` but by `GetScopesForAccessLevel` for **no**
      access level, Owner and Administrator included.
- [ ] `GetEffectiveScopes` still resolves it from a code-registered tenant role and from explicit
      `ScopeOverrides`, so `[RequireScope]` enforcement is unchanged for a legitimate holder.
- [ ] `ValidateCustomRoles` rejects a tenant-defined custom role referencing a grant-only scope, with a
      message that says it is grant-only rather than that it is unregistered.
- [ ] `TenantRoleManager` does not offer a grant-only scope when defining a custom role.
- [ ] `ScopeOverrideEditor` does not offer a grant-only scope in `TeamComponent` or `ApiKeyView`. A
      member who already holds one shows it as inherited (checked, disabled) so the effective set stays
      truthful.
- [ ] `ScopeView` lists a grant-only scope with its description, no granting access level, and a marker
      explaining why.
- [ ] A code-registered role naming a scope absent from the registry is reported at startup.
- [ ] Existing behaviour for ordinary scopes is unchanged, held by test.
- [ ] `implementation-guide.md` → *Grant-only scopes* leads with the new API and keeps the role-only
      approach as the alternative.
- [ ] Full test suite green.

## Done condition

The user has confirmed the feature is complete, #232 is answered with what shipped and closed, and the
backlog entry under *Scopes & roles* is reduced to the deferred toolkit-releveling half.
