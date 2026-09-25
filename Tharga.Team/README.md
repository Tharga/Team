# Tharga Team
[![NuGet](https://img.shields.io/nuget/v/Tharga.Team)](https://www.nuget.org/packages/Tharga.Team)
![Nuget](https://img.shields.io/nuget/dt/Tharga.Team)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Domain models, service abstractions, and authorization primitives for multi-tenant Blazor applications. This package has **no server-side dependencies** and works with both Blazor Server and Blazor WebAssembly.

## What's included

### Team and user models
- `ITeam` / `ITeam<TMember>` - Team aggregate with members.
- `ITeamMember` - Team member with `AccessLevel`, invitation state, tenant roles, and scope overrides.
- `IUser` - User identity.
- `Invitation`, `InviteUserModel`, `MembershipState`.

### Service interfaces
- `ITeamService` - **the storage contract you implement, not the one you inject.** Team CRUD, member management, invitations. Deliberately unchecked, because framework code reads through it while building the very claims that would authorize the read — so it is marked `[EditorBrowsable(Never)]` and a component, controller or MCP provider must inject a gated facet (`ITeamManagementService` and its siblings) instead. Includes `GetMembersAsync(teamKey)` returning `IAsyncEnumerable<ITeamMember>` for consumers that need to enumerate members without knowing the per-consumer `TMember` type.
- `ITeamManagementService` - Scope-enforced mutations (create, rename, delete, invite, etc.).
- `IUserService` - Current user resolution.
- `IApiKeyAdministrationService` / `IApiKeyManagementService` - API key management.
- `IApiKeyLifecycleHandler` - Opt-in hook receiving an API key's private token on create/recycle (and a tokenless delete signal) via `ApiKeyLifecycleContext` / `ApiKeyLifecycleReason`. Registered through `ThargaTeamOptions.AddApiKeyLifecycleHandler<T>()`.

### Authorization
- `AccessLevel` enum - Owner, Administrator, User, Viewer, Custom. `Custom` grants no inherited base scopes (effective scopes = roles ∪ scope overrides only) for least-privilege keys/members.
- `Tag` record - System-set key-value tag on an API key (a list, so a key may repeat). Set at creation only; surfaced as a `tag.{Key}` claim on the authenticated principal.
- `TeamClaimTypes` - Claim type constants (`TeamKey`, `AccessLevel`, `Scope`, `TagPrefix`).
- `IScopeRegistry` / `ScopeRegistry` - Register and resolve scopes per access level.
- `ITenantRoleRegistry` / `TenantRoleRegistry` - Register **code roles** (global, fixed at deploy time) with associated scopes.
- `ITenantRoleService` / `TenantRoleService` - Team-aware role resolution: merges code roles with a team's **runtime-defined custom roles** and unions their scopes for a member of a given team. Registered by `AddThargaDynamicTenantRoles()` (wired when `o.EnableDynamicRoles = true`); custom roles are created/edited per team via `ITeamService.SetTeamCustomRolesAsync`, with scopes constrained to app-registered scopes. The scope required to manage custom roles is configurable via `AddThargaDynamicTenantRoles(o => o.ManageScope = "…")` (default `team:manage`).
- `ITenantRoleVisibilityProvider` - Optional per-team hook that decides whether a tenant role is offered in the role editor. Default (`AllRolesVisibleTenantRoleVisibilityProvider`) shows every role; register your own to hide feature-gated roles from teams where the feature is disabled. Hiding a role never prunes existing assignments and does not affect scope resolution.
- `RequireAccessLevelAttribute` / `RequireScopeAttribute` - Declarative authorization on service methods.
- `TeamScopes` / `ApiKeyScopes` / `AuditScopes` - Built-in scope constants (`audit:read` gates the audit log).
- `SystemTeamScopes` - Cross-team system scopes: `teams:read` (enumerate any team), `teams:delete` (delete any team), `teams:assign-owner` (give an **ownerless** team an owner, chosen from its existing members; refused when the team already has one).
- `ISystemScopeRegistry` / `ISystemRoleRegistry` - Global (system) scopes for system API keys, and a mapping of app/global roles (e.g. `Developer`) to those scopes for privileged users. Configured via `o.ConfigureSystemScopes` / `o.ConfigureSystemRoles`.

### Base classes
- `TeamServiceBase` - Implement your own team service backend.
- `UserServiceBase` - Implement your own user service backend.

#### What you must override, and what happens if you do not

`UserServiceBase` leaves persistence to you. Several members are `virtual` with a **do-nothing default**,
so forgetting one produces a write that reports success and discards the data — no error, no log, and a
feature that looks configured and is not.

| Member | If you do not override it |
|---|---|
| `SetUserNameAsync` | Renaming a user reports success and changes nothing |
| `SeedUserNameAsync` | An invited user's name is discarded when they accept |
| `SetUserIconReferenceAsync` (`protected`) | An uploaded icon is stored, its reference discarded, and the blob orphaned |
| `SetUserDirectoryIdAsync` | The directory link is never persisted, so verification falls back to matching by email |

**You are told at startup.** `AddThargaTeamBlazor` reports every un-overridden member in one error,
naming the type and what each silently loses. Set `o.Blazor.ThrowOnIncompleteUserService = true` to make
it fatal instead. Members whose feature is unreachable — the icon one with no `IIconStore` registered —
are not reported, so the message stays about real mistakes.

> Deriving from `UserServiceRepositoryBase` (in **Tharga.Team.MongoDB**) implements all of these. The
> gaps only apply to a service extending `UserServiceBase` directly.

**One of them cannot be caught by an interface check.** `SetUserIconReferenceAsync` is `protected`, so it
does not appear in an interface map — a test asserting "my service implements `IUserService`" cannot see
it. The startup check reflects over the concrete type and walks the base chain instead, so an override on
your own intermediate base counts.

#### `TeamServiceBase`: what you must override

`TeamServiceBase`'s abstract members are the storage seam, and the compiler makes you implement them. The
members below were added later as `virtual`, so an existing service keeps compiling — which also means
nothing tells you when one of them is missing. Most **throw `NotSupportedException` naming themselves** when
reached. A few **return quietly**, and those are the ones to check before you rely on the feature behind
them.

| Member | Default | Reached by | If you do not override it |
|---|---|---|---|
| `GetAllTeamsInternalAsync` | **Throws**; reported at startup | Any caller holding the `teams:read` system scope | Every team page fails for that caller. Override it, or do not grant `teams:read` |
| `GetTeamKeyByInviteKeyInternalAsync` | Returns `null`; reported at startup | Opening a short invitation link (the only form the toolkit generates) | Every invitation link opens on "This invitation link is no longer valid" |
| `GetInvitationInternalAsync` | Returns `null` | Accepting an invitation while `InvitationOptions.Lifetime` is set | Expiry is never enforced on accept |
| `GetInvitedMemberNameAsync` | Returns `null` | Accepting an invitation | The name the inviter typed is not carried over to the new user |
| `SupportsSoftDelete` / `SoftDeleteTeamAsync` | `false` / hard delete | Deleting a team | Delete is permanent; there is nothing to restore. Deliberate for a store that cannot soft-delete |
| Suspension, access requests, invitation expiry updates, owner lookup, user removal, team icons | **Throw** | The feature that uses each | The feature fails loudly, naming the member |

**The startup check covers `GetAllTeamsInternalAsync`.** When anything can grant `teams:read` — a system role,
`Consent.GrantTeamsRead`, or registering it as a system scope for API keys — and your service overrides
neither it nor `GetAllTeamsAsync`, `AddThargaTeamBlazor` logs an error naming the type. Set
`o.Blazor.ThrowOnIncompleteTeamService = true` to make it fatal. This used to return nothing silently, and
the symptom was every user with the scope, Owners included, being told they are not a member of a team.

**It also covers `GetTeamKeyByInviteKeyInternalAsync`, unconditionally.** Every invitation link the toolkit
generates carries only its code, so any host that invites anybody reaches the lookup. A service that does not
override it is reported the same way, naming the member. With Tharga.Team.MongoDB, a replacement
`ITeamRepository` that does not implement `GetByInviteKeyAsync` is reported too, as an error in the log.

> Deriving from `TeamServiceRepositoryBase` (in **Tharga.Team.MongoDB**) implements all of these. The table
> only applies to a service extending `TeamServiceBase` directly.

#### The user cache

`UserServiceBase` caches resolved users. Overriding a persistence member replaces the path that
invalidated it, so the toolkit invalidates through a decorator instead — you do not need to call
`InvalidateUserCache` yourself.

If you see a change that **survives every page reload and corrects only on process restart**, that is a
stale-cache read. Nothing else looks like that; a write that never landed looks identical on screen and
has the opposite fix.

## Caching and multi-instance deployments

The claims path runs on **every authenticating request** and performs three lookups: the caller, their
membership in the selected team, and that team's custom roles. All three go through **`ITeamCache`**.

The built-in `InMemoryTeamCache` is registered for you. It holds entries in this process with no expiry,
dropped when a write invalidates them.

### If you run more than one instance, replace it

**`InMemoryTeamCache` is correct for a single instance only.** A change made through one instance never
reaches the others, so until that instance restarts:

| Changed on instance A | Instance B keeps |
|---|---|
| Member access level, tenant roles, scope overrides | issuing the old claims |
| **Member suspended** | granting them their full team scopes |
| **User disabled** | their session alive |
| Team custom roles | the old role-to-scope mapping |

**Periodic claim revalidation does not correct this** — it recomputes through the same cache, reads the same
stale entry, and concludes nothing changed.

Register your own implementation over any store every instance can see:

```csharp
builder.Services.AddSingleton<ITeamCache, RedisTeamCache>();   // before AddThargaTeam
builder.AddThargaTeam(o => { ... });
```

The toolkit registers its built-in with `TryAdd`, so yours wins.

**Then forward it from your own service's constructor.** Since 3.10.8 the toolkit **fails at startup** if you
register a custom cache your services never received, naming the types — so this step can no longer be missed
silently:

```csharp
public class TeamService : TeamServiceRepositoryBase<TeamEntity, TeamMember>
{
    public TeamService(IUserService userService, ITeamRepository<TeamEntity, TeamMember> repository,
        IMongoDbServiceFactory factory, IIconStore iconStore = null, ITeamCache cache = null)
        : base(userService, repository, factory, iconStore, cache) { }   // <- cache
}
```

A service that does not forward it falls back to the process-local cache, and the table above would apply
again — which is why registering a custom cache without forwarding it is a startup failure rather than a
warning. The check compares the cache each service actually holds against the registered one, so it cannot
misfire; a host that has registered nothing custom never sees it.

### Writing an adapter

- **`Found` and the value are separate.** Both `null` users and `null` memberships are cached deliberately —
  a non-member is *remembered* as not being one. Return `CachedValue<T>.Miss` for "no entry", not a null
  value, or every non-member request goes back to the store.
- **You serialize your own types.** `IUser` and `ITeamMember` are interfaces your entities implement, which
  is precisely why this is your adapter and not something the toolkit can ship.
- **Returning `Miss` from every read is valid** and simply disables caching. Prefer reporting a miss over
  throwing: an uncached read is slow, a throwing one breaks sign-in.
- **The two by-user removals need an index.** `RemoveUserByKeyAsync` and `RemoveMembersForUserAsync` are not
  keyed the way their entries are, so expect a companion index from a user key to that user's identity and
  teams.

### What is deliberately not cached

The **team document**. It carries the member roster, and the paths that suspend a member, remove one, assign
an owner or transfer ownership read it precisely because they need current state to decide access — a cache
there would sit in front of an authorization check. The **consent-teams query** is also uncached.

## Related packages

| Package | Description |
|---------|-------------|
| [Tharga.Team.Blazor](https://www.nuget.org/packages/Tharga.Team.Blazor) | Team management Blazor UI components, authentication |
| [Tharga.Team.MongoDB](https://www.nuget.org/packages/Tharga.Team.MongoDB) | MongoDB persistence for teams and users |
| [Tharga.Team.Service](https://www.nuget.org/packages/Tharga.Team.Service) | Server-side API key auth, Swagger, audit logging |
| [Tharga.Blazor](https://www.nuget.org/packages/Tharga.Blazor) | Generic Blazor UI components (buttons, breadcrumbs, etc.) |
