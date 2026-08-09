# Tharga Team Blazor
[![NuGet](https://img.shields.io/nuget/v/Tharga.Team.Blazor)](https://www.nuget.org/packages/Tharga.Team.Blazor)
![Nuget](https://img.shields.io/nuget/dt/Tharga.Team.Blazor)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Team management Blazor components for multi-tenant applications.

> **Hosting:** the components themselves are hosting-agnostic, but **this package is not WebAssembly-clean
> today**. It references `Tharga.Team.Service`, which references `Tharga.MongoDB` and `Swashbuckle.AspNetCore`
> directly — so taking the components also takes the MongoDB driver and the OpenAPI generator. Use it under
> **Blazor Server**. The contracts package, `Tharga.Team`, has no server-side dependencies and is the one a
> browser client can take cleanly. See `docs/articles/architecture.md` for the full reference graph.

## Components

- **Invitation redemption** - `TeamInviteView` is **standalone and route-agnostic**: put it on a page of your own with nothing but `[Authorize]`, and point generated links there with `o.Blazor.InvitePath`. It reads the invite code from the current URL — any route — and falls back to browser storage so the code survives a redirect through login. **Redeeming an invitation and administering a team are different capabilities with different audiences**; `TeamComponent` embeds the view for convenience, so leaving invitations to land on `/team` means whatever gate that page carries becomes the gate on redemption. `InvitePath` covers the invitation email *and* the "Copy invitation link" action — a host can rewrite what its own `ITeamEmailSender` sends, but not what an administrator copies to the clipboard.
- **Team management** - `TeamSelector`, `TeamComponent`, `TeamDialog`, `InviteUserDialog`, `TeamInviteView`. `TeamComponent`'s member list tints the signed-in user's own row (a background highlight with a left accent — no text, so nothing to localize). **Both team surfaces change shape once the list stops being scannable, at the same threshold (`8` by default, since both turn on the same fact):** `TeamSelector` gains a search box (`FilterThreshold`, `AllowFiltering`); its teamless "Create team" link can be hidden with `ShowCreateTeamLink="false"` — **presentation only**, for hosts that want creation reachable from the team page but not advertised in the top bar. To actually prevent creation use `o.Blazor.AllowTeamCreation = false`, which the service enforces as well, and `TeamComponent` switches from expandable cards to a grid with sorting, filtering and paging (`TeamLayout` = `Auto`/`Cards`/`Grid`, `TeamFilterThreshold`, `AllowTeamSorting`, `AllowTeamFiltering`, `AllowTeamPaging`, `TeamPageSize`, `TeamPageSizeOptionsValues`). The grid shows Team (sortable, filterable, default sort), Your access, Consent, Members as `3 (+2)` where two are still invited, and the team actions; the selected row is marked and opens on load. Only the team name sorts and filters — every other column is derived, and a sort control that silently does nothing is worse than none. Consent shows a drop-down only where it can be changed (the selected team) and states the level everywhere else.
- **Selected-team state** - `ITeamStateService` is what a component injects to read the current team or hear about a change. `SelectedTeamChangedEvent` is raised **only when the selection actually changed** — a different team, or the same team renamed — and **its args carry the team**, so a handler should read `e.SelectedTeam` rather than call `GetSelectedTeamAsync()`. That method *resolves* rather than reads: it validates the held team, consults browser local storage, may create your first team and may force a refresh to apply the team's claims, so it is not free to call in a loop. For the value already resolved on this circuit, `TryGetSelectedTeam(out var team)` reads it with no interop, no event and no resolution; `false` means "ask `GetSelectedTeamAsync`", not "no team is selected". See [Reacting to the selected team](https://team.tharga.net/articles/implementation-guide.html#reacting-to-the-selected-team).
- **API key management** - `ApiKeyView` for team-scoped API keys. Row actions are in a single overflow (`⋮`) **context menu** (copy, show/hide, audit, edit roles & scopes, lock, refresh, delete). On **create or regenerate** the key is shown once in a **reveal dialog** with a copy button and a "shown only once / not stored" warning — required because with `AutoLockKeys` the key is locked immediately. Shows **Created** and **Last used** columns per key (`SystemApiKeyView` shows the same for system keys); the Last used tooltip lists Created / Expiry / **Created by** (falling back to "System" for keys with no recorded creator, e.g. auto-generated). Also shows a **Tags** column (system-set key-value tags, displayed read-only via an `(i)` tooltip). Per-component parameters: `ShowScopeTooltip` (effective-scope `(i)`, default on), `ShowScopeOverrides` (scope-override editor), `ShowRoles` (tenant-role editor), `ShowLastUsed` (Last used column; 60-day expiry warning), `ShowExpiryDatePicker`, `ShowTags` (`bool?` — null = auto-show when any key has tags), `ChipTagKeys`, `ShowAuditLogButton`, `AllowGridSorting` (sort by Name / Last used, default on, Name ascending), `AllowGridFiltering` (case-insensitive Name text filter, default off), `ShowPrivateKeys` (`None`/`Mine`/`All` — include owner-scoped "private" keys; default None) and `AllowPrivilegedAccess` (let Administrator/Owner *see* private keys when `All`; view-only). `TeamComponent` shares `ShowScopeTooltip`/`ShowScopeOverrides`/`ShowRoles`; `SystemApiKeyView` uses global **system scopes** (`o.ConfigureSystemScopes`). Access is gated on the `apikey:manage` scope; cross-team access comes from mapping a role to system scopes (`o.ConfigureSystemRoles`), not per-component role parameters. "Last used" writes are throttled by `ApiKeyOptions.LastUsedThrottle` (default 1 min).
- **Scope explorer** - `ScopeView` shows which scopes a member would have: pick an **access level** (single-select bar; Owner/Administrator are merged since they grant the same scopes) and **roles** (multi-select bar), and scopes not granted by the selection are **greyed out**. Defaults to the signed-in member's own access level, roles, and **scope overrides** (overrides are highlighted with a ⭐ and an `Override` badge). Built dynamically from `IScopeRegistry` / `ITenantRoleRegistry`, so it always reflects the live configuration (no hard-coded list). Parameters: `ShowDescription` (default on), `ShowAccessLevelSelector` (default on), `ShowRoles` (default on; the roles bar auto-hides when no tenant roles are configured), `AllowGridSorting` (sort by scope name, default on), `AllowGridFiltering` (case-insensitive name filter, default off). Shows a friendly notice when no scopes are configured.
- **Custom role management** - `TenantRoleManager` lets a team administrator (`team:manage`) create / edit / delete the team's own **runtime-defined custom roles** — each granting a chosen subset of the app-registered scopes — without a code deploy. Requires `o.EnableDynamicRoles = true`. Scopes are picked from `IScopeRegistry` (so a role can never grant an unregistered scope), and the server rejects duplicate names or collisions with code-registered roles. Custom roles then appear alongside code roles in `TeamComponent`'s role picker. See "Dynamic (runtime-defined) tenant roles" below.
- **User management** - `UserProfileView`, `UsersView`.
- **Authentication** - `LoginDisplay` with login/logout and team navigation.
- **Claims augmentation** - `TeamClaimsAuthenticationStateProvider` adds `TeamKey`, `AccessLevel`, role, and scope claims. Compatible with all hosting models.
- **Scope enforcement in the circuit** - `AddThargaTeamBlazor` registers a circuit-aware `ITeamPrincipalAccessor`, so `[RequireScope]` / `[RequireAccessLevel]` on services (registered with `AddScopedWithScopes` / `AddScopedWithAccessLevel`) enforce when called from interactive Blazor Server components, not just from controllers/API.
- **Access simulation** - `AccessSimulationBar` lets a team owner/administrator (`simulation:use`) temporarily view the application as a less privileged user — a member, a tenant role, an access level, or a hand-picked set of scopes. **De-escalation only**: the effective set is always a subset of what the caller genuinely holds, because the mechanism can only remove claims. The picker states up front what a simulation *cannot* show (scopes the caller lacks; a person's system-wide access, which is unknowable), since a silent gap would lead an administrator to grant more access than needed. Actions taken while simulating are still audited as the real person, with `simulation.*` metadata. Opt in with `o.Blazor.Simulation.Enabled = true` and place `<AccessSimulationBar />` in your layout. Off by default.
- **Audit** - `AuditLogView` for viewing audit logs with charts, filtering, and per-entry failure details (a failure code + reason tooltip on the OK column).

## Quick Start (recommended)

Use `AddThargaTeam` to register all Tharga.Team services in one call:

```csharp
builder.AddThargaTeam(o =>
{
    o.Blazor.Title = "My App";
    o.Blazor.RegisterTeamService<MyTeamService, MyUserService>();
});

var app = builder.Build();
app.UseThargaTeam();
```

This registers auth (Azure AD + OIDC), API key authentication, Blazor components, and controllers with sensible defaults. See the main [README](../README.md) for the full setup including MongoDB.

## Individual Registration

For partial or custom setups, use the individual methods:

### Authentication

```csharp
builder.AddThargaAuth();   // registers auth services
app.UseThargaAuth();       // maps /login and /logout endpoints
```

Requires an `AzureAd` section in `appsettings.json`:

```json
{
  "AzureAd": {
    "Authority": "https://<tenant>.ciamlogin.com/<domain>",
    "ClientId": "<client-id>",
    "TenantId": "<tenant-id>",
    "CallbackPath": "/signin-oidc"
  }
}
```

### Team management

```csharp
builder.Services.AddThargaTeamBlazor(o =>
{
    o.Title = "My App";
    o.RegisterTeamService<MyTeamService, MyUserService>();
});
```

**UI components:**
- `<LoginDisplay />` — profile menu with Gravatar when authenticated, login button when not.
- `<UserProfileView />` — displays the user's profile info and authentication claims.

### Per-team role visibility

When `TeamComponent`'s role editor is enabled (`ShowRoles`), it offers every registered tenant role by default. To hide feature-gated roles from teams where the feature is disabled, register an `ITenantRoleVisibilityProvider` (from `Tharga.Team`):

```csharp
public sealed class FeatureGatedRoleVisibility : ITenantRoleVisibilityProvider
{
    public async Task<bool> IsRoleVisibleAsync(string teamKey, string roleName, CancellationToken ct = default)
        => await _features.IsRoleEnabledForTeamAsync(teamKey, roleName, ct);
}

builder.Services.AddSingleton<ITenantRoleVisibilityProvider, FeatureGatedRoleVisibility>();
```

`TeamComponent` filters the editor's role list per team through the provider. Hiding a role is **UI-only**: a role already assigned to a member stays assigned (it is preserved, never pruned, and reappears if the feature is re-enabled) and continues to grant its scopes at runtime. The default provider shows all roles, so this is opt-in and non-breaking.

### Dynamic (runtime-defined) tenant roles

Code-registered roles (`o.ConfigureTenantRoles`) are the same for every team and require a deploy to change. **Dynamic tenant roles** let a team administrator define their own roles per team at runtime — for example, org-specific operational roles like Registrar / Case officer / Reader / Archivist — each granting a chosen subset of the app-registered scopes.

Enable the feature, then drop the management component on a `team:manage`-gated page:

```csharp
builder.AddThargaTeam(o =>
{
    o.ConfigureScopes = s => { s.Register("case:read", AccessLevel.Custom); s.Register("case:write", AccessLevel.Custom); /* … */ };
    o.EnableDynamicRoles = true;   // registers the team-aware resolver + enables TenantRoleManager
    // o.DynamicRoleManageScope = "access:manage"; // optional — scope required to manage custom roles (default team:manage)
});
```

```razor
@attribute [Authorize]
<TenantRoleManager />
```

- **Storage & scope** — custom roles live on the team document (per team), created/edited/deleted via `ITeamManagementService.SetTeamCustomRolesAsync`, which requires `team:manage` on the team by default. Set `o.DynamicRoleManageScope` (e.g. `"access:manage"`) to gate custom-role CRUD under a dedicated scope instead — enforced by both the service layer and `TenantRoleManager`. *Assigning* a role to a member remains a `member:manage` operation.
- **No privilege escalation** — a custom role may only grant scopes registered via `o.ConfigureScopes`; the server rejects any unregistered scope, duplicate role names, and names that collide with code-registered roles.
- **Uniform surfacing** — when enabled, a member assigned a custom role receives that role's scopes as claims (server, WASM, and API-key paths). Custom roles also appear alongside code roles in the role pickers of `TeamComponent` (respecting `ITenantRoleVisibilityProvider`) and, with `ShowRoles="true"`, `ApiKeyView` — so a custom role can be assigned to a team API key.
- **Off by default** — with `EnableDynamicRoles = false` (the default) only code roles apply and behaviour is unchanged.

### Overriding the "Create team" action

By default a teamless user's **Create team** link (in `TeamSelector`) navigates to `/team`, and the **Create new Team** button (in `TeamComponent`) calls `ITeamManagementService.CreateTeamAsync()` directly. A host that wants team creation to run through its own onboarding flow (collect organization type, working language, seed templates, …) can override where these built-in entry points lead — **without** setting `AllowTeamCreation = false`, which hides the button but also blocks the programmatic create API.

Two override points, evaluated in this order (**callback → path → built-in**):

**1. `CreateTeamPath` (global, declarative).** Point the built-in entry points at your own page:

```csharp
builder.AddThargaTeam(o =>
{
    o.Blazor.CreateTeamPath = "/get-started";   // TeamSelector link + TeamComponent button navigate here
});
```

Your `/get-started` page runs the wizard and calls `CreateTeamAsync()` itself (works because `AllowTeamCreation` stays `true`), then runs onboarding.

**2. `CreateTeamRequested` (per component, imperative).** Handle the create in place — e.g. open a dialog — and skip navigation entirely. Takes precedence over `CreateTeamPath`:

```razor
<TeamSelector CreateTeamRequested="LaunchOnboardingAsync" />
<TeamComponent TMember="MyMember" CreateTeamRequested="LaunchOnboardingAsync" />

@code {
    private async Task LaunchOnboardingAsync()
    {
        var team = await OnboardingWizard.RunAsync();   // your flow: collect info + CreateTeamAsync + seed
        // navigate / refresh as needed
    }
}
```

When neither is set, behavior is unchanged. `CreateTeamPath` is `null` and both `CreateTeamRequested` callbacks are unset by default, so this is additive and non-breaking. Note the override applies to the built-in UI entry points only; teams created programmatically or via `AutoCreateFirstTeam` are unaffected.

### Cross-team visibility for oversight roles

Support and administration roles usually need to see every team, not just the ones they belong to. The
`teams:read` system scope grants **discovery only** — access inside a team still depends on that team's
consent.

```csharp
builder.AddThargaTeam(o =>
{
    // Explicit:
    o.ConfigureSystemRoles = roles => roles.Map("Developer", SystemTeamScopes.Read);

    // Or reuse the consent role list (opt-in, default false):
    o.Blazor.Consent.Roles = ["Developer"];
    o.Blazor.Consent.GrantTeamsRead = true;
});
```

`GrantTeamsRead` defaults to `false` deliberately: `Consent.Roles` means "roles a team may grant access
to", and silently promoting that to a global enumeration privilege would widen access for existing hosts
on upgrade.

A holder of `teams:read` sees every team in `TeamComponent`, `TeamSelector` and `UsersView` → Teams,
each tagged with a **Not a member** badge where applicable, followed by what that team has consented to —
**No access**, **Partial access** or **Full access**. You can select any team you can see, and the choice is
remembered across visits — but selection grants no access by itself: a non-member gets scopes only where
the team has consented to a role they hold. A team you never chose is never selected for you; when there
is no current or remembered choice, the fallback comes from your own memberships. Enumeration is not
audited; mutations still are.

The Teams tab also shows each team's **owner**, **last used** (the most recent member `LastSeen`, so it
tracks team *selection* rather than sign-in), an accepted-vs-invited split of the member count, and
badges for empty and ownerless teams. Both tabs show the record key with a copy button in the expanded
row and cross-link between a user and their teams. `<UsersView ShowAuditLogButton="true" />` adds an
opt-in per-row audit log, pinned to that team or user.

Deleting a team from `UsersView` → Teams is a separate, stronger privilege: the `teams:delete` system
scope, which **no consent option grants**. Consent decides what a team exposes inbound; it does not
decide who may destroy it. Map it explicitly to the roles that should have it:

```csharp
o.ConfigureSystemRoles = roles =>
{
    roles.Map("Developer", SystemTeamScopes.Delete);
    roles.Map("Administrator", SystemTeamScopes.Delete);
};
```

**Deleting a team takes three scopes, not one.** `users:manage` to reach the surface at all,
`teams:read` to list teams the caller is not a member of, and `teams:delete` for the action itself.
`teams:read` is not required by the delete, but without it the cross-team rows the action exists for are
not on the grid. All three must be **system** grants — a scope of the same name registered at an access
level produces a different claim type and never satisfies these checks.

Deleting a **user** requires `users:manage` alone; there is no `users:delete`. That is the wider
privilege of the two — it removes the user from every team and can optionally delete the directory
account organization-wide — so map `users:manage` only to a role you would trust with that.

Note also that `o.ConfigureSystemScopes` does **not** withhold `users:manage` or `teams:delete` from
system API keys: both are auto-registered because the admin surfaces need them grantable. Omitting them
from `ConfigureSystemScopes` has no effect on what a key may be granted.

Both tabs render row actions as a **split button** and accept the same shape of extension hooks, all
forwarded by the `UsersView` wrapper so a host can extend either tab without composing `UsersListView`
and `TeamsListView` by hand:

| | Users tab | Teams tab |
|---|---|---|
| Item inside the menu | `ActionItems` | `TeamActionItems` |
| Click callback | `ActionInvoked` (`UserRowAction`) | `TeamActionInvoked` (`TeamRowAction`) |
| Control beside the button | `ActionsTemplate` | `TeamActionsTemplate` |
| Drill-down grid | — | `MemberActionsTemplate` |

Clicking the primary button expands the row on both tabs; supplied items dispatch through the callback.

The Users tab's expanded row shows the **user key**, the **identity** (the authentication subject) and
the **directory id** (Entra `oid`), each with a copy button. The directory id distinguishes *not stored*
(the host's user entity does not declare `DirectoryId`) from *not resolved yet* — an empty value would
otherwise read as "no directory account", which is a different claim.

`<TeamComponent ShowAuditLogButton="true" />` adds a per-member audit action on the team page, pinned to
that member **and** that team, so a team owner/administrator can see what one of their members did inside
their own team. Hidden unless the caller holds `audit:read` — a system grant shows it on every team, a
team grant only on the selected one. The same exists per API key via `<ApiKeyView ShowAuditLogButton="true" />`
and `<SystemApiKeyView ShowAuditLogButton="true" />`, pinned to the key id.

Full scope matrix: [User management & directory](https://team.tharga.net/articles/user-management.html).

## Dependencies

- [Tharga.Blazor](https://www.nuget.org/packages/Tharga.Blazor) - Generic UI components.
- [Tharga.Team](https://www.nuget.org/packages/Tharga.Team) - Domain models and authorization primitives.
- [Tharga.Team.Service](https://www.nuget.org/packages/Tharga.Team.Service) - Audit types for AuditLogView.

## Related packages

| Package | Description |
|---------|-------------|
| [Tharga.Team.MongoDB](https://www.nuget.org/packages/Tharga.Team.MongoDB) | MongoDB persistence for teams and users |
| [Tharga.Team.Service](https://www.nuget.org/packages/Tharga.Team.Service) | Server-side API key auth, Swagger, audit logging |
