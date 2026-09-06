# Tharga Team

A suite of NuGet packages for building multi-tenant Blazor applications with team management, authorization, and API infrastructure.

## Packages

| Package | Description | WASM-safe |
|---------|-------------|-----------|
| [Tharga.Team](https://www.nuget.org/packages/Tharga.Team) | Domain models, authorization primitives, service abstractions | Yes |
| [Tharga.Blazor](https://www.nuget.org/packages/Tharga.Blazor) | Generic Blazor UI components (buttons, breadcrumbs, etc.) | Yes |
| [Tharga.Team.Blazor](https://www.nuget.org/packages/Tharga.Team.Blazor) | Team management Blazor components | Yes |
| [Tharga.Team.MongoDB](https://www.nuget.org/packages/Tharga.Team.MongoDB) | MongoDB persistence for teams and users | No |
| [Tharga.Team.Service](https://www.nuget.org/packages/Tharga.Team.Service) | Server-side API key auth, Swagger, audit logging | No |
| [Tharga.Team.Entra](https://www.nuget.org/packages/Tharga.Team.Entra) | Microsoft Entra ID user directory (verify / list / delete users via Graph) | No |
| [Tharga.Team.Images](https://www.nuget.org/packages/Tharga.Team.Images) | Automatic squaring and downscaling of uploaded team/user icons (SkiaSharp) | No |
| [Tharga.Team.Support](https://www.nuget.org/packages/Tharga.Team.Support) | Support module — Slack notifications for audited events, routed per event | No |
| [Tharga.Team.Mcp](https://www.nuget.org/packages/Tharga.Team.Mcp) | MCP (Model Context Protocol) bridge — auth, scopes, audit for MCP tools. Renamed from `Tharga.Platform.Mcp`, which is deprecated and frozen at 3.5.x | No |

## Dependency graph

```
Tharga.Team ── plain .NET, no external dependencies
├── Tharga.Blazor ── generic Blazor UI components
│   └── Tharga.Team.Blazor ── team management UI
│       └── + Tharga.Team.Service
├── Tharga.Team.MongoDB ── persistence layer
│   └── + Tharga.MongoDB
├── Tharga.Team.Service ── server-side API + auth
│   └── + Tharga.MongoDB, ASP.NET Core
├── Tharga.Team.Entra ── Entra ID user directory (optional)
│   └── + Azure.Identity, Microsoft Graph REST
├── Tharga.Team.Images ── icon squaring + downscaling (optional)
│   └── + SkiaSharp
└── Tharga.Team.Support ── support module, Slack notifications (optional)
    └── + Tharga.Team.Service
```

## Quick Start

Install the packages:

```
dotnet add package Tharga.Team.Blazor
dotnet add package Tharga.Team.Service
dotnet add package Tharga.Team.MongoDB
```

Register everything in `Program.cs`:

```csharp
// One call to set up auth, Blazor, controllers, API keys
builder.AddThargaTeam(o =>
{
    o.Blazor.Title = "My App";
    o.Blazor.RegisterTeamService<MyTeamService, MyUserService>();
    // or, writing no storage types of your own:
    // o.Blazor.RegisterTeamService<DefaultTeamService, DefaultUserService>();
});

// MongoDB persistence (requires consumer-specific entity types)
builder.Services.AddMongoDB(o => { /* connection config */ });
builder.Services.AddThargaTeamRepository(o =>
{
    o.UseUserEntity<MyUserEntity>();
    o.UseTeamEntity<MyTeamEntity, MyTeamMember>();
});

var app = builder.Build();

app.UseThargaTeam();
```

Add to `appsettings.json`:

```json
{
  "AzureAd": {
    "Authority": "https://your-tenant.ciamlogin.com/your-tenant-id",
    "ClientId": "your-client-id",
    "TenantId": "your-tenant-id",
    "CallbackPath": "/signin-oidc"
  }
}
```

Optional features (pass via `ThargaTeamOptions`):

```csharp
builder.AddThargaTeam(o =>
{
    // Fine-grained scopes
    o.ConfigureScopes = scopes =>
    {
        scopes.Register("orders:read", AccessLevel.Viewer);
        scopes.Register("orders:write", AccessLevel.Administrator);

        // Grant-only: reaches no access level, not even Owner or Administrator, and cannot be added to a
        // tenant-defined custom role or a scope override. It is held solely through a role you register
        // in code, so holding it stays an explicit decision. Registering it keeps its catalogue entry,
        // description and typo-safety, which the older "just leave it unregistered" trick gives up.
        scopes.RegisterGrantOnly("case:read", "Read secrecy-classified case records.");
    };

    // Named roles that bundle scopes
    o.ConfigureTenantRoles = roles =>
    {
        roles.Register("Editor", new[] { "orders:read", "orders:write" });
        roles.Register("CaseOfficer", new[] { "case:read" });
    };

    // Hide an access level from the invite, member, API-key and consent pickers. Hidden is not invalid --
    // members already on the level keep working and keep their badge.
    // o.Blazor.HiddenAccessLevels = [AccessLevel.Viewer];

    // Optional: let team admins define their own custom roles at runtime (assignable to members and
    // API keys via <TenantRoleManager /> and <ApiKeyView ShowRoles="true" />).
    // o.EnableDynamicRoles = true;
    // o.DynamicRoleManageScope = "access:manage"; // scope for custom-role CRUD (default team:manage)

    // Audit logging (StorageMode defaults to Logger only — set MongoDB to populate AuditLogView)
    o.Audit = new AuditOptions { StorageMode = AuditStorageMode.MongoDB };

    // Capture an API key's private token on create/recycle (e.g. to re-deliver a minted key)
    o.AddApiKeyLifecycleHandler<MyApiKeyHandler>();
});
```

API-key behaviour (auto-lock, expiry, and the random secret length via `MinKeyLength`/`MaxKeyLength`) is configured on `o.ApiKey`. See the [Tharga.Team.Service README](Tharga.Team.Service/README.md#api-key-options) and the [Implementation Guide](docs/articles/implementation-guide.md).

## User administration & Entra directory

The user store tracks per-user **last seen** (opt-in: declare `LastSeen`/`DirectoryId` on your user entity), and `IUserManagementService` provides audited administration: verify users against Microsoft Entra ID, list users that exist only in Entra, disable users, and delete them — from the app and (explicit opt-in) from Entra. Everything cross-user — including viewing the admin lists and enumerating users via `IUserService` — requires the `users:manage` system scope, enforced in the service layer:

```csharp
// dotnet add package Tharga.Team.Entra
builder.Services.AddThargaEntraUserDirectory(builder.Configuration);   // AzureAd section + ClientSecret

builder.AddThargaTeam(o =>
{
    o.ConfigureSystemRoles = roles => roles.Map("Developer", SystemUserScopes.Manage);
});
```

The `<UsersView />` admin component picks it all up automatically. Its two tabs show each record's key with a copy control, the signed-in user's own row highlighted, and — on the Teams tab — owner, last used, a pending-invitation split, avatars and an empty-team badge. Two host opt-ins: grant the `teams:delete` **system** scope through `o.ConfigureSystemRoles` to offer team deletion, and set `<UsersView ShowAuditLogButton="true" />` for a per-row audit-history dialog. See [User management & directory](docs/articles/user-management.md).

## Team & user icons

Teams and users get real icons/avatars via two pluggable seams — **storage** (`IIconStore`, default MongoDB, no extra package) and **sourcing** (`IIconSource`: stored icon → custom → Gravatar → default → initials). Team icons need no entity change; add `Icon` to your user entity to enable user icons. A `team:manage` holder sets a team icon (upload or URL) from the team component; users upload their own from the profile page (an alternative to Gravatar), and admins (`users:manage`) can set a user's icon. Behavior is configurable and runtime-adjustable via `o.IconSettings` (Gravatar on/off + style, a default image, upload toggles). Add the optional `Tharga.Team.Images` package to auto-downscale oversized uploads (256 px) instead of rejecting them:

```csharp
builder.Services.AddThargaImageProcessing();   // optional: auto-square + downscale via SkiaSharp
```

See [Team & user icons](docs/articles/icons.md).

## Live claim revalidation

Team claims (membership, access level, tenant-role scopes, consent access) are enriched at HTTP authentication, so in a long-lived Blazor Server circuit they would otherwise stay frozen until a reload — a removed member, a downgraded access level, or a revoked consent would keep their old access, in the service-layer checks as well as the UI. Tharga.Team revalidates them on an interval and refreshes the principal **in place**, so team access is stale for at most one interval. A change in team access never signs anybody out — the session is still valid, only the access moved. The one exception is a **disabled user**, who is signed out on the same interval. On by default (30 min); tune or disable it:

```csharp
builder.AddThargaTeam(o =>
{
    o.Blazor.ClaimRevalidation.Interval = TimeSpan.FromMinutes(5); // narrow the window
    // o.Blazor.ClaimRevalidation.Enabled = false;                 // or turn it off
});
```

See [Team-claim revalidation](docs/articles/implementation-guide.md#team-claim-revalidation).

## Naming a team on an MCP call

MCP derived everything from the caller's `TeamKey` claim, so a call could only address the team the
caller was already anchored to — and a **system key is anchored to none**. Send the team key in a header
instead:

```http
POST /mcp
Authorization: Bearer <api key>
X-Team-Key: acme-corp
```

Naming a team grants the caller's **membership** scopes in it, or the team's **consented** level if they
hold a global role it consented to, and refuses otherwise — including for a suspended member. Selection
**replaces** rather than accumulates: scopes are recomputed for the named team and the principal's own
scope claims, which describe a different team, are never consulted. So naming a team can only narrow what
a caller may do.

Per call rather than per session, because `ModelContextProtocol` 2.0.0 is stateless — over HTTP,
per-request is per-call. The rule comes from the same resolver the Blazor claims builder uses, so a caller
reaches a team at the same level either way. See [Tharga.Team.Mcp](Tharga.Team.Mcp/README.md).

## Suspending instead of deleting

Deletion is final: it drops the record, the memberships, the scopes and the trail. Three reversible
alternatives cover the ordinary cases — someone on leave, a suspected compromise, a paused integration —
each bounded differently:

| To stop | Call | Scope | Reach |
|---|---|---|---|
| A person signing in at all | `IUserManagementService.SetUserDisabledAsync` | `users:manage` | The whole application |
| A person working in **one team** | `ITeamManagementService.SetMemberSuspendedAsync` | `member:manage` | That team only |
| An API key | `IApiKeyManagementService.SetKeyDisabledAsync` | `apikey:manage` | That key |

All three record **when** and **by whom**, audit both directions under distinct actions, and give back
everything on the way out. Nobody can disable or suspend themselves, and a team's Owner cannot be
suspended.

A **disabled user** is refused at sign-in and evicted from a live session within the revalidation
interval. A **suspended member** keeps their membership and still sees the team in the selector — they
simply hold no scopes in it, so every scoped operation refuses. Seeing the team is the point: a
membership that silently vanishes is indistinguishable from removal. Drop `<SuspendedTeamNotice />` into
your layout to explain it to them. They can still **leave**: that is the one team operation carrying no
scope, so holding none does not trap somebody in a team.

A **disabled key** stops authenticating, and the refusal is recorded as an authentication failure —
those attempts are the point of the trail. Refreshing it does **not** re-enable it: a refresh mints a new
secret, it is not a decision to trust the key again.

To persist any of it, declare the properties on your entity (`DisabledAt`/`DisabledBy` on the user,
`SuspendedAt`/`SuspendedBy` on the member) and implement the matching hook. Both are opt-in by shape, as
`Icon` and `LastSeen` are. See [Suspending instead of deleting](docs/articles/user-management.md#suspending-instead-of-deleting).

## Invitations

An invitation link carries a short opaque token and nothing else:

```
https://your-host/invitation?tic=84Fb6G_8BbXE
```

**Changed in 3.20.** Links used to carry base64 of `{"TeamKey":…,"Code":…}` — an encoding, not encryption, so
the team key was readable by any mail relay, helpdesk ticket or forwarded message the link passed through. It
is no longer in the link at all. Links already sent keep working; only new ones use the short form.

Invitations can expire, off by default because they never used to:

```csharp
builder.Services.Configure<InvitationOptions>(o => o.Lifetime = TimeSpan.FromDays(14));
```

An expired invitation is refused at acceptance and reported as *expired* rather than as an invalid code —
the difference between asking for a new link and retrying something that will never work.

**Extending one keeps its code**, which is the point: someone who has already mailed a link can give it more
time without the recipient's link dying. Re-inviting the same address does the same thing rather than issuing
a second live code for one seat.

```csharp
await teamManagementService.ExtendInvitationAsync(teamKey, inviteKey);
```

A host with its own team store implements one method to resolve a token without its team, and one to move an
expiry. Skip the first and links naming their team still work; see
[the implementation guide](docs/articles/implementation-guide.md#what-an-invitation-link-looks-like).

## Advanced Usage

Individual `Add*` methods remain available for partial/custom setups. See the **[Implementation Guide](docs/articles/implementation-guide.md)** for step-by-step instructions.

## Links

- [Implementation Guide](docs/articles/implementation-guide.md)
- [Documentation site](https://team.tharga.net)
- [Report an issue](https://github.com/Tharga/Team/issues)
