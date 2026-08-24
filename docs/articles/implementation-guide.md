# Tharga Team — Implementation Guide

Step-by-step instructions for adding Tharga Team features to a Blazor application.

## Recommended: Single-call setup

For most applications, use `AddThargaTeam` to register everything in one call:

```csharp
using Tharga.Team.Blazor.Framework;

builder.AddThargaTeam(o =>
{
    o.Blazor.Title = "My App";
    o.Blazor.RegisterTeamService<MyTeamService, MyUserService>();

    // Optional: scopes, roles, audit
    o.ConfigureScopes = scopes => { /* ... */ };
    o.ConfigureTenantRoles = roles => { /* ... */ };
    o.Audit = new AuditOptions();
});

// MongoDB persistence (always separate — requires your entity types)
builder.Services.AddMongoDB(o => { /* connection config */ });
builder.Services.AddThargaTeamRepository(o =>
{
    o.UseUserEntity<MyUserEntity>();
    o.UseTeamEntity<MyTeamEntity, MyTeamMember>();
});

var app = builder.Build();
app.UseThargaTeam();
```

This replaces Steps 1–8 below. Set sub-options to `null` to skip features you don't need (e.g. `o.Controllers = null`, `o.ApiKey = null`).

---

## Advanced: Step-by-step setup

Use the individual `Add*` methods when you need partial or custom registration. Each step is a self-contained feature that builds on previous steps. Add only what you need.

> **Secrets:** Several steps require sensitive configuration values (client IDs, connection strings, API keys).
> These should never be committed to source control. Use **Manage User Secrets** in Visual Studio
> (right-click the Server project > Manage User Secrets) or run `dotnet user-secrets init` followed by
> `dotnet user-secrets set "Section:Key" "value"` from the Server project directory.

---

## Dependency overview

```
Step 1: UI Foundation (Tharga.Blazor)
    │
Step 2: Authentication (Tharga.Team.Blazor)
    │
    ├── Step 3: API Controllers & Swagger (Tharga.Team.Service)
    │
    ├── Step 4: Team Management (Tharga.Team.Blazor + Tharga.Team.MongoDB)
    │       │
    │       ├── Step 5: API Key Authentication (Tharga.Team.Service)
    │       │
    │       ├── Step 6: Scopes (Tharga.Team + Tharga.Team.Service)
    │       │       │
    │       │       └── Step 7: Tenant Roles (Tharga.Team)
    │       │
    │       └── Step 8: Audit Logging (Tharga.Team.Service)
```

---

## Step 1: UI Foundation

Adds Radzen-based UI components: buttons, breadcrumbs, error boundary, loading indicators, and layout primitives.

### Packages

```
dotnet add package Tharga.Blazor
```

### Program.cs (Server)

```csharp
using Tharga.Blazor.Framework;

builder.Services.AddRadzenComponents();
builder.Services.AddRadzenCookieThemeService(o =>
    o.StorageKeyName = "ThemeStorageName");
builder.Services.AddThargaBlazor(o => o.Title = "My App");
```

`AddThargaBlazor` registers `BreadCrumbService`, `BlazoredLocalStorage`, and `BlazorOptions`. It also supports binding from `appsettings.json`:

```json
{
  "Tharga": {
    "Blazor": {
      "Title": "My App"
    }
  }
}
```

```csharp
builder.Services.AddThargaBlazor(configuration: builder.Configuration);
```

Code configuration takes precedence over `appsettings.json`.

### Program.cs (Client — if using WebAssembly)

```csharp
builder.Services.AddRadzenComponents();
builder.Services.AddThargaBlazor();
```

### _Imports.razor (both projects)

```razor
@using Radzen
@using Radzen.Blazor
@using Tharga.Blazor
@using Tharga.Blazor.Framework
@using Tharga.Blazor.Framework.Buttons
@using Tharga.Blazor.Features.BreadCrumbs
```

### App.razor

Add to `<head>`:
```html
<RadzenTheme Theme="material" />
```

Add to `<body>`:
```html
<script src="@Assets["_content/Tharga.Blazor/tharga.blazor.js"]"></script>
<script src="_content/Radzen.Blazor/Radzen.Blazor.js"></script>
```

### What becomes available

| Component | Description |
|-----------|-------------|
| `<ActionButton>` | Button with built-in busy state and error handling |
| `<CancelButton>` | Cancel button |
| `<CopyButton>` | Copy-to-clipboard button |
| `<StandardButton>` | General purpose button |
| `<BreadCrumbs>` | Breadcrumb navigation (registered by `AddThargaBlazor`) |
| `<Title>` | Page title (reads from `BlazorOptions.Title`) |
| `<CustomErrorBoundary>` | Error boundary with correlation ID |
| `<ExpandableCard>` | Collapsible card |
| `<Loading>` | Loading indicator — use instead of hardcoded "Loading..." text |
| `<DateTimeView>` | Formatted date/time display |
| `<TimeSpanView>` | Formatted time span display |

### Layout

Replace the default Bootstrap layout with Radzen layout components: `RadzenLayout`, `RadzenHeader`, `RadzenSidebar`, `RadzenBody`, `RadzenFooter`, `RadzenPanelMenu`.

### Verification

The app should render with Radzen styling. Buttons and layout components should work without errors.

---

## Step 2: Authentication

Adds Azure AD (CIAM) authentication with Cookie + OIDC, login/logout endpoints, and auth UI components.

**Requires:** Step 1

### Packages

```
dotnet add package Tharga.Team.Blazor
```

> `Microsoft.Identity.Web` is included transitively — no need to add it separately.

### Configuration

Add an `AzureAd` section to `appsettings.json`. Values are environment-specific:

```json
{
  "AzureAd": {
    "Authority": "",
    "ClientId": "",
    "TenantId": "",
    "CallbackPath": ""
  }
}
```

- **Authority** — varies by identity provider (e.g. CIAM: `https://<tenant>.ciamlogin.com/<domain>`, standard Entra ID: `https://login.microsoftonline.com/<tenant-id>/v2.0`)
- **ClientId** — from the Azure app registration
- **TenantId** — from the Azure app registration
- **CallbackPath** — varies by setup (e.g. `/signin-oidc`, `/authentication/login-callback`)

> **Secrets:** `ClientId` and `TenantId` may be considered sensitive depending on your environment.
> Put them in **Manage User Secrets** if you prefer not to commit them.

### Program.cs

```csharp
using Tharga.Team.Blazor.Features.Authentication;

// Service registration
builder.AddThargaAuth();

// After builder.Build()
app.UseThargaAuth();
```

### Options

```csharp
builder.AddThargaAuth(o =>
{
    o.LoginPath = "/sign-in";              // default: "/login"
    o.LogoutPath = "/sign-out";            // default: "/logout"
    o.ValidateConfiguration = false;       // default: true — throws at startup if AzureAd section is missing
});
```

### _Imports.razor

```razor
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@using Tharga.Team.Blazor.Features.Authentication
@using Tharga.Team.Blazor.Framework
```

> **Note:** `Tharga.Team.Blazor.Framework` provides the `Roles` class used in `AuthorizeView` (e.g. `Roles.Developer`, `Roles.TeamMember`).

### What becomes available

| Component | Namespace | Description |
|-----------|-----------|-------------|
| `<LoginDisplay />` | `Tharga.Team.Blazor.Features.Authentication` | Profile menu with Gravatar when authenticated, login button when not. Navigates to `/login`, `/logout`, and the profile/team pages — at `o.Blazor.ProfilePath` and `o.Blazor.TeamPath` if you mounted them somewhere other than `/profile` and `/team`. The Team item can be restricted to specific roles via `TeamMenuRoles`. |
| `<UserProfileView />` | `Tharga.Team.Blazor.Features.User` | The signed-in user's avatar, name and email, with inline editing of their own name, plus authentication claims in an expandable card. When access simulation is enabled it also renders `<AccessSimulationCard />` between the two — set `ShowAccessCard="false"` to place that yourself. |
| `<AccessSimulationCard />` | `Tharga.Team.Blazor.Features.Simulation` | Expandable card offering **demo mode** and **view as another user**, plus the way out while either is active. Rendered by `UserProfileView` by default; draws nothing unless simulation is enabled and the caller can simulate. See [access simulation](access-simulation.md) |

### Usage

Add `<LoginDisplay />` to `NavMenu.razor` header.

By default the **Team** item in the profile menu is shown to every authenticated user (whenever a team service is registered). To restrict it to specific roles, set `TeamMenuRoles` — the item is then shown only to users in at least one of those roles, and hidden for everyone else:

```razor
<LoginDisplay TeamMenuRoles="@(new[] { "Administrator", "Developer" })" />
```

Leaving `TeamMenuRoles` unset keeps the original behavior (visible to all authenticated users). This gates the menu *link* only — protect the `/team` page itself with `[Authorize(Roles = ...)]` as well.

Create a profile page:
```razor
@page "/profile"
@using Tharga.Team.Blazor.Features.User
@attribute [Authorize]

<UserProfileView />
```

### Localizing menu strings

The profile menu (`LoginDisplay`) and `TeamSelector` strings — *User, Team, Logout, Login, Create Team, Loading…* — resolve through `IThargaTextProvider`. By default they return English; register your own provider to translate them, e.g. by bridging to your app's content/localization system. Each string is a `TextKey` that bundles a stable key with its English fallback, and the keys live in `TeamMenuText`:

```csharp
public sealed class MyMenuText(IContentService content) : IThargaTextProvider
{
    // Return a translation for the key, or fall back to the bundled English default.
    public Task<string> GetAsync(TextKey key) => content.GetOrDefaultAsync(key.Key, key.Default);
}

// Register it through the platform options (same pattern as AddClaimsEnricher):
builder.AddThargaTeam(o => o.Blazor.AddTextProvider<MyMenuText>());
```

Without a provider the English defaults are used, so this is non-breaking for existing apps.

#### Finding every key you can override

`ThargaTextKeys.All` is the complete set the toolkit can render, each entry carrying its stable key and
English default. Enumerate it to generate a translation table, seed a content system, or assert in your own
tests that you have covered everything:

```csharp
foreach (var key in ThargaTextKeys.All)
    Console.WriteLine($"{key.Key}	{key.Default}");
```

It is discovered by reflection over the toolkit's catalogues, so a key added in a later version appears
without you doing anything — and a test of your own over `ThargaTextKeys.All` will tell you when one arrives
that you have not translated yet.

#### Coverage — what a provider actually reaches today

**Every surface Tharga/Team#204 names is now fully routed.** Registering a provider still does not
translate the whole UI, and the honest list matters more than a reassuring one: a component not below is
still rendering English.

**Fully routed** — every string resolves through your provider: `LoginDisplay`, `TeamSelector`,
**`TeamComponent`**, `AuditLogView`, `UsersView` **and both of its tabs (`UsersListView`,
`TeamsListView`)**, `DirectoryOnlyUsersView`, **`DeleteUserDialog`**, **`InviteUserDialog`**,
**`TeamInviteView`**, `UserIconDialog`, `TeamIconDialog`, `TeamDialog`, `AssignOwnerDialog`,
`SuspendedTeamNotice`, `AccessSimulationCard`, **`AccessSimulationBar`**, `RoleEditor` and
`ScopeOverrideEditor`.

> **`UsersView` now means the whole page.** It is a wrapper around a tab strip, and an earlier version of
> this note said it "resolves every string it renders" while `UsersListView` and `TeamsListView` held 80
> literals between them — true of the wrapper, misleading about the page. Both tabs are migrated as of
> 3.13, so the claim is now true of what you actually see.

**Still literal**, largest first: `ApiKeyView` 44, `SystemApiKeyView` 35, `ScopeView` 14,
`UserProfileView` 13, `AccessSimulationDialog` 12, `TenantRoleManager` 11, `ApiKeyRevealDialog` 2 —
**131 strings across 7 components.**

> **`AccessSimulationDialog` is the one to watch if you use simulation.** The banner is migrated as of
> 3.13, so the way *out* of a reduced session translates — but the "View as another user" screen the
> banner's own button opens does not yet.

**None of those are on the surfaces #204 names.** They are API-key, scope and simulation surfaces, which
that issue does not cover.

### Plurals, and what a translator can and cannot do

Sentences that vary with a count are **one key per form, each holding the whole sentence** — for example
`team.deleteUser.ownsOneTeam` and `team.deleteUser.ownsManyTeams`. Nothing is assembled at runtime from a
head and a tail, because those reorder in translation and often change agreement elsewhere in the sentence.

Both forms of a pair take **the same arguments in the same order**, even where one does not use all of
them, so a template can move a placeholder freely.

The limitation is deliberate: a language with **more than two plural categories** (Polish, Russian, Arabic)
cannot be expressed today. English and Swedish both have two. Since no key is a fragment, adding a
count-aware overload later would not invalidate any translation written now.

A build-time ratchet holds every one of those numbers: it fails if a count grows, fails if a count shrinks
without the record being updated, and fails if a component carrying literal text is missing from the list
altogether. So this table cannot quietly drift out of date, and "not listed" cannot come to mean "nothing to
do".

Note the counts include **dialog titles, notifications and confirmation prompts** built in each component's
C# — most of what a user actually reads — not only markup labels.

#### Messages that name something

A message with a value in it is a **template with positional placeholders**, resolved through
`TextSet.Format`:

```csharp
// key default: "Email sent to {0}"
_text.Format(TeamText.EmailSent, recipient);
```

Placeholders are positional rather than interpolated because a translated sentence often needs its parts in
a different order — a translator can move `{0}` and `{1}`, and cannot touch a C# interpolated string at all.

A malformed template — one naming a placeholder that does not exist — falls back to the English default
rather than throwing. Templates can come from your content system, so they are untrusted input on a render
path.

#### Keys are whole strings, not substitutable nouns

There is deliberately **no** `Text["Team"] = "Organisation"` knob. Composing sentences from a noun token
produces broken translations: Swedish suffixes the definite article, so *"medlem i ett team"* becomes
*"teamet"* — a form no substitution reaches — and word order moves besides. Each string is keyed whole, which
is what lets a translation differ structurally from the English rather than only lexically.

### Adding your own profile-menu items

Register extra entries on the `LoginDisplay` profile menu. They render after the built-in items and above
**Logout**, in registration order:

```csharp
builder.AddThargaTeam(o =>
{
    o.Blazor.AddMenuItem("help", "myapp.menu.help", "Help", "/help");
    o.Blazor.AddMenuItem("receipt_long", "myapp.menu.audit", "Audit log", "/audit",
        requiredScope: AuditScopes.Read);
});
```

The label is a **key plus an English default**, not a string — so it resolves through the same
`IThargaTextProvider` as the built-in entries. A host that registered a provider gets its own menu items
translated with **no further work**, in every language the provider covers.

`requiredScope` and `requiredRole` are optional; set both and both must hold. A scope is satisfied by either a
team scope or a system scope, so a cross-team administrator is not hidden from a link merely because the grant
arrived by the other provenance.

> **These gates control rendering, not access.** They hide a link the caller cannot use — the page behind it
> must still gate itself. A hidden menu item is a courtesy, never a protection.

### Version notes

- `UseThargaAuth()` requires **>= 2.0.1-pre.1** for correct async login behavior. Version 2.0.0 used `Results.Challenge` (synchronous) which caused DNS errors with some Azure AD configurations.

### Verification

The login button should appear. Clicking it redirects to Azure AD. After login, the profile menu shows with the user's Gravatar.

---

## Step 3: API Controllers & Swagger

Adds MVC controller support with OpenAPI documentation and Swagger UI.

**Requires:** Step 2

### Packages

```
dotnet add package Tharga.Team.Service
```

### Program.cs

```csharp
// Service registration
builder.Services.AddThargaControllers();

// After builder.Build()
app.UseThargaControllers();
```

### Options

```csharp
builder.Services.AddThargaControllers(o =>
{
    o.SwaggerTitle = "My API v1";          // default: "API v1"
    o.SwaggerRoutePrefix = "api-docs";     // default: "swagger"
});
```

#### Which credentials reach the API

`AuthenticationSchemes` lists the schemes Tharga's own controllers accept. It defaults to the API-key
scheme alone, which is what an API caller normally presents:

```csharp
using Microsoft.AspNetCore.Authentication.Cookies;

builder.Services.AddThargaControllers(o =>
{
    o.AuthenticationSchemes.Add(CookieAuthenticationDefaults.AuthenticationScheme);   // also allow a signed-in user
});
```

Naming the schemes up front matters more than it looks. A policy that names **no** scheme authenticates
against the application's *default* scheme — in a Blazor host that is OIDC, so an unauthenticated API call
is answered with a **302 to a login page** instead of a `401`. An agent or script following that redirect
receives HTML and a `200`. This is the same trap as [`Tharga/Mcp#18`](https://github.com/Tharga/Mcp/issues/18),
which is why both surfaces now configure schemes explicitly.

### Customizing the OpenAPI document (.NET 10+)

`AddThargaControllers` owns the OpenAPI document and registers the API-key security scheme on it. To add your own `IOpenApiDocumentTransformer` / `IOpenApiOperationTransformer` — for example, per-scope operation filtering so the generated spec only exposes operations the caller is authorized for — use the `ConfigureOpenApi` hook rather than calling `AddOpenApi("v1", …)` directly:

```csharp
builder.Services.AddThargaControllers(o =>
    o.ConfigureOpenApi(api => api.AddDocumentTransformer<ScopeFilteringDocumentTransformer>()));
```

The callback receives the same `OpenApiOptions` Tharga configures, so your transformers apply to the document Tharga already manages. Multiple `ConfigureOpenApi` calls compose in registration order. Using this hook (instead of a separate `AddOpenApi("v1", …)` call) keeps composition explicit and avoids the .NET 10 OpenAPI source generator emitting an interceptor into your project. On .NET 9 the document is Swashbuckle-based and this hook is not present.

### What becomes available

- MVC controller routing
- OpenAPI endpoint with API key security scheme
- Swagger UI at `/<SwaggerRoutePrefix>`
- API key header convention (`X-API-KEY`)
- **`GET /api/audit`** — the audit log over REST (see below)

### Reading the audit log over REST

`AddThargaControllers` ships one controller of its own, so audit data is reachable from a script or an
agent rather than only from the Blazor view.

### The caller never names the team

**There is no `teamKey` parameter.** Which team a call is about comes from the credential:

| Credential | Team | Reads |
|---|---|---|
| **Team API key** | the key itself — it can be nothing else | that team |
| **System API key**, no header | none to imply | **system audit**: every team, narrowed by filters |
| **System API key** + `X-Team-Key` | the header | that team, **if the team has consented** |

A parameter beside a team-bound credential is two sources of truth for one question. They can disagree,
and an API shaped to allow that is wrong even when the disagreement is refused — which it is. A team key
presenting a header for a *different* team is refused rather than ignored: the request has said two
incompatible things, and answering as though one of them were absent leaves the caller believing they got
what they asked for.

```
GET /api/audit?from=2026-01-01&take=100
X-API-KEY: <team key>
```

```
GET /api/audit?from=2026-01-01&take=100
X-API-KEY: <system key>
X-Team-Key: ABC123
```

| Parameter | Meaning |
|---|---|
| `team` | **A filter, not an authorization input.** Narrows a system-audit read to one team; refused if it contradicts the team the caller is already bound to |
| `from`, `to` | Time window |
| `feature`, `action` | Filter by what was done |
| `success` | `true`/`false` |
| `skip`, `take` | Paging; `take` is capped at 500 |

### Authorization is on the services, not on any surface

Reading audit goes through two registered services, and **no surface authorizes anything itself**:

| Service | Registered as | Requires |
|---|---|---|
| `IAuditReadService.QueryAsync(teamKey, query)` | team service | `audit:read` **on that team** |
| `IAuditOversightService.QueryAllAsync(query)` | system service | a **system** `audit:read` grant |

Both carry `[RequireScope(AuditScopes.Read)]` and are enforced by `ScopeProxy`, so the REST endpoint, the
Blazor view and the MCP resource give the same answer to the same caller **because they ask the same
code** — not because three implementations were tested into agreement.

> **They previously did not.** The UI and REST called `AuditAccess.CanRead`; the MCP resource gated on a
> host-configurable role instead. The same API key was admitted at one door and refused at another. A
> static helper shared by the surfaces was not enough: it still had to be *called*, and one surface
> simply did not.

The team-bound service cannot reach past the team it names, so *a team grant never reaches system-wide
audit* is a property of the shape rather than a check somebody remembered to write.

| Caller | Result |
|---|---|
| No credential | `401` |
| Team key holding `audit:read` | `200` for its own team |
| Team key presenting `X-Team-Key` for another team | `403` — a contradiction, not a preference |
| System key without a **system** `audit:read` grant | `403` |
| System key holding system `audit:read` | `200`, across every team |
| System key naming a team that has not consented | `403`, indistinguishable from naming one that does not exist |

A system grant reads one named team by *filtering* the oversight read. `ScopeProxy`'s team check does not
accept a system grant — that provenance split is deliberate, so an in-team scope can never be spent
cross-team — so the two are separate calls, and the REST endpoint tries both on the caller's behalf.

`audit:read` is registered at `AccessLevel.Administrator`, so a Viewer- or User-level caller is refused
even for its own team.

### The audit action is hidden, not shown-then-refused

`<UsersView ShowAuditLogButton="true" />` and `<TeamComponent ShowAuditLogButton="true" />` offer the
per-row audit action only to a caller who can actually read audit — the system grant for the users list,
which spans every team, and the team grant for a team's own members.

The flag is the host's permission to *offer* the feature; it is not evidence that this caller may use it.
A control that appears and then refuses is a defect this codebase has fixed twice, and the second time it
was in the users list while the team surface already had it right.

### `X-Team-Key` is a platform mechanism, not an audit one

The header is resolved **before** any endpoint runs, and a resolved team is added to the caller's claims.
So every `[RequireScope]` check works unchanged — including in **your own controllers**, which need no
knowledge that the header exists. MCP resolves the same header through the same code, so a system key
gets the same answer whichever surface it arrives on.

Configure the name once, on `TeamContextOptions.TeamKeyHeader`. It is deliberately not also an MCP
option: two places to configure one name is one place for the surfaces to disagree.

### Consent reaches audit, and stops where the level does

A caller who is **not a member** of a team reaches it if the team consented to a role they hold — at
exactly the consented level, never above it. Since `audit:read` sits at `Administrator`:

| Consented level | Reaches the team | Reads its audit |
|---|---|---|
| None | ❌ | ❌ |
| Viewer | ✅ | ❌ |
| User | ✅ | ❌ |
| Administrator | ✅ | ✅ |

Consent is therefore necessary but not sufficient: a team can grant cross-team access and grant no audit
access by the same act.

**A person and a key are answered differently, because they are different things.** A user naming a team
is resolved by membership first, then by the consent their *roles* carry. A key holds no roles at all, so
for a key the question is whether the team consented — and the **consented level is the grant**.

> **Worth being deliberate about:** a team that enables consent so its support staff can help thereby
> admits system keys at that same level. Consent is a single statement about who may enter, and it does
> not distinguish people from machines.

Denials are `403`, never `404` — a `404` would confirm whether a team exists to a caller not allowed to
know that.

### Usage

Create controllers as usual:
```csharp
[ApiController]
[Route("api/[controller]")]
public class MyController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("Hello");
}
```

### Verification

Navigate to `/swagger` — the Swagger UI should load with your controllers listed.

---

## Step 4: Team Management

Adds multi-tenant team management with MongoDB persistence, team selection, member management, and claims augmentation.

**Requires:** Step 2, and a MongoDB database (via [Tharga.MongoDB](https://www.nuget.org/packages/Tharga.MongoDB))

### Packages

```
dotnet add package Tharga.Team.MongoDB
```

> `Tharga.Team` is included transitively via `Tharga.Team.Blazor`.
> You also need `Tharga.MongoDB.Blazor` configured separately — see [Tharga.MongoDB docs](https://github.com/Tharga/MongoDB).

### Configuration

Add a MongoDB connection string to `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Default": ""
  }
}
```

> **Secrets:** The connection string contains credentials. Put it in **Manage User Secrets**.

### Program.cs

```csharp
// Service registration
builder.Services.AddThargaTeamBlazor(o =>
{
    o.Title = "My App";
    o.AutoCreateFirstTeam = true;          // default: false — auto-creates a team for first-time users
    o.CreateTeamPath = "/get-started";     // default: null — built-in "Create team" entry points navigate here instead of the bare create (see "Overriding the Create team action")
    o.ProfilePath = "/account";            // default: null — where the profile menu's User item navigates. Set it if you mounted <UserProfileView /> anywhere but /profile
    o.TeamPath = "/organisation";          // default: null — where the profile menu's Team item navigates. Set it if you mounted <TeamComponent /> anywhere but /team
    o.ShowMemberRoles = false;             // default: false — shows tenant role assignment in team UI
    o.ShowScopeOverrides = false;          // default: false — shows scope override controls in TeamComponent (team-member UI). For ApiKeyView, opt in via the [Parameter] ShowScopeOverrides on the component itself; the two flags are intentionally independent.
    o.RegisterTeamService<MyTeamService, MyUserService>();
});

builder.Services.AddThargaTeamRepository(o =>
{
    o.RegisterUserRepository<UserEntity>();
    o.RegisterTeamRepository<TeamEntity, TeamMember>();
});
```

### The two `RegisterTeamService` overloads

| Overload | Use when | Member type |
|---|---|---|
| `RegisterTeamService<TTeamService, TUserService>()` | The standard member is enough | Taken from the team service's own generic base |
| `RegisterTeamService<TTeamService, TUserService, TMember>()` | Your member carries extra properties | Exactly what you name |

The two-argument form reads the member type from the service you register — a service deriving from
`TeamServiceRepositoryBase<TEntity, TMember>` carries it — so you do not name it twice. An explicit
`TMember` always wins: that records a decision, and inference only fills a gap where none was expressed.

> **If it can find neither**, the facets layered over the team store cannot be registered and
> `TeamServiceCompletenessCheck` says so at startup, naming them. That happens when a service derives
> straight from `TeamServiceBase`, which is generic in nothing — name the member type explicitly. Set
> `ThrowOnIncompleteTeamService` to make it fatal rather than an error in the log.

### Write no storage types at all

`Tharga.Team.MongoDB` ships a standard implementation of every piece:

| Type | Replaces |
|---|---|
| `DefaultTeamMember` | `record TeamMember : TeamMemberBase;` |
| `DefaultTeamEntity` | `record TeamEntity : TeamEntityBase<TeamMember>;` |
| `DefaultTeamService` | a service implementing the two factory methods |
| `DefaultUserEntity` | `record UserEntity : EntityBase, IUser` with the optional properties |
| `DefaultUserService` | a service implementing `CreateUserEntityAsync` |

`DefaultUserEntity` declares **every** optional property — `DirectoryId`, `LastSeen` and `Icon` — so
directory linking, activity tracking and icons all work. Those are opt-in *by shape*: the toolkit
persists them only when the entity has somewhere to put them, so a smaller default would leave three
documented features silently doing nothing.

### Using your own entities

The defaults are a complete set to use or replace, not a base to extend. Deriving from
`DefaultUserEntity` compiles, but the store still reads and writes the base type, so your extra property
would not round-trip.

To add properties, declare the entity **and** its service — the factory has to construct your concrete
type, which is the only reason those methods are abstract:

```csharp
public record MyMember : TeamMemberBase { public string Department { get; init; } }
public record MyTeam : TeamEntityBase<MyMember>;

public class MyTeamService : TeamServiceRepositoryBase<MyTeam, MyMember>
{
    protected override Task<MyTeam> CreateTeam(...)          // one object initializer
    protected override Task<MyMember> CreateTeamMember(...)  // one object initializer
}
```

Everything else stays inherited, and every member is virtual — derive from `DefaultTeamService` instead
when you want the standard types but different behaviour.

> **Custom collection names:** If you need to change the MongoDB collection names (e.g. when sharing a database with a legacy app), set `TeamCollectionName` and `UserCollectionName`:
> ```csharp
> builder.Services.AddThargaTeamRepository(o =>
> {
>     o.TeamCollectionName = "MyTeams";     // default: "Team"
>     o.UserCollectionName = "MyUsers";     // default: "User"
>     o.RegisterUserRepository<UserEntity>();
>     o.RegisterTeamRepository<TeamEntity, TeamMember>();
> });
> ```

> **Note:** `AddThargaTeamBlazor()` internally calls `AddThargaBlazor()`, so `BreadCrumbService` and `BlazoredLocalStorage` are registered automatically.

### Implementing the required types

You need to create entity and service types that extend the base classes:

#### Entities

```csharp
public record UserEntity : EntityBase, IUser
{
    public required string Key { get; init; }
    public required string Identity { get; init; }
    public required string EMail { get; init; }
    public string? Name { get; init; }  // populate from 'name' claim for display names
}

public record TeamEntity : TeamEntityBase<TeamMember>;

public record TeamMember : TeamMemberBase;
```

##### Store enums by name

If your entity has an enum property, declare how it is stored:

```csharp
[BsonRepresentation(BsonType.String)]
public MyEnum Kind { get; init; }
```

The MongoDB driver's default for an enum is `Int32`, so **leaving the attribute off selects the ordinal**
without looking like a choice. An ordinal is correct only while the enum's declaration order never changes
— insert or reorder a member and every document already written silently means something else, with no
error and nothing to notice.

Storing the name costs nothing and removes the whole class of problem. A test in `Tharga.Team.MongoDB`
sweeps every persisted entity and fails if one is missing the attribute, so the toolkit's own entities
cannot drift; the same rule is worth applying to yours.

#### UserService

```csharp
public class MyUserService : UserServiceRepositoryBase<UserEntity>
{
    public MyUserService(AuthenticationStateProvider asp, IUserRepository<UserEntity> repo)
        : base(asp, repo) { }

    protected override Task<UserEntity> CreateUserEntityAsync(ClaimsPrincipal principal, string identity)
    {
        var email = principal.FindFirst(ClaimTypes.Email)?.Value
                    ?? principal.FindFirst("preferred_username")?.Value
                    ?? "unknown";
        var name = principal.FindFirst("name")?.Value;
        return Task.FromResult(new UserEntity
        {
            Key = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            Identity = identity,
            EMail = email,
            Name = name
        });
    }
}
```

> **Tip:** Populate `IUser.Name` from the `name` claim — it's used for default team names and member display names. If not set, the display name is derived from the email (e.g. `john.doe@example.com` becomes `John Doe`).

#### TeamService

```csharp
public class MyTeamService : TeamServiceRepositoryBase<TeamEntity, TeamMember>
{
    public MyTeamService(IUserService us, ITeamRepository<TeamEntity, TeamMember> repo, IMongoDbServiceFactory msf)
        : base(us, repo, msf) { }

    protected override Task<TeamEntity> CreateTeam(string teamKey, string name, IUser user, string displayName = null)
    {
        return Task.FromResult(new TeamEntity
        {
            Key = teamKey,
            Name = name,
            Members =
            [
                new TeamMember
                {
                    Key = user.Key,
                    Name = displayName,           // resolved from IUser.Name or email
                    AccessLevel = AccessLevel.Owner,
                    State = MembershipState.Member
                }
            ]
        });
    }

    protected override Task<TeamMember> CreateTeamMember(InviteUserModel model)
    {
        // Invitation and State are auto-generated by the base class if not set.
        // You only need to set them here if you want custom behavior.
        return Task.FromResult(new TeamMember
        {
            Key = null,                           // assigned when the user accepts the invite
            Name = model.Name,
            AccessLevel = model.AccessLevel
        });
    }
}
```

> **Auto-generated fields:** When `CreateTeamMember` returns a member without `Invitation`, the base class auto-generates it using the model's email, a new GUID invite key, and the current timestamp. Similarly, `State` defaults to `MembershipState.Invited` if not set. You can still set these explicitly if you need custom behavior.

### _Imports.razor

```razor
@using Tharga.Team.Blazor.Features.Team
```

### What becomes available

| Component | Description |
|-----------|-------------|
| `<TeamSelector />` | Dropdown to switch between teams. Shows the team's name instead when there is exactly one and it is selected, and the picker with a *Select a team* placeholder whenever teams are visible but none is chosen. `ShowCreateTeamLink="false"` hides the teamless "Create team" link (presentation only — `AllowTeamCreation` is what governs creation). Gains a **search box** once there are `FilterThreshold` teams (default 8) — below that a short list is read faster than it is typed into. `AllowFiltering` forces it either way |
| `<TeamComponent />` | Full team management (create, rename, delete, members). **Cards while the list is short, a sortable/filterable/paged grid once it is not** — the same threshold the selector uses. See [Finding a team](#finding-a-team-when-there-are-many-of-them) |
| `<TeamInviteView />` | Pending invitation view. **Works standalone on its own route** — see [Where invitations are redeemed](#where-invitations-are-redeemed) |
| `<UsersView />` | Admin user list: last seen, directory verification, user deletion, and a directory-only tab when a directory service is registered. Highlights your own row, shows record keys with copy, and cross-links users to teams. Its **Teams** tab shows owner, last used, invited-count split and an empty-team badge, and offers deleting any team to a holder of the `teams:delete` system scope, which no consent option grants. Opt-in `ShowAuditLogButton` adds a per-row audit log. Viewing and acting require the `users:manage` system scope — enforced in the service layer (see [User management & directory](user-management.md)) |
| `<ApiKeyView />` | API key management (requires Step 5). Shows **Created** and **Last used** columns per key, and a **Tags** column (chips for keys in `ChipTagKeys`, plus an `(i)` tooltip of all tags). Opt-in `[Parameter]` flags: `ShowAuditLogButton`, `ShowScopeOverrides` (Scopes column + create-card multi-select + Edit-Scopes dialog per row), `ChipTagKeys` |
| `<AuditLogView />` | Audit log viewer (requires Step 8) |
| `Roles.TeamMember` | Role claim added to authenticated team members |
| `Roles.Developer` | Role for developer-only UI sections |

The `TeamClaimsAuthenticationStateProvider` automatically augments the authentication state with team claims (`TeamKey`, `AccessLevel`, scopes) based on the selected team.

> **Note:** Team management works without scopes or tenant roles. The `ShowMemberRoles` and `ShowScopeOverrides` options only take effect when the corresponding registries are registered (Step 6 and Step 7). Without them, the team UI shows access levels only — which is sufficient for many applications.

### Reacting to the selected team

Inject `ITeamStateService` to read the current team, or to be told when it changes.

```csharp
@inject ITeamStateService TeamStateService

@code {
    private ITeam _team;

    protected override async Task OnInitializedAsync()
    {
        // The handler takes the team from the args. Resolve once, here -- never from the handler.
        TeamStateService.SelectedTeamChangedEvent += async (_, e) =>
        {
            _team = e.SelectedTeam;
            await InvokeAsync(StateHasChanged);
        };

        _team = await TeamStateService.GetSelectedTeamAsync();
    }
}
```

| Member | What it does | Cost |
|---|---|---|
| `GetSelectedTeamAsync()` | **Resolves** the selection: checks the held team is still visible, reads the remembered team from browser local storage, falls back to one of your memberships, may create your first team (`AutoCreateFirstTeam`), may force a page refresh so the team's claims apply, and raises `SelectedTeamChangedEvent` if the selection changed | A JS interop round trip, sometimes a navigation. **Not free to call in a loop** |
| `TryGetSelectedTeam(out var team)` | Reads the selection **already resolved** on this circuit. No interop, no event, no resolution | Free |
| `SelectedTeamChangedEvent` | Raised when the selection changes — a different team, or the same team renamed. **The args carry the team** | — |
| `SetSelectedTeamAsync(team)` | Changes the selection, remembers it across visits, and refreshes so the new claims apply | A navigation |

**Do not call `GetSelectedTeamAsync` from a `SelectedTeamChangedEvent` handler.** The name reads like a
getter, but it resolves — which is the very work the handler is reacting to — and `e.SelectedTeam` already
holds the answer. The event is raised only on a real change, so the loop this used to create is gone; reading
from the args is still both cheaper and clearer about what the handler is for.

`TryGetSelectedTeam` returns `false` when nothing has been resolved yet on this circuit. Treat that as "ask
`GetSelectedTeamAsync`", **not** as "no team is selected" — the two are different states and only the resolve
can tell them apart.

### Claims Enrichment

Team, role, access level, and scope claims are automatically enriched on the `ClaimsPrincipal` when a team is selected. Tharga.Team provides two enrichment paths:

| Path | How it works | Hosting models |
|------|-------------|----------------|
| **Server-side** (default) | `IClaimsTransformation` reads the `selected_team_id` cookie during the HTTP pipeline | Blazor Server, SSR, Hybrid |
| **Client-side** | `AuthenticationStateProvider` decorator reads from LocalStorage via JS interop | Standalone WASM only |

The server-side path is **always registered** — no configuration needed. It adds:
- `team_id` — selected team key (`TeamClaimTypes.SelectedTeamKey`). Records the *selection*, and is added
  whether or not that team grants the caller anything
- `TeamKey` — team key claim (`TeamClaimTypes.TeamKey`). The *access anchor* — added only once access to the
  selected team resolves, so it is absent when a team is selected but consented nothing
- `Role: TeamMember` — membership role
- `Role: Team{AccessLevel}` — access level role (e.g. `TeamOwner`, `TeamAdministrator`)
- `AccessLevel` — raw access level value
- Scope claims — all effective scopes for the member's access level, roles, and overrides

#### `SkipAuthStateDecoration` (default: `true`)

This setting controls whether the client-side enrichment path is also registered:

- **`true` (default)** — Only server-side enrichment. Works for **Blazor Server, SSR, and Hybrid** apps. No JS interop is used. This is the recommended setting for most applications.
- **`false`** — Additionally registers a client-side `AuthenticationStateProvider` decorator that enriches claims via LocalStorage/JS interop. Only needed for **standalone Blazor WebAssembly** apps that have no server-side HTTP pipeline.

> **Warning:** Setting `SkipAuthStateDecoration = false` on a Server/SSR app will cause a blank page (silent deadlock from JS interop during prerendering).

> **Unproven:** the `false` path has never been verified against a real standalone WebAssembly app — there is no WASM sample in the repository. If you are the first to use it, expect to validate it yourself, and please report what you find. Automatic hosting-model detection was investigated so this option could disappear entirely, but four separate approaches all produced the same silent SSR hang, and the work was dropped: the evidence pointed at the `AuthenticationStateProvider` decoration pattern itself rather than at when it gets applied.

#### Which setting do I need?

| App type | Setting |
|----------|---------|
| Blazor Server | `true` (default) — no config needed |
| Blazor Server with SSR | `true` (default) — no config needed |
| Blazor Hybrid (Server + WASM) | `true` (default) — server enriches claims for all render modes |
| Standalone Blazor WASM | `false` — needs client-side enrichment |

#### Custom Claims Enricher

If you need to inject custom claims (e.g. global roles from a database) before team member lookup and consent evaluation, implement `ITeamClaimsEnricher` and register it:

```csharp
public class MyClaimsEnricher : ITeamClaimsEnricher
{
    private readonly IMyUserDatabase _db;

    public MyClaimsEnricher(IMyUserDatabase db) => _db = db;

    public async Task EnrichAsync(ClaimsIdentity identity)
    {
        var roles = await _db.GetGlobalRolesAsync(identity.Name);
        foreach (var role in roles)
        {
            if (!identity.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == role))
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }
    }
}
```

Register via options:

```csharp
builder.Services.AddThargaTeamBlazor(o =>
{
    o.AddClaimsEnricher<MyClaimsEnricher>();
    // ...
});
```

Or via `AddThargaTeam`:

```csharp
builder.AddThargaTeam(o =>
{
    o.Blazor.AddClaimsEnricher<MyClaimsEnricher>();
});
```

The enricher runs **once per request** inside `TeamServerClaimsTransformation`, before member lookup and consent evaluation. It supports full dependency injection (constructor injection). Duplicate claims are automatically prevented.

> **When team claims refresh.** `TeamServerClaimsTransformation` is an `IClaimsTransformation`, so it runs during **HTTP authentication** — a page load or the establishment of a Blazor Server circuit — not on every interaction within a live circuit. Team claims are therefore re-evaluated on page load and on team switch (switching teams forces a full reload). To keep a live circuit from acting on frozen claims — a removed member, a lowered access level, or a revoked consent — Tharga.Team also revalidates team claims periodically (see [Team-claim revalidation](#team-claim-revalidation) below), refreshing them in place without signing the user out.

**Use cases:**
- Assign global roles (e.g. `Developer`, `SystemAdministrator`) based on user identity
- Add custom claims from external systems before team consent is evaluated
- Enrich the principal with application-specific metadata

#### Team-claim revalidation

Because the claims transformation runs only at HTTP authentication, team membership, access level, tenant-role scopes, and consent-derived access would otherwise stay frozen for the life of a Blazor Server circuit — so a removed member, a downgraded access level, or a revoked consent would keep their old access until a full reload. This affects **service-layer authorization** too, not just the UI: `BlazorTeamPrincipalAccessor` falls back to the circuit's authentication state when there is no `HttpContext`, so `[RequireScope]`, `[RequireAccessLevel]`, and the `ITeamService` authorization decorator all read the frozen claims in-circuit.

To close this window, Tharga.Team revalidates team claims on an interval for the life of each Blazor Server circuit. On each tick the caller's team claims are recomputed for their selected team; if they changed, the principal is refreshed **in place** — the caller is **not** signed out, their team access is simply brought up to date (including downgrades and removal). The net guarantee: team access is stale for at most one interval instead of "until reload". Team-independent system scopes (granted by app roles) and app roles themselves are preserved; a transient recompute error fails open (current claims kept, retried next interval).

```csharp
builder.AddThargaTeam(o =>
{
    // Default: enabled, 30-minute interval.
    o.Blazor.ClaimRevalidation.Interval = TimeSpan.FromMinutes(5); // narrow the window
    // o.Blazor.ClaimRevalidation.Enabled = false;                 // or turn it off entirely
});
```

| Option | Default | Effect |
|--------|---------|--------|
| `ClaimRevalidation.Enabled` | `true` | Revalidate team claims for the life of a Server circuit. `false` reverts to pre-3.5 behaviour (claims refresh only on reload / team switch). |
| `ClaimRevalidation.Interval` | 30 minutes | How often claims are re-evaluated. Shorter narrows the staleness window at the cost of one lightweight membership read per active circuit per interval. |

> Server/SSR only. Revalidation is wired when `SkipAuthStateDecoration` is `true` (the default, and the signal for a server-hosted app); a standalone WASM client has no server circuit to revalidate this way. A team **switch** still refreshes claims immediately via a full reload regardless of this setting.

### Verification

After login, the team selector should appear. Creating a team should persist to MongoDB. Switching teams should update the claims.

---

## Step 5: API Key Authentication

Adds API key authentication so external clients can call your API using `X-API-KEY` headers.

**Requires:** Step 3, Step 4

### Program.cs

Extend the existing `AddThargaTeamBlazor` call to register the API key service, and add API key authentication:

```csharp
builder.Services.AddThargaTeamBlazor(o =>
{
    // ... existing team config ...
    o.RegisterApiKeyAdministrationService<MyApiKeyService>();
});

builder.Services.AddThargaApiKeys();

// Chain onto the existing authentication registration:
builder.Services.AddAuthentication()
    .AddThargaApiKeyAuthentication();
```

### Options

```csharp
.AddThargaApiKeyAuthentication(o =>
{
    o.AdvancedMode = false;                // default: false — simple mode auto-generates keys
    o.AutoKeyCount = 2;                    // default: 2 — number of auto-generated keys in simple mode
    o.AutoLockKeys = false;               // default: false — auto-lock keys after creation
    o.MaxExpiryDays = 365;                // default: 365 — maximum key expiry in days (null = no cap)
    o.LastUsedThrottle = TimeSpan.FromMinutes(1); // default: 1 min — min interval between "last used" timestamp writes per key (TimeSpan.Zero = stamp every request)
    o.MinKeyLength = 32;            // default: 32 — alphanumeric chars in the key secret; fixed length unless MaxKeyLength is set (floor 24 ≈143-bit; team + system keys)
    o.MaxKeyLength = null;          // default: null — when set, the length is random in [MinKeyLength, MaxKeyLength] per key instead of fixed
});
```

### What becomes available

- API key authentication handler (validates `X-API-KEY`, or `Authorization: Bearer`)
- Three authorization policies — see the table below
- API key management UI via `<ApiKeyView />` (from Step 4)
- Constants in `ApiKeyConstants.HeaderName`, `ApiKeyConstants.PolicyName`

#### Which policy to use

**The first two are disjoint, not a hierarchy.** `SystemApiKeyPolicy` is not "`ApiKeyPolicy` plus more".

| Policy | Team key | System key |
|---|---|---|
| `ApiKeyConstants.PolicyName` (`ApiKeyPolicy`) | ✅ | ❌ |
| `ApiKeyConstants.SystemPolicyName` (`SystemApiKeyPolicy`) | ❌ | ✅ |
| `ApiKeyConstants.AnyKeyPolicyName` (`AnyApiKeyPolicy`) | ✅ | ✅ |

> [!WARNING]
> **Requiring both admits nothing.** ASP.NET Core combines policies when several are named, so
> `RequireAuthorization(PolicyName, SystemPolicyName)` demands a key that is both at once — which no key
> is. Use `AnyKeyPolicyName` for an endpoint both kinds should reach.

**MCP endpoints need none of these** — `UseThargaMcp()` builds its own policy that admits both kinds,
provided `mcp.AddTeam()` has contributed the API-key scheme. Naming a policy there narrows the endpoint
rather than securing it.

### _Imports.razor (if referencing constants)

```razor
@using Tharga.Team.Service
```

### System-set tags

API keys can carry **system-set tags** — a key-value list (`IReadOnlyList<Tag>`, `record Tag(string Key, string Value)`) set by backend code at creation. Tags are **backend-only**: there's a `tags` parameter on `CreateKeyAsync`, no mutation API, and no input in the `ApiKeyView` create card — so an operator can't add or re-point them from the UI.

```csharp
await apiKeyManagementService.CreateKeyAsync(
    teamKey, "Firewall opener", AccessLevel.Custom,
    scopeOverrides: new[] { "firewall:open" },
    tags: new[] { new Tag("Type", "firewall"), new Tag("firewall.groupId", "ABC123") });
```

- **Surfaced as claims.** Each tag becomes a `tag.{Key}` claim on the authenticated principal (`TeamClaimTypes.TagPrefix = "tag."`) — no DB round-trip to read a key's binding. Because it's a *list*, a key may carry the same key twice (e.g. `Type=firewall` + `Type=PIM`), producing two `tag.Type` claims; read them with `user.FindAll("tag.Type")`.
- **Displayed read-only.** `ApiKeyView` shows all tags in an `(i)` tooltip; pass `ChipTagKeys` to render selected keys as chips (e.g. `ChipTagKeys="@(new[] { "Type" })"`).
- **Legacy data.** Pre-tags keys stored an empty `Tags` document; reads tolerate this automatically (it deserializes as no tags). To purge the legacy field, call `IApiKeyRepository.CleanLegacyTagsAsync()` once (server-side, safe to repeat).

### Lifecycle hook (capturing the private token)

The private API token is shown once at creation and is otherwise unrecoverable — it's never persisted, logged, or exposed programmatically. If a host needs to **capture and re-deliver** a key (e.g. minting a scoped key to hand out repeatedly), register an `IApiKeyLifecycleHandler`. It receives the token at the moment it exists — on **create** and **recycle/regenerate** — plus a tokenless **delete** signal so the host can purge its own copy.

```csharp
public class MyApiKeyHandler(ISecretProtector protector, IMyKeyStore store) : IApiKeyLifecycleHandler
{
    public async Task OnApiKeyLifecycleAsync(ApiKeyLifecycleContext ctx)
    {
        switch (ctx.Reason)
        {
            case ApiKeyLifecycleReason.Created:
            case ApiKeyLifecycleReason.Recycled:
                await store.SaveAsync(ctx.ApiKeyId, protector.Protect(ctx.PrivateToken), ctx.TeamKey, ctx.Tags);
                break;
            case ApiKeyLifecycleReason.Deleted:
                await store.RemoveAsync(ctx.ApiKeyId);
                break;
        }
    }
}

// register it inside AddThargaTeam:
builder.AddThargaTeam(o =>
{
    // ...
    o.AddApiKeyLifecycleHandler<MyApiKeyHandler>();   // may be called multiple times
});
```

- **What you get** — `ApiKeyLifecycleContext`: `Reason`, `ApiKeyId` (the stable public id), `PrivateToken` (non-null on Created/Recycled, null on Deleted), `TeamKey` (null for system keys), `IsSystemKey`, `Name`, `Tags`. Applies to both team and system keys.
- **Error policy** — if the handler throws, the originating `CreateKey`/`RefreshKey`/`DeleteKey` throws too (capture failures are not swallowed). Note this does **not** roll back: a thrown create still leaves the key in storage and a thrown recycle has already rotated the secret — treat a failure as "operation failed" and reconcile (re-recycle, or delete the orphan).
- **Scope** — fires only on explicit create/recycle/delete. Simple-mode *auto-generated* keys (created lazily by `GetKeysAsync`) and lock/scope/role edits do **not** fire it.
- **Security** — the token is handed only to in-process handlers you registered; it is still never persisted or logged by the platform. You own whatever you capture (encrypt it at rest).
- Multiple handlers can be registered; all are invoked.

### Private (owner-scoped) keys

By default every team key is visible to all team admins and any Owner can recycle/lock/delete it. For keys that gate *personal* data, mint an **owner-scoped ("private")** key — bound to a team member, hidden from others, and mutable only by its owner.

- **Mint** — server-side via `IApiKeyAdministrationService.CreateKeyAsync(..., ownerMemberKey: currentMember.Key)`, or from the UI via the "Private (only me)" toggle (which calls `IApiKeyManagementService.CreateKeyAsync(ownerScoped: true)`; the service forces the owner to the *caller's own* member key — a caller can't mint a key owned by someone else).
- **Visibility / mutation** are enforced in `ApiKeyManagementService` from the authenticated principal (a `MemberKey` claim, added by the claims transformation):
  - **Owner** sees and manages their own private keys.
  - **Developer role** sees and manages all (audit/incident escape).
  - **Privileged access levels** (Administrator/Owner) can *see* private keys **only when the host opts in** — and remain **view-only** (they cannot recycle/lock/delete others').
- **`ApiKeyView` parameters** — `ShowPrivateKeys` (`None` default / `Mine` / `All`) and `AllowPrivilegedAccess` (default false; only meaningful with `All`). The actual visibility is always intersected with the caller's identity server-side, so the flags can never reveal a key the caller isn't entitled to.
- Existing keys have a null `OwnerMemberKey` (team-wide) — behaviour is unchanged unless you opt in. Not to be confused with **system keys** (team-less infra keys via `SystemApiKeyView`); private keys are still team-scoped.

### Verification

Create an API key via the UI, then call your API with `X-API-KEY: <key>` header. The request should authenticate successfully.

---

## Step 6: Scopes

Adds fine-grained permission scopes that control access to service methods. Scopes are resolved per team member based on their access level, tenant roles, and scope overrides.

**Requires:** Step 4

### Program.cs

```csharp
using Tharga.Team;
using Tharga.Team.Service;

// Define scopes with default minimum access levels
builder.Services.AddThargaScopes(scopes =>
{
    scopes.Register("feature:read", AccessLevel.Viewer);
    scopes.Register("feature:write", AccessLevel.User);
    scopes.Register("feature:manage", AccessLevel.Administrator);
});

// Register services with scope enforcement — as a team service or a system service
builder.Services.AddTeamService<IMyService, MyService>();
builder.Services.AddSystemService<IMyAdminService, MyAdminService>();
```

### Which team service to inject

A component, controller or MCP provider should inject one of these — **never `ITeamService`**.

> [!IMPORTANT]
> **Behaviour change in 3.10.** `team:read` is now enforced on every first-level team read. A caller
> lacking it is refused where it previously succeeded.
>
> **Almost certainly a no-op for you.** The scope is registered at `AccessLevel.Viewer`, so every
> ordinary member already holds it. It bites **`AccessLevel.Custom`** — least-privilege machine keys
> carrying only their explicit grants — which until now read team metadata, the full roster with access
> levels and states, and API-key metadata regardless. Grant `team:read` to any `Custom` key that should
> keep reading team data.
>
> An application with no `IScopeRegistry` registered is unaffected: it does not use scopes, and enforcing
> would refuse reads it never gated.


| Inject | For | Checked by |
|---|---|---|
| `ITeamManagementService` | One team: its details, roster, members, and every mutation | `team:read` on reads, `team:manage` / `member:manage` on mutations |
| `ITeamDirectoryService` | The caller's **own** teams | Recomputed per team from that membership — teams not granting `team:read` are omitted |
| `ITeamOversightService` | **Every** team, regardless of membership | `teams:read` system scope. Discovery only |
| `ITeamInvitationService` | Resolving an invite code | **The code itself.** An invitee holds no scope for the team they are joining |
| `ITeamLifecycleService` | Creating a team | Authenticated caller plus `AllowTeamCreation` |

#### `ITeamService` is the contract you implement, not the one you inject

It is the host's own storage seam and is **deliberately unchecked** — framework code reads through it
while constructing the very claims that would authorize the read, so gating it would be circular and
break sign-in.

> A first-level surface injecting it bypasses authorization entirely. That is not hypothetical: it is
> how `team:read` came to be registered, documented, granted — and checked by nothing.

**Three categories, not two**, and only the first is marked by an attribute:

| Category | Marked by | Rule |
|---|---|---|
| **Gated** | `[RequireScope]` on every method | All-or-nothing. The interface is wholly team-bound or wholly system-wide, so one registration is true of every method |
| **Filtered** | nothing — stated in XML docs | A first-level read naming no team, so it cannot be gated. Recomputes the caller's scopes per item and omits what they may not see |
| **Internal** | `[EditorBrowsable(Never)]` + XML docs | The contract a host implements. Never inject from a component, controller or MCP provider |

**This is enforced, not just documented.** `InternalServiceInjectionTests` fails if any component or MCP
provider in the toolkit takes a dependency on a type marked `[EditorBrowsable(Never)]`, reading both
constructor parameters and `[Inject]` properties. Internal services are discovered by the attribute rather
than a list, so marking a new contract internal enrols it automatically.

> **The guard cannot see your assembly.** It scans the toolkit's own components and providers. If you write
> components against these services, add the equivalent test over your assembly — the rule is the same, and
> a host component injecting `ITeamService` is exactly as unchecked as a toolkit one would be. Reflect over
> your `IComponent` types, collect constructor parameters and `[Inject]` properties, and assert none is
> marked `EditorBrowsableState.Never`.

**An entry point's check need not be a scope.** An invitation is authorized by its invite code, because
the invitee is not yet a member and holds nothing. The rule is that a first-level call is *checked*, not
that it is checked by a scope.

### Where invitations are redeemed

**Redeeming an invitation and administering a team are different capabilities with different audiences.**
Put them on different routes, or the gate on one becomes the gate on the other.

`TeamComponent` renders `<TeamInviteView>` at the top of its own markup, which is why `/team` happens to
be where invitations are redeemed by default. That is convenience, not a requirement:
**`TeamInviteView` is standalone and route-agnostic.** It reads the invite code from the current URL —
any route — and falls back to browser storage, so the code survives a redirect through login.

```razor
@page "/invitation"
@attribute [Authorize]

<TeamInviteView TMember="MyMember" ShowEmptyMessage="true" />
```

Then point generated links at it:

```csharp
o.Blazor.InvitePath = "/invitation";
```

`InvitePath` covers the invitation **email** and the **"Copy invitation link"** action alike — both go
through one builder. That matters: a host can rewrite what its own `ITeamEmailSender` sends, but nothing
can rewrite what an administrator copies to the clipboard. Unset, links point at `/team` as before.

> **`[Authorize]` and nothing more.** Gating the redemption route any further reproduces the problem it
> exists to solve. If you restrict `/team` to staff roles — reasonable when teams come from a
> registration flow — and invitations land there, **the one page that redeems an invite is closed to
> exactly the people who need it**, and it fails silently from every angle: the inviter sees a normal
> link, the invitee sees a "not found" that reads like an expired invitation, and nothing surfaces
> server-side because the request never reaches the invite handling (Tharga/Team#191).

The route name is yours; `/invitation` above is only an example, and worth checking against whatever your
own stack already serves.

### Sending the invitation email

**Invitations are the only mail the toolkit sends.** `ITeamEmailSender` has a single member,
`SendInviteAsync`, so configuring email here is a decision about invitation delivery — not about handing the
toolkit your mail pipeline.

Three-way choice, and it works identically on the facade and granular paths:

```csharp
// 1. Your own sender — wins over SMTP, which is then ignored.
o.AddEmailService<MyEmailSender>();          // any ITeamEmailSender

// 2. Or the built-in SMTP sender.
o.Email = new EmailOptions
{
    SmtpHost = "smtp.example.com",
    FromAddress = "no-reply@example.com",
    // FromName falls back to the application Title when unset.
};

// 3. Or neither — no email is sent, and no sender is registered.
```

On the granular path set these on `AddThargaTeamBlazor`; on the facade set them on `AddThargaTeam` (or on its
`o.Blazor` section — the facade's own `Email` wins if both are set).

> **With no sender registered, invitations are not sent and nothing reports it.** `InviteUserDialog` and
> `TeamComponent` resolve the sender with `GetService`, so they degrade to **"Copy invitation link"** and an
> administrator delivers it by hand. That is deliberate — it is what option 3 means — but it looks the same as
> forgetting to configure email. If invitations seem to vanish, check whether a sender is registered at all.
>
> Before 3.10, `AddThargaTeamBlazor` could not register a sender at all, so a granular host hit this state
> without having chosen it (Tharga/Team#176).

### Team services and system services

Every scope-enforced service is registered as exactly one of two kinds. The choice is made once, at
registration, and applies to every method — including ones added later, which is the point: there is no
per-method annotation to forget.

| | `AddTeamService` | `AddSystemService` |
|---|---|---|
| Every method | takes the team it acts on as its first parameter, named `teamKey` | takes no `teamKey` |
| The scope must be held | **for the team named in that call** (`Scope` claim) | as a system grant (`SystemScope` claim) |
| A team must be selected | yes | no |

Registration validates the interface against its declared kind **in both directions** and throws at
startup otherwise. Rejecting a team service whose method names no team is the obvious half; rejecting a
system service that *does* take a `teamKey` is what stops a mixed interface being registered as a system
service to escape the team check.

An interface must therefore be wholly one kind. Split it if it is not — that is why
`ISystemApiKeyManagementService` exists separately from `IApiKeyManagementService`.

### Service implementation

Decorate service methods with the required scope:

```csharp
public class MyService : IMyService
{
    [RequireScope("feature:read")]
    public Task<Data> GetAsync(string teamKey) { ... }

    [RequireScope("feature:write")]
    public Task SaveAsync(string teamKey, Data data) { ... }
}
```

`ScopeProxy<T>` checks the scope before calling the method and throws `UnauthorizedAccessException`
otherwise. On a team service it checks the scope against `teamKey` **as passed in the call**, not merely
against whichever team the caller happens to have selected — so holding `feature:write` for one team does
not authorize writing to another.

> **The attribute alone enforces nothing.** Enforcement comes from the registration installing the proxy.
> A service registered with a plain `AddScoped` carries its `[RequireScope]` attributes as documentation
> and is not checked at all.

> **Works in interactive Blazor Server too.** The proxy resolves the caller via `ITeamPrincipalAccessor`. The default implementation reads `IHttpContextAccessor` (controllers/API). `AddThargaTeam` / `AddThargaTeamBlazor` automatically swap in a circuit-aware accessor that uses `HttpContext` when present and falls back to `AuthenticationStateProvider` otherwise — so a single `[RequireScope]` / `[RequireAccessLevel]` enforces both your API and interactive Blazor callers (no `HttpContext` is needed in a circuit). To plug in a different principal source, register your own `ITeamPrincipalAccessor`.

### How scopes are resolved

1. **Access level** — Owner and Administrator get all scopes. User gets scopes at User or Viewer level. Viewer gets only Viewer-level scopes. **`Custom` gets no base scopes** (and is exempt from the Owner/Administrator "all scopes" rule).
2. **Tenant roles** — Additional scopes granted by assigned roles (see Step 7).
3. **Scope overrides** — Per-member overrides set in the team management UI (when `ShowScopeOverrides = true`).

> **`AccessLevel.Custom` — least-privilege keys/members.** Use `Custom` when a principal should carry *only* its explicitly assigned roles and scope overrides, with nothing inherited from the access-level tier — e.g. a machine API key minted with a single scope. Its effective scopes are exactly `roles ∪ scopeOverrides`. Set it **explicitly**: a key created without an access level still defaults to a non-`Custom` level. `Custom` is surfaced in the `ApiKeyView` create card; it is intentionally hidden from the team-member pickers until member scope/role editing lands ([#76](https://github.com/Tharga/Team/issues/76)).

### Hiding an access level from the pickers

Every scope you register at `Administrator` makes `Viewer` and `User` less distinguishable, and if all of
them sit there the two levels resolve to exactly the same set. Offering both is then a choice with nothing
behind it that every team administrator has to reason about.

```csharp
o.Blazor.HiddenAccessLevels = [AccessLevel.Viewer];
```

That level stops being offered by the invite dialog, the member editor, the API-key level picker, and the
consent picker. Default is empty — unset behaves exactly as before.

**Hidden is not invalid.** This governs what a person can *choose*, and nothing else. Members already on the
level keep working, keep their scopes, and keep rendering their badge; if you sync members from another
system that still produces the level, they arrive and function normally. Nothing about the model, the claims
or the display changes.

| Level | Effect of hiding it |
|---|---|
| `Viewer`, `User` | Removed from every picker. The common case |
| `Custom` | Only the API-key picker offers it; hiding it removes the least-privilege machine-key option |
| `Administrator` | Allowed, but read the note below |
| `Owner` | **Throws at registration** |

**Hiding `Owner` is refused** because no picker offers it in the first place — ownership moves through
`TransferOwnershipAsync` and `SetOwnerAsync`, never by choosing a level. Accepting the setting would leave
you believing a restriction was in force when nothing had changed, so it fails at startup instead.

**Hiding `Administrator` is allowed but consequential.** It is a coherent model — the Owner still manages the
team — but management can then only be delegated by handing over ownership. Note the domain still *produces*
Administrators regardless: both ownership operations demote a displaced owner to that level, and those
members keep working. That is the hidden-is-not-invalid rule doing its job rather than a bug.

**A configuration that empties a picker is refused**, naming the surface it emptied. An invite dialog with
nothing to choose is broken, not configured.

### Grant-only scopes — keeping one out of the Owner/Administrator grant

Rule 1 above has a consequence worth stating plainly: **every scope registered with `ConfigureScopes` reaches
Owner and Administrator, unconditionally.** `GetScopesForAccessLevel` returns the whole registry for those two
levels, `GetEffectiveScopes` unions roles and overrides *in*, and there is no deny list. So a team
administrator holds every registered scope by virtue of being a team administrator.

That is usually what you want. It is not what you want for a scope that should be held only as a **recorded
decision** — a scope reaching regulated or classified records, say, where "an administrator gets it
automatically" is the wrong default.

**Register it grant-only** (3.14+):

```csharp
o.ConfigureScopes = scopes =>
{
    scopes.RegisterGrantOnly("case:read", "Read secrecy-classified case records.");
};

o.ConfigureTenantRoles = roles =>
{
    roles.Register("CaseOfficer", ["case:read"], "May read case records.");
};
```

Then use it exactly as any other scope:

```csharp
[RequireScope("case:read")]
Task<Case> GetCaseAsync(string teamKey, string caseKey);
```

#### What grant-only exempts

A grant-only scope is excluded from **every path that grants a scope automatically or from inside the
tenant** — three of them, not one:

| Path | Behaviour | Why it has to be covered |
|---|---|---|
| Access-level grants | `GetScopesForAccessLevel` returns it for **no** level, Owner and Administrator included | The headline rule |
| Tenant-defined custom roles | `SetTeamCustomRolesAsync` rejects it, and `<TenantRoleManager />` does not offer it | Defining custom roles is authorized by `DynamicTenantRoleOptions.ManageScope` — `team:manage` by default — which every administrator holds |
| Scope-override pickers | `<TeamComponent ShowScopeOverrides="true" />` and `<ApiKeyView ShowScopeOverrides="true" />` do not offer it | Editing overrides is authorized by `team:member:manage`, which every administrator also holds |

The last two matter more than they look. Exempting only the access level would leave a team administrator
able to define a role containing the scope, or tick it in the override picker, and grant it to themselves —
so the scope would be no better protected than before, while *reading* as protected.

**What still grants it**, and this is the point rather than a gap:

- A **code-registered tenant role** (`ConfigureTenantRoles`). This is the intended grant path.
- An explicit **`ScopeOverrides`** value set programmatically. Nothing validates overrides against the
  registry, so your own code can still assign one; it is the *picker* that does not offer it.

A member already holding a grant-only scope still sees it in the override picker, checked and disabled, so
the effective set on screen stays truthful. An existing override remains removable — removal is
de-escalation.

#### What you keep by registering it

Registering the scope rather than hiding it is what buys:

- **A catalogue entry with its description**, shown in `<ScopeView />` marked with a lock and granted by no
  access level.
- **Typo safety.** A misspelled scope on a role definition grants nothing and matches nothing, which for a
  security scope is indistinguishable from one nobody needed. `UnregisteredRoleScopeCheck` logs a warning at
  startup naming any role scope absent from the registry.

> **Do not reach for `AccessLevel.Custom` here.** Registering a scope at `Custom` looks like the natural way
> to say "no level grants this", and it does the opposite. The levels are `Owner=0, Administrator=1, User=2,
> Viewer=3, Custom=4`; Owner and Administrator receive *all* registered scopes regardless of
> `DefaultMinimumLevel`, and the fall-through filter is `DefaultMinimumLevel >= accessLevel`, so `Custom=4`
> satisfies User and Viewer as well. A scope registered that way is granted to **every** level. The `Custom`
> guard in `GetScopesForAccessLevel` applies to the *principal's* level, not the scope's.

#### One limitation

**The toolkit's own scopes cannot be made grant-only.** `team:read`, `team:manage`, `team:member:manage`,
`apikey:manage`, `audit:read` and `simulation:use` are registered by `AddThargaTeamBlazor` before your
`ConfigureScopes` runs, and `Register` throws on a duplicate name, so there is no way to relevel them. If you
need one of those held only by explicit grant, please open an issue describing the case.

#### The pre-3.14 alternative

Before `RegisterGrantOnly` existed, the same authorization outcome was reached by **not registering the scope
at all** and naming it only on a code-registered role:

```csharp
// Works on any version. Nothing in the enforcement path consults the scope registry.
o.ConfigureTenantRoles = roles => roles.Register("CaseOfficer", ["case:read"]);
```

This still works — `TenantRoleRegistry` stores scope names without validating them, `GetEffectiveScopes`
unions role scopes in, `ScopeProxy` checks the claim, and `GetScopesForAccessLevel` cannot return a scope
that was never registered. On 3.14+ prefer `RegisterGrantOnly`: the authorization is the same, and you keep
the catalogue entry, the description and the typo warning. The startup check will warn about a role scope
left unregistered this way, which is the nudge to move it across.

### Built-in scopes

| Scope | Kind | Source | Gates |
|-------|------|--------|-------|
| `team:read` | team | `TeamScopes.Read` | View team details & members |
| `team:manage` | team | `TeamScopes.Manage` | Rename, delete, transfer ownership, consent, custom roles |
| `member:manage` | team | `TeamScopes.MemberManage` | Invite/remove members, **suspend/restore a member**, change access level/roles/scope-overrides, edit display names |
| `teams:delete` | **system** | `SystemTeamScopes.Delete` | Delete **any** team (cross-team), and restore one that was soft-deleted |
| `teams:purge` | **system** | `SystemTeamScopes.Purge` | **Permanently** remove a soft-deleted team and destroy its stored data. Irreversible |
| `teams:read` | **system** | `SystemTeamScopes.Read` | See **every** team (cross-team discovery) |
| `teams:manage` | **system** | `SystemTeamScopes.Manage` | Rename and set the icon of **any** team — **not** consent or custom roles |
| `apikey:manage` | team | `ApiKeyScopes.Manage` | Create/refresh/lock/**disable**/delete API keys |

##### Consent is configured once, in the core package

`ConsentOptions` lives in **`Tharga.Team`**, not in the Blazor package, and is registered as a single
instance every surface reads:

```csharp
builder.AddThargaTeam(o =>
{
    o.Blazor.Consent.Roles = ["Support"];          // roles a team may consent to
    o.Blazor.Consent.AccessLevel = AccessLevel.User; // level consent grants by default
});
```

It is authorization, not presentation: it decides what a caller may do in a team they do **not** belong
to. More than one surface answers that question — the Blazor circuit, and an MCP call naming a team — and
while the type lived in the Blazor package the MCP side could not reach it and briefly carried its own
copy of the default level. The same caller could then reach the same team at two different levels
depending on which door they came through. Two tests pin the single instance and its assembly.

##### `teams:manage` stops short of consent

The system scope covers **rename and icon only**. In-team `team:manage` also covers **consent** and
**custom roles**, and those stay in-team: consent is a team's own statement about what it exposes
inbound, and custom roles decide what a member may do. Both are authorization; a name and an icon are
presentation. An operator fixing a typo across teams is a much smaller claim than one overriding what a
team exposes.

Nothing in the type system can express "these two members of that scope but not those two", so the
erosion would be a one-line change that reads like consistency. Two tests assert the refusals instead.

#### Team-operation authorization

Team mutations are enforced in the **service layer** (`AuthorizationTeamServiceDecorator` over `ITeamService`), so the same rules protect the Blazor circuit **and** any consumer's REST controller that calls the service — the toolkit ships no controllers of its own.

| Operation | Allowed when |
|---|---|
| Create | authenticated **and** `AllowTeamCreation` (no scope — self-service) |
| Delete | (`team:manage` on the team **and** `AllowTeamCreation`) **or** `teams:delete` |
| Restore | Same as Delete — restoring undoes it and is strictly less destructive |
| Purge | `teams:purge` **only**. No team-level or `AllowTeamCreation` path: destroying a team's data is not something a tenant should reach by holding `team:manage` |
| Rename / Consent | `team:manage` on the team |
| Member invite/remove/role/overrides/display-name | `member:manage` on the team |
| Transfer ownership | Owner only |

### Deleting a team: soft delete, restore and purge

**From 3.13.1 deleting a team is recoverable by default.** `DeleteTeamAsync` marks the team deleted and
hides it from every read; the record survives until it is purged.

```csharp
o.TeamDeleteMode = TeamDeleteMode.Soft;   // default
o.TeamDeleteMode = TeamDeleteMode.Hard;   // pre-3.13.1 behaviour: remove and destroy in one step
```

Nothing in the API changed and a deleted team still disappears from every list, lookup, team selector and
MCP resource — and stops granting access, because the membership and consent reads that issue claims
exclude it too. The observable difference is that the document survives, and that **deleting no longer
needs the privilege to destroy stored data**.

| Operation | Scope | Reversible | Needs the storage privilege |
|---|---|---|---|
| Delete | `teams:delete` | Yes — restore | No |
| Restore | `teams:delete` | — | No |
| **Purge** | **`teams:purge`** | **No** | **Yes** |

> **Purge is the only operation that destroys data, and it requires the privilege to do so.** The MongoDB
> adapter drops the team's database, and in a per-team-database deployment (`DatabasePart` = team key) that
> needs a database user permitted to run `dropDatabase`. **MongoDB Atlas's `readWriteAnyDatabase` does not
> include it** — you need `dbAdminAnyDatabase`, `atlasAdmin`, or a custom role, and because the database
> name is generated per team the grant has to cover *any* database matching the pattern.
>
> **A deployment that never purges can withhold both `teams:purge` and that database grant entirely** and
> still delete teams normally. That is the point of the split (Tharga/Team#224).
>
> When the privilege is missing, purge fails with a `TeamStorageException` naming what the deployment has
> to grant, rather than a driver stack trace. The team record is already gone at that point — the ordering
> is deliberate, so a partial failure leaves an inert orphaned database rather than a live team pointing at
> deleted data.

**A soft-deleted team keeps its key** until it is purged, so a new team can never be created on it. In a
per-team-database deployment that would otherwise point the new team at the deleted team's data.

**If your store predates this**, nothing changes: a `TeamServiceBase` that does not override
`SupportsSoftDelete` reports `false`, and its deletes stay exactly as irreversible as they were. Implement
`SoftDeleteTeamAsync` and `RestoreTeamAsync` to opt in.

Team scopes (`team:*`, `member:manage`) authorize only the caller's **own** team — the `TeamKey` claim must match the team being acted on, so an admin of one team can't act on another. `TeamComponent` mirrors this in the UI: because the scope is issued for the **selected** team only, the Rename and Delete buttons appear on the selected team and not on the other teams you belong to. Select a team to manage it. **`teams:delete`** is a **system** scope (toolkit-defined) that authorizes deleting *any* team regardless of membership and regardless of `AllowTeamCreation` — grant it to your support/dev tooling via `o.ConfigureSystemRoles` (e.g. map `Developer` → `teams:delete`) or to a system API key. Setting `AllowTeamCreation = false` disables the self-service create and in-team delete paths but never blocks `teams:delete`.

### Finding a team when there are many of them

Both team surfaces were built for a handful of teams. Past that they change shape, at the same threshold —
**8 by default**, because both turn on the same fact: whether the list can still be taken in at a glance.

**The selector** gains a search box:

```razor
<TeamSelector />                        @* search appears at 8 teams *@
<TeamSelector FilterThreshold="3" />    @* …or wherever you want it *@
<TeamSelector AllowFiltering="true" />  @* …or always *@
```

**The team list** switches from expandable cards to a grid with sorting, filtering and paging:

```razor
<TeamComponent TMember="MyMember" />                              @* cards, then a grid at 8 *@
<TeamComponent TMember="MyMember" TeamLayout="TeamListLayout.Grid" />   @* always a grid *@
<TeamComponent TMember="MyMember" TeamLayout="TeamListLayout.Cards" />  @* always cards *@
```

Cards suit a handful — the expand affordance is obvious, and a grid of three rows looks like an
administrative report of nothing much. Past the threshold that reverses: cards cannot be sorted, filtered
or paged, and a page of stacked accordions is not a list.

| Parameter | Default | What it does |
|-----------|---------|--------------|
| `TeamLayout` | `Auto` | `Cards`, `Grid`, or let the threshold decide |
| `TeamFilterThreshold` | 8 | Teams needed before the list becomes a grid, and before its filter shows |
| `AllowTeamSorting` | `true` | Sorting on the team name |
| `AllowTeamFiltering` | *threshold* | Forces the name filter on or off |
| `AllowTeamPaging` | *auto* | Pages only when there is more than one page |
| `TeamPageSize` | 10 | Teams per page |
| `TeamPageSizeOptionsValues` | 10 / 25 / 50 | Page sizes offered |

The grid shows **Team** (sortable, filterable, the default sort), **Your access** — your own level, or an
em-dash where you are not a member — **Consent**, **Members** as `3 (+2)` where two are still invited, and
the team actions. The selected team's row is marked and opens on load.

**Only the team name sorts and filters.** Every other column is derived — your level comes from your
membership row, the badges from consent — and a sort control that silently does nothing is worse than no
control.

> **Paging is a rendering fix, not a loading one.** The team list still fetches every team with its
> members up front, so paging changes how much is drawn rather than how much is fetched. A paged,
> member-less read would be a service-contract change and is not part of this.

### Alternative: Access level enforcement

For simpler cases where scopes are overkill, use access level enforcement instead:

```csharp
builder.Services.AddScopedWithAccessLevel<IMyService, MyService>();
```

```csharp
[RequireAccessLevel(AccessLevel.Administrator)]
public Task DeleteAsync(string id) { ... }
```

> A `Custom` principal is the lowest tier and fails **every** `[RequireAccessLevel]` gate (including `Viewer`). Authorize such principals with scope-based checks (`[RequireScope]`) rather than access-level enforcement.

### Verification

Call a scope-protected method as a Viewer when it requires User level — it should be denied. Elevate the member's access level and retry — it should succeed.

---

## Step 7: Tenant Roles

Adds named roles that bundle scopes together, making it easier to manage permissions for team members.

**Requires:** Step 6

### Program.cs

```csharp
builder.Services.AddThargaTenantRoles(roles =>
{
    roles.Register("Editor", new[] { "feature:read", "feature:write" });
    roles.Register("Auditor", new[] { "feature:read", "audit:read" });
});
```

### Team UI

Role assignment is a **component parameter**, not a global option. Set `ShowRoles="true"` on `<TeamComponent>`
(and on `<ApiKeyView>` to assign roles to keys):

```razor
<TeamComponent TMember="MyMember" ShowRoles="true" ShowScopeOverrides="true" />
```

### How it works

When a team member is assigned the "Editor" role, they automatically receive the `feature:read` and `feature:write` scopes in addition to their access-level scopes. Roles are combined — a member with both "Editor" and "Auditor" gets all scopes from both. Members/keys store the role **names**; the scopes are resolved live from the registry (change a role's scopes and it applies to all assignees).

### Hiding roles per team

By default the role editor offers every registered role for every team. If a role is feature-gated — only meaningful for teams that have the feature enabled — register an `ITenantRoleVisibilityProvider` to hide it where the feature is off:

```csharp
public sealed class FeatureGatedRoleVisibility : ITenantRoleVisibilityProvider
{
    public Task<bool> IsRoleVisibleAsync(string teamKey, string roleName, CancellationToken ct = default)
        => _features.IsRoleEnabledForTeamAsync(teamKey, roleName, ct);
}

builder.Services.AddSingleton<ITenantRoleVisibilityProvider, FeatureGatedRoleVisibility>();
```

`<TeamComponent>` consults the provider per team before building each row's role list. This is **display-only**: a role already assigned to a member is preserved (never pruned) and still grants its scopes at runtime even while hidden — it simply isn't offered as a new choice, and reappears in the editor if the feature is re-enabled. The default provider shows all roles, so the hook is opt-in and non-breaking.

### Dynamic (runtime-defined) tenant roles

The roles registered above via `AddThargaTenantRoles` are **code roles** — global and fixed at deploy time. To let a team administrator define their **own** roles per team at runtime (e.g. org-specific Registrar / Case officer / Reader / Archivist), enable dynamic tenant roles and add the management component:

```csharp
builder.AddThargaTeam(o =>
{
    o.ConfigureScopes = s => { s.Register("case:read", AccessLevel.Custom); s.Register("case:write", AccessLevel.Custom); };
    o.EnableDynamicRoles = true;
    // o.DynamicRoleManageScope = "access:manage"; // optional — scope for custom-role CRUD (default team:manage)
});
```

```razor
@* a team:manage-gated admin page *@
<TenantRoleManager />
```

- **Per-team storage** — custom roles are stored on the team document and edited via `ITeamManagementService.SetTeamCustomRolesAsync`, which requires **`team:manage`** on the team by default; set `o.DynamicRoleManageScope` to require a different scope (e.g. **`access:manage`**) instead, honoured by both the service layer and `TenantRoleManager`. Assigning a role to a member is still a **`member:manage`** operation.
- **No privilege escalation** — the manager only offers scopes registered via `o.ConfigureScopes`, and the server rejects any unregistered scope, duplicate names, or names that collide with a code role.
- **Resolved like code roles** — when enabled, a member assigned a custom role receives that role's scopes as claims (server, WASM, and API-key paths), and custom roles appear alongside code roles in the role pickers of `<TeamComponent>` (honouring the visibility provider above) and `<ApiKeyView ShowRoles="true">`, so a custom role can be assigned to a team API key.
- **Cached** — enabling dynamic roles puts a team's custom roles on the claims path, which runs on every authenticating request. They are cached through `ITeamCache`; `SetTeamCustomRolesAsync` drops the entry, so editing through the service layer or `<TenantRoleManager />` takes effect immediately. A host that writes custom roles **straight to its own store**, around `SetTeamCustomRolesAsync`, keeps serving the previous roles. **If you run more than one instance, read "Claims-path caching" below** — the built-in cache is process-local.
- **Off by default** — `EnableDynamicRoles = false` leaves behaviour unchanged (code roles only).

### Verification

Assign a role to a team member, then verify they can access methods protected by the role's scopes.

---

## Step 7a: Claims-path caching (required reading for multi-instance)

The server claims transformation runs on **every authenticating HTTP request** and performs three lookups —
the caller, their membership in the selected team, and that team's custom roles. All three go through
**`ITeamCache`**, and the built-in `InMemoryTeamCache` is registered automatically. On a single instance
there is nothing to configure.

> [!WARNING]
> **`InMemoryTeamCache` is correct for one instance only.** Entries are process-local, so a change made
> through one instance never reaches the others. Until that instance restarts, it keeps issuing the old
> claims — including keeping a **suspended member's scopes** and a **disabled user's session** alive.
> Periodic claim revalidation does not correct it: it recomputes through the same cache, reads the same
> stale entry, and concludes nothing changed.

### Replacing it

Register any implementation backed by a store every instance can see. The toolkit uses `TryAdd`, so yours
wins:

```csharp
builder.Services.AddSingleton<ITeamCache, RedisTeamCache>();   // before AddThargaTeam
builder.AddThargaTeam(o => { ... });
```

Then **forward it from your own service constructors**. Since 3.10.8 this cannot be missed silently: the
toolkit **fails at startup** when a custom cache is registered that your services never received, naming the
types and the fix. It compares the cache each service actually holds against the registered one, so a host
that registered nothing custom never sees it:

```csharp
public class TeamService : TeamServiceRepositoryBase<TeamEntity, TeamMember>
{
    public TeamService(IUserService userService, ITeamRepository<TeamEntity, TeamMember> repository,
        IMongoDbServiceFactory factory, IIconStore iconStore = null, ITeamCache cache = null)
        : base(userService, repository, factory, iconStore, cache) { }
}

public class UserService : UserServiceRepositoryBase<UserEntity>
{
    public UserService(AuthenticationStateProvider auth, IUserRepository<UserEntity> repository,
        ILogger<UserServiceBase> logger = null, IIconStore iconStore = null, ITeamCache cache = null)
        : base(auth, repository, logger, iconStore, cache) { }
}
```

### Implementing one

| Requirement | Why |
|---|---|
| Return `CachedValue<T>.Miss` for "no entry" — **not** a null value | `null` users and memberships are cached deliberately: a non-member is *remembered* as not being one. Collapsing the two sends every non-member request to the store. |
| Serialize your own entity types | `IUser` and `ITeamMember` are interfaces your entities implement. Only you know the concrete types — which is why this is an adapter you own. |
| Prefer a miss over an exception | An uncached read is slow; a throwing one breaks sign-in. Returning `Miss` from every read is valid and simply disables caching. |
| Keep a companion index for the by-user removals | `RemoveUserByKeyAsync` and `RemoveMembersForUserAsync` are not keyed the way their entries are. |

### Not cached

The **team document** is deliberately never cached: it carries the member roster, and the paths that suspend
a member, remove one, assign an owner or transfer ownership read it precisely because they need current state
to decide access. The **consent-teams query** on the non-member consent path is also uncached.

---

## Step 7b: Managing roles & scopes (reference)

A principal's effective scopes are the **union** of four sources:

| Source | Applies to | Configured via |
|--------|-----------|----------------|
| **Access level** → scopes | team members, team API keys | `o.ConfigureScopes` (scope's default min level); `AccessLevel.Custom` grants no base scopes |
| **Tenant roles** → scopes | team members, team API keys | `o.ConfigureTenantRoles` (role → scopes) |
| **Scope overrides** (explicit) | team members, team API keys | per-principal, edited in the UI |
| **System scopes** (global, flat) | **system API keys**, and **users** via role mapping | `o.ConfigureSystemScopes`; `o.ConfigureSystemRoles` (app role → system scopes) |

Service methods gate uniformly with `[RequireScope("…")]` regardless of whether the caller is a team member, a team key, a system key, or a privileged user — but the **claim carries where the grant came from**, and the two are not interchangeable:

| Granted by | Claim type | Authorizes |
|---|---|---|
| Access level, tenant roles, scope overrides — the first three rows above | `TeamClaimTypes.Scope` | the caller's **selected team only** |
| A system API key, or an app role via `ConfigureSystemRoles` | `TeamClaimTypes.SystemScope` | **system-wide**, no team needed |

A scope name may legitimately appear in both. `audit:read` is registered at `AccessLevel.Administrator` *and* commonly mapped to a system role: the team grant opens that team's audit log, the system grant opens the cross-team view. Because the claim types differ, a team administrator can no longer satisfy a system-wide check — which, while both were emitted as `Scope`, meant any team administrator could read every team's audit log.

> **Breaking in 4.0.** Code reading `TeamClaimTypes.Scope` directly to detect a *system* grant must read `TeamClaimTypes.SystemScope` instead. Anything gating through `[RequireScope]`, `TeamScopeGate` or the authorization decorators needs no change.

> **Tip:** drop the `<ScopeView />` component (Tharga.Team.Blazor) on a page to explore the configured **team** scopes interactively. Pick an access level and roles and the scopes a member would have light up while the rest grey out; it defaults to the signed-in member's own access level, roles, and overrides (overrides are highlighted). It builds itself from `IScopeRegistry` / `ITenantRoleRegistry`, so it always matches the running configuration. When the signed-in user holds any **system** scopes, a separate **System scopes** table appears listing them (it's hidden entirely when they hold none; set `ShowSystemScopes="false"` to disable it) — so you can tell at a glance which of your scopes are team vs system.

### Built-in system scopes

The toolkit auto-registers these; grant them through `ConfigureSystemRoles` or a system API key.

> **Registering one of these yourself is safe.** `SystemScopeRegistry.Register` skips a name already
> present, so a host declaring a scope the library also declares is a no-op rather than an error, in either
> order. The **first** registration's description wins, so the catalogue text you see depends on which ran
> first — register nothing you do not need if you care which wording appears.
>
> This changed in **3.14.1**. Before it, `Register` threw on a duplicate, and because 3.14.0 started
> registering `simulation:demo` — a scope hosts had previously been obliged to register themselves — every
> such host failed at startup with `System scope '…' is already registered.`
> ([#237](https://github.com/Tharga/Team/issues/237)). **Team** scopes are unchanged and still throw:
> `ScopeDefinition` also carries an access level and a grant-only flag, which two registrations can
> genuinely disagree about, so there a duplicate is a real conflict rather than a repetition.

| Scope | Authorizes |
|---|---|
| `teams:read` | Enumerating any team, regardless of membership. Discovery only — selecting a team still yields only what it consented to |
| `teams:delete` | Deleting any team, regardless of membership or `AllowTeamCreation`; also restoring a soft-deleted one |
| `teams:purge` | Permanently removing a soft-deleted team and destroying its stored data |
| `teams:set-owner` | Making any existing member the **sole owner** of any team, demoting every other owner. Works whatever the current owner count — none, one, or several |
| `simulation:demo` | Entering **demo mode** — dropping your own system scopes and application roles. The run-as half is the separate *team* scope `simulation:use` |
| `users:manage` | User administration: the admin lists, verify, rename, delete |

`teams:set-owner` has **no in-team fallback**, unlike `teams:delete` which accepts either a system grant or
`team:manage` on the team — and for two reasons rather than one. On an ownerless team no in-team caller can
exist. On a team that has an owner, the in-team caller who should move ownership *is* the owner, and
`TransferOwnershipAsync` is already their path; an in-team fallback would let an Administrator depose the
owner, which `SetMemberRoleAsync` exists to refuse. See
[Choosing who owns a team](user-management.md#choosing-who-owns-a-team).

> **Renamed in 3.14.** This scope was `teams:assign-owner` in 3.9.0–3.13.0, where it authorized only the
> ownerless-repair case. The name changed with the capability rather than being widened in place, so a host
> that granted the old string does not silently acquire the ability to depose owners. **Remap it** — the old
> name now authorizes nothing, and a startup check fails loudly if it is still registered rather than letting
> holders be refused at the point of use with nothing explaining why.

### System scopes & privileged users

System scopes are global capabilities (no access-level hierarchy):

```csharp
o.ConfigureSystemScopes = s =>
{
    s.Register("system:teams:read", "Read any team's data (cross-tenant).");
    s.Register("system:metrics:read", "Read infrastructure metrics.");
};

// Map app/global roles to system scopes so privileged USERS gain them (team-independent).
o.ConfigureSystemRoles = r =>
{
    r.Map("Developer", "system:teams:read", "system:metrics:read", "apikey:manage", "audit:read");
};
```

- **System API keys** are minted with an explicit system-scope list (`SystemApiKeyView` picker reads `ConfigureSystemScopes`).
- **Users** with a mapped app role (e.g. `Developer`) receive the mapped scopes as claims via `TeamServerClaimsTransformation` — even with no team selected. Map `apikey:manage` / `audit:read` to a role to grant that role cross-team key/audit management.
- Map external IdP role claims to internal role names with an `ITeamClaimsEnricher` (runs first), e.g. `Dev → Developer`.

> **`ConfigureSystemScopes` is not an isolation boundary for every scope.** `users:manage` and
> `teams:delete` are **auto-registered** by the framework — the admin surfaces need them grantable — so
> leaving them out of `ConfigureSystemScopes` does **not** prevent a system API key from being granted
> them. Withhold them by not granting them to the key, not by omitting them from the registry. Any
> comment or test asserting the opposite is relying on behaviour that does not hold.

### Consent (cross-team access)

A team can **consent** to grant a global role access to its data, at a chosen access level:

```csharp
o.Blazor.Consent.Roles = ["Developer"];      // which roles a team may consent to
o.Blazor.Consent.ShowToggle = true;          // show the consent picker in TeamComponent
o.Blazor.Consent.AccessLevel = AccessLevel.Viewer; // default level when the consent doesn't carry one
```

The team admin picks the access level when consenting (Viewer/User/Administrator); a consented user gains that team's scopes at that level. The granted level is `team.ConsentAccessLevel ?? Consent.AccessLevel`.

When `ShowToggle` is on, the picker is shown to every member of the team but is **disabled** for anyone below `AccessLevel.Administrator` — so an ordinary member can see what the team has consented to without being able to change it.

### Cross-team visibility for oversight roles

Support and administration roles often need to see the whole estate. The `teams:read` system scope
grants exactly that — **discovery, and nothing else**:

- **Discovery is global.** A caller holding `teams:read` sees every team in `TeamComponent`,
  `TeamSelector` and the developer `UsersView` — both the Teams tab and the per-user team counts and
  membership lists on the Users tab.
- **Access stays per-team and consent-governed.** Selecting a team they are not a member of grants only
  the scopes that team has consented to. A team that consented to nothing yields no access — the team is
  visible, its data is not.

Grant it either explicitly, or with the opt-in convenience flag:

```csharp
o.ConfigureSystemRoles = roles => roles.Map("Developer", SystemTeamScopes.Read);
// or, to reuse the consent role list:
o.Blazor.Consent.Roles = ["Developer"];
o.Blazor.Consent.GrantTeamsRead = true;   // default false
```

`GrantTeamsRead` is off by default on purpose. `Consent.Roles` means "roles a team *may grant access to*"
— a per-team, inbound opt-in. Turning that into a global enumeration privilege automatically would widen
access for existing hosts on upgrade, so it must be opted into. The flag composes with any
`ConfigureSystemRoles` mapping for the same role rather than conflicting with it.

**What a `teams:read` holder sees.** Each team carries a consent badge — *No access* (red), *Partial
access* (yellow, Viewer/User) or *Full access* (green, Administrator) — preceded by a **Not a member**
badge on teams they don't belong to, so the qualifier is read before the level it qualifies. The `TeamSelector` shows the same state as a tinted dot.

**Selecting a team you don't belong to.** An oversight caller can select any team they can see, and that
choice is remembered across visits like any other — returning to the site re-selects it. Selection on its
own carries **no access**: the claims transformation still grants only what that team has consented to, to
a role the caller holds. No consent, or a role the team hasn't consented to, means no team scopes at all.

Acting on such a team throws `UnauthorizedAccessException` with **"Access denied for the selected team
'&lt;key&gt;'."** — distinct from **"No team selected."**, which means no team is chosen at all. The two are
separate claims: `TeamClaimTypes.TeamKey` is the *access anchor*, emitted only once access resolves, while
`TeamClaimTypes.SelectedTeamKey` (`team_id`) records the selection regardless of access. A guard that reads
only the anchor cannot tell the two states apart and will report a selected-but-inaccessible team as
"No team selected."

The distinction that matters is *chosen* versus *defaulted*. A team the caller picked is restored; a team
they never picked is never selected for them. When there is no current or remembered selection, the
fallback always comes from the caller's **own** memberships — so a support user with no memberships and no
prior choice lands on no team, rather than inside whichever tenant happens to sort first. A remembered team
that is no longer visible (deleted, consent revoked, scope removed) falls back the same way.

**With teams visible and none selected, `TeamSelector` shows the picker with a *Select a team* placeholder**
— the estate is offered, not entered. This is the ordinary first visit for a support or operations account:
they can see every team and belong to none, so there is nothing to default to and the choice stays theirs.
Override the placeholder through `TeamSelectorText.SelectTeam` (`team.selector.selectTeam`) like any other
string.

> Before 3.10.10 this state rendered **nothing at all** — an empty top bar with no way to reach a team
> (Tharga/Team#214). If you built a wrapper component to cover it, you can drop it. Note that the two blank
> states meant opposite things and were indistinguishable on screen: a *Create team* link meant `teams:read`
> was **not** reaching the caller, while a completely empty bar meant it **was**. If you spent time
> re-checking a scope mapping that turned out to be correct, this is why.

Team enumeration is deliberately **not audited** — it is a read with no side effect. Mutations performed
inside a team are audited as usual.

### Overriding the "Create team" action

By default the teamless **Create team** link (`TeamSelector`) navigates to `/team` and the **Create new Team** button (`TeamComponent`) calls `CreateTeamAsync()` directly. **Both entry points respect `AllowTeamCreation`** — setting it to `false` hides the link *and* the button, and blocks the programmatic create API at the service layer. (The `TeamSelector` link previously ignored the option and was offered even when creation was disabled, so following it reached an operation the service then refused.) To route team creation through your own onboarding flow instead of disabling it, use one of two override points, evaluated **callback → path → built-in**:

```csharp
// 1. Global, declarative — both entry points navigate to your page (which runs the wizard + CreateTeamAsync):
o.CreateTeamPath = "/get-started";
```

```razor
@* 2. Per component, imperative — handle in place (e.g. a dialog); takes precedence over CreateTeamPath: *@
<TeamSelector CreateTeamRequested="LaunchOnboardingAsync" />
<TeamComponent TMember="MyMember" CreateTeamRequested="LaunchOnboardingAsync" />
```

Both default to unset, so behavior is unchanged unless you opt in. The override applies to the built-in UI entry points only — teams created programmatically or via `AutoCreateFirstTeam` are unaffected.

### Where the profile menu navigates

`LoginDisplay`'s two built-in items — **User** and **Team** — navigate to `/profile` and `/team`. The toolkit
ships `<UserProfileView />` and `<TeamComponent />` as components and lets you mount them at any route, so
if you mounted either anywhere else, say where it went:

```csharp
o.ProfilePath = "/account";        // default: null — keeps /profile
o.TeamPath    = "/organisation";   // default: null — keeps /team
```

Both default to unset, so an existing host needs neither. They are independent: the two pages are mounted
separately, so moving one says nothing about the other.

**Menu items you supply yourself were never affected** — they carry their own `Href` and are matched before
the built-ins. This is only about the two the toolkit renders.

> This matters beyond a stray link if you use access simulation. With `ShowBanner="false"` on
> `AccessSimulationBar`, the access card on the profile page is the only way out of a reduced session — so
> the profile route has to actually resolve.

### Component parameter reference

| Component | Parameters |
|-----------|-----------|
| `<TeamSelector>` | `CreateTeamRequested` (intercept the teamless "Create team" link) |
| `<UserProfileView>` | `ShowAccessCard` (default true — renders `<AccessSimulationCard />` between the profile details and Claims) |
| `<AccessSimulationBar>` | `Text` (overrides the resolved `team.simulation.bar.viewAs`, "View as…"), `ShowEntryPoint` (true), `ShowBanner` (true — **off means the profile card is the only way out**) |
| `<TeamComponent>` | `ShowScopeTooltip` (default true), `ShowScopeOverrides`, `ShowRoles`, `CreateTeamRequested` (intercept the "Create new Team" button) |
| `<ApiKeyView>` | `ShowScopeTooltip` (true), `ShowScopeOverrides`, `ShowRoles`, `ShowLastUsed` (true), `ShowExpiryDatePicker`, `ShowTags` (`bool?`, null=auto), `ChipTagKeys`, `ShowAuditLogButton` |
| `<SystemApiKeyView>` | `ShowScopeTooltip` (true), `ShowScopeOverrides` (true), `ShowLastUsed` (true), `ShowExpiryDatePicker`, `ShowAuditLogButton` |

Access to manage keys is gated on `apikey:manage`; the audit log on `audit:read`. (The former per-component
`CrossTeamRoles` / `RequiredScopes` parameters were removed — grant cross-team access via the role→system-scope
mapping instead.)

### Disabling a key, and why refresh does not undo it

`SetKeyDisabledAsync` stops a key authenticating while keeping its name, scopes, roles, tags and audit
trail — the reversible alternative to deleting it. The refusal is **recorded as an authentication
failure**, because a disabled key still gets used: by a scheduled job nobody remembers, or by whoever
the disabling was aimed at, and those attempts are the point of the trail.

> **Refreshing a disabled key leaves it disabled.** A refresh mints a new secret; it is not a decision to
> trust the key again — and the usual reason to refresh is the same suspected leak that prompted the
> disable. Re-enabling is always explicit.

Disabled is shown as a **badge**, distinct from expiry's red text. Two red things in one grid is how an
operator concludes a contained key merely lapsed.

---

## Step 8: Audit Logging

Adds audit logging for service calls, authorization events, and data changes. Logs can be stored in the application logger, MongoDB, or both.

**Requires:** Step 4

### Program.cs

```csharp
builder.Services.AddThargaAuditLogging();
```

> **⚠️ Most common gotcha:** `StorageMode` defaults to **`Logger` only**. The `AuditLogView` component
> reads from **MongoDB**, so with the default it stays **empty** — entries only go to `ILogger`. To
> populate the UI you must opt into Mongo storage:
> ```csharp
> builder.Services.AddThargaAuditLogging(o => o.StorageMode = AuditStorageMode.MongoDB);
> // or keep both: AuditStorageMode.Logger | AuditStorageMode.MongoDB
> ```
> `AuditStorageMode` is a `[Flags]` enum, so the values combine. MongoDB storage requires MongoDB
> configured (Step 4).

### Options

```csharp
builder.Services.AddThargaAuditLogging(o =>
{
    o.StorageMode = AuditStorageMode.Logger | AuditStorageMode.MongoDB; // default: Logger only — see gotcha above
    o.CallerFilter = AuditCallerFilter.Api | AuditCallerFilter.Web;  // default: Api | Web ([Flags])
    o.EventFilter = AuditEventFilter.All;              // default: All ([Flags])
    o.ExcludedActions = new[] { "read", "list" };      // default: empty — skip noisy read operations
    o.ExcludedEndpoints = Array.Empty<string>();       // default: empty
    o.RetentionDays = 90;                              // default: 90 — null (or <= 0) = keep forever (no TTL)
    o.BatchSize = 100;                                 // default: 100 — MongoDB background-writer batch size
    o.FlushIntervalSeconds = 5;                        // default: 5 — MongoDB background-writer flush interval
});
```

| Option | Default | Notes |
|---|---|---|
| `StorageMode` | `Logger` | `[Flags]`: `Logger`, `MongoDB`, or both. **Set `MongoDB` to populate `AuditLogView`.** |
| `CallerFilter` | `Api \| Web` | `[Flags]` — which caller sources to record. |
| `EventFilter` | `All` | `[Flags]` — which event types to record. |
| `ExcludedActions` | empty | Action names to skip (e.g. `"read"`, `"list"`). |
| `ExcludedEndpoints` | empty | Endpoints to skip (e.g. `"/health"`). |
| `RetentionDays` | `90` | `int?` mapped to a MongoDB TTL index (`Timestamp_TTL`). **`null` or `<= 0` = keep forever** (no TTL index). |
| `BatchSize` | `100` | Background MongoDB writer batch size. |
| `FlushIntervalSeconds` | `5` | Background MongoDB writer flush interval. |

> **Retention / TTL.** `RetentionDays` creates a MongoDB TTL index that auto-deletes entries older than
> the given age. Set it to **`null`** (or any value `<= 0`) to keep entries **indefinitely** — no TTL
> index is created. Caveat: MongoDB does not drop an existing TTL index automatically, so changing the
> retention on a collection that already has a `Timestamp_TTL` index (including switching to "forever")
> may require dropping that index manually.

### What gets audited

Mutations flow through auditing decorators: **team-service** operations (create/rename/delete team,
invite/remove member, set consent, …), **API-key management** (create/recycle/lock/delete, for team
and system keys), and **user administration** (directory verify, bulk verify, user delete — see
[User management & directory](user-management.md#audit)). Read operations pass through unaudited; use
`ExcludedActions` to drop any others you consider noise.

### What becomes available

| Component | Description |
|-----------|-------------|
| `<AuditLogView />` | Audit log viewer with charts and filtering |
| `CompositeAuditLogger` | Write your own audit entries. Applies the caller/event filters and fans out to every configured backend. **Inject this, not `IAuditLogger`** — that resolves to a single backend and bypasses the filters. |

### Scoping `<AuditLogView />`

| Parameter | Effect |
|---|---|
| `TeamKey` | Scopes the view to one team **and authorizes against it** — a caller holding that team's `audit:read` is admitted. Hides the Team filter, since there is nothing to choose. |
| `PinnedFilter` | Forces one or more dimensions (team, caller, feature, action, key) and hides their controls. Outranks `TeamKey` when both name a team. |
| `RestrictCallerType` | Limits entries to one caller type (user or API key). |

Naming **no** team makes the read system-wide, which requires a system grant (`audit:read` via
`o.ConfigureSystemRoles`, or a system API key) rather than membership of any one team.

```razor
@* One team, authorized by that team's audit:read *@
<AuditLogView TeamKey="@teamKey" />

@* Every team the caller can reach; needs a system grant *@
<AuditLogView />
```

> Before 3.10, `TeamKey` scoped the query but **not** the access decision, so passing it alone was refused
> even for a team Owner holding `audit:read`, and hosts worked around it by also passing a `PinnedFilter`
> naming the same team. That workaround is no longer needed (Tharga/Team#175).

### Audit entry fields

Each audit entry captures: timestamp, correlation ID, event type, feature/action, caller identity, team key, access level, scope check results, duration, and a `Metadata` dictionary.

### Who an entry is attributed to

Three fields describe the actor, and they answer different questions:

| Field | Holds | Match |
|---|---|---|
| `CallerIdentity` | A display string, resolved `name` → `preferred_username` → subject → `name` | Substring — its content depends on which claims your IdP emits |
| `CallerUserIdentity` | The acting user's authentication subject, or null | **Exact** — the subject or nothing, never a fallback |
| `CallerKeyId` | The API key's id, or null | Exact |

Use `CallerUserIdentity` to correlate rows to one person; `CallerIdentity` is for reading.

`CallerType` and `CallerSource` say what kind of actor it was:

| Situation | `CallerType` | `CallerSource` |
|---|---|---|
| API-key request | `ApiKey` | `Api` |
| Cookie / federated sign-in | `User` | `Web` |
| Authenticated, unrecognised scheme | `User` | `Unknown` |
| Anonymous request | `Unknown` | `Unknown` |
| Declared background actor | `System` | `Background` |
| No principal, no declared actor | `Unknown` | `Unknown` |

> **Changed:** a caller with no `HttpContext` used to be recorded as `User` with a null identity — a row
> claiming a person did what a background job did. It now records `Unknown`, or the declared actor below.
> If you report on `CallerType == User`, those rows have moved.

### Auditing background work

Code outside a request — a hosted service, a message handler, a scheduled job — has no principal to
attribute. Declare one for the duration of the work:

```csharp
public class ClaimedJobWorker(
    IAuditContextAccessor auditContext,
    IAuditEntryFactory auditEntryFactory,
    IAuditLogger auditLogger)
{
    public async Task RunAsync(Job job, CancellationToken cancellationToken)
    {
        using var _ = auditContext.Push(new AuditActor(
            Identity: "fortdocs-worker",
            CorrelationId: job.Id));       // groups every entry this job writes

        auditLogger.Log(auditEntryFactory.Create("job", "claim", teamKey: job.TeamKey));
    }
}
```

**Build entries with `IAuditEntryFactory`, not by hand.** `IAuditLogger.Log` takes a pre-built entry and
does not consult the ambient actor, so an `AuditEntry` you construct yourself will not carry the actor
however carefully you scoped it. The factory resolves the caller the same way the built-in decorators do
— HTTP principal if there is one, declared actor if not.

Supply `teamKey` explicitly for background work: there is no selected team to infer it from.

Entries written inside the scope carry that identity, `AuditCallerType.System` and
`AuditCallerSource.Background`. Three things worth knowing:

- **The scope is `AsyncLocal`**, so it survives `await` and flows into nested calls without being passed
  along. Nested scopes restore the outer actor on dispose rather than clearing it.
- **An authenticated caller always wins.** A scope left open on a pooled thread cannot relabel a real
  user's action as the system's. An *anonymous* request does not win — a job triggered through an
  unauthenticated endpoint still knows what it is.
- **Set `CorrelationId` per unit of work.** Without it every entry gets its own generated id, and the
  grouping cannot be reconstructed afterwards.

`IAuditContextAccessor` is registered by `AddThargaAuditLogging()`, regardless of storage mode.

### Reading failures in the log view

The **OK** column of `<AuditLogView />` shows a green check for a successful entry. For a failed entry it
shows a red icon **plus a short failure code** — the audit equivalent of a response code, taken from the
entry's `EventType` (`ScopeDenial`, `AccessLevelDenial`, `AuthFailure`, `RateLimit`), or `Error` for an
entry that failed by throwing. Hovering the status shows a detail tooltip with the code, the scope that was
checked and its result (when the failure was an authorization denial), and the `ErrorMessage` reason. The
reason is also present in the CSV and JSON exports (the `ErrorMessage` column/field).

### Operation metadata (what changed)

Built-in operations record *what* changed in `AuditEntry.Metadata`, not just that they happened — so the
log answers "renamed to what, from what?" without a second lookup. Keys are defined on `AuditMetadataKeys`;
before/after pairs are captured where the previous value is needed to read the entry:

| Operation | Metadata keys |
|-----------|---------------|
| Create team | `team.name` |
| Rename team | `team.name.old`, `team.name.new` |
| Delete team | `team.name` (unrecoverable afterward, so captured) |
| Invite member | `member.email` |
| Remove member | `member.key` |
| Change role | `member.key`, `member.accesslevel.old`, `member.accesslevel.new` |
| Set tenant roles / scope overrides | `member.key`, `member.tenantroles` / `member.scopeoverrides` |
| Set display name | `member.key`, `member.name.old`, `member.name.new` (`""` = cleared override) |
| Set consent | `consent.accesslevel.old`, `consent.accesslevel.new` (`"none"` = cleared), `consent.roles` |
| Set custom roles | `customroles.names` |
| Transfer ownership | `team.newowner.key` |

Capturing a "before" value is best-effort: if the read fails it is omitted rather than recorded as a
misleading blank, and it never fails the operation. Metadata shows as an expandable detail row in
`<AuditLogView />`, in the CSV export (a JSON-encoded `Metadata` column) and JSON export, and — since the
3.2.x line — in `LoggerAuditLogger` output.

> One gap: a caller changing consent on a team they are **not** a member of doesn't get the
> `consent.accesslevel.old` value (the "before" read is scoped to the caller's own teams). The new value
> and roles are still recorded.

### Adding your own metadata

Register an `IAuditEnricher` to attach host-defined metadata (a request id, a ticket number, anything from
`IHttpContextAccessor`) to every entry the toolkit writes:

```csharp
public sealed class RequestIdAuditEnricher(IHttpContextAccessor http) : IAuditEnricher
{
    public void Enrich(AuditEntry entry, IDictionary<string, string> metadata)
    {
        var requestId = http.HttpContext?.TraceIdentifier;
        if (requestId != null) metadata["request.id"] = requestId;
    }
}

builder.Services.AddThargaAuditEnricher<RequestIdAuditEnricher>();
```

Enrichers run for every entry that passes the audit filters, in registration order. The merge is
**add-only**: an enricher augments the record but cannot overwrite a key the toolkit set (nor one an
earlier enricher set), so the authoritative "who did what" can't be forged. An enricher is resolved as a
**singleton** — read per-request state through `IHttpContextAccessor`, not a scoped dependency — and one
that throws is logged and skipped, so enrichment can never fail the operation being audited.

### Event types

| Type | When logged |
|------|------------|
| `ServiceCall` | Any proxied service method call |
| `AuthSuccess` | Successful authentication |
| `AuthFailure` | Failed authentication |
| `ScopeDenial` | Scope check denied |
| `DataChange` | Data modification |
| `RateLimit` | Rate limit hit |

### Verification

Perform some actions, then view the audit log via `<AuditLogView />`. Entries should appear with correct caller identity and scope information.

---

## Quick reference: Registration order in Program.cs

```csharp
using Microsoft.AspNetCore.Authentication;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Authentication;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;

// Step 1: Radzen + Blazor foundation
builder.Services.AddRadzenComponents();
builder.Services.AddThargaBlazor(o => o.Title = "My App");

// Step 2: Authentication
builder.AddThargaAuth();

// Step 3: Controllers
builder.Services.AddThargaControllers();

// Step 4: Team management
builder.Services.AddThargaTeamBlazor(o =>
{
    o.Title = "My App";
    o.RegisterTeamService<MyTeamService, MyUserService>();
    o.RegisterApiKeyAdministrationService<MyApiKeyService>();  // Step 5
    o.ShowMemberRoles = true;                                   // Step 7
    o.ShowScopeOverrides = true;                                // Step 6
});
builder.Services.AddThargaTeamRepository(o =>
{
    o.RegisterUserRepository<UserEntity>();
    o.RegisterTeamRepository<TeamEntity, TeamMember>();
});

// Step 5: API key auth
builder.Services.AddThargaApiKeys();
builder.Services.AddAuthentication()
    .AddThargaApiKeyAuthentication();

// Step 6: Scopes
builder.Services.AddThargaScopes(scopes =>
{
    scopes.Register("feature:read", AccessLevel.Viewer);
    scopes.Register("feature:write", AccessLevel.User);
});
builder.Services.AddScopedWithScopes<IMyService, MyService>();

// Step 7: Tenant roles
builder.Services.AddThargaTenantRoles(roles =>
{
    roles.Register("Editor", new[] { "feature:read", "feature:write" });
});

// Step 8: Audit
builder.Services.AddThargaAuditLogging();

var app = builder.Build();

// Step 2: Auth endpoints
app.UseThargaAuth();

// Step 3: Controllers
app.UseThargaControllers();
```

---

## Quick reference: _Imports.razor

```razor
@* Step 1: UI Foundation *@
@using Radzen
@using Radzen.Blazor
@using Tharga.Blazor
@using Tharga.Blazor.Framework
@using Tharga.Blazor.Framework.Buttons
@using Tharga.Blazor.Features.BreadCrumbs

@* Step 2: Authentication *@
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@using Tharga.Team.Blazor.Features.Authentication
@using Tharga.Team.Blazor.Framework

@* Step 4: Team management *@
@using Tharga.Team.Blazor.Features.Team
```

---

## Package summary

| Package | Added in | Purpose |
|---------|----------|---------|
| `Tharga.Blazor` | Step 1 | Generic UI components (Radzen, buttons, breadcrumbs) |
| `Tharga.Team.Blazor` | Step 2 | Authentication, team UI, claims augmentation |
| `Tharga.Team.Service` | Step 3 | API controllers, API key auth, scopes, audit |
| `Tharga.Team.MongoDB` | Step 4 | MongoDB persistence for teams and users |
| `Tharga.Team.Entra` | (optional) | Microsoft Entra ID user directory — verify / list / delete via Graph ([guide](user-management.md)) |
| `Tharga.Team.Images` | (optional) | Auto-square and downscale uploaded team/user icons via SkiaSharp ([guide](icons.md)) |
| `Tharga.Team` | (transitive) | Domain models, authorization primitives |
