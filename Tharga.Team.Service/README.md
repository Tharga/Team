# Tharga Team Service
[![NuGet](https://img.shields.io/nuget/v/Tharga.Team.Service)](https://www.nuget.org/packages/Tharga.Team.Service)
![Nuget](https://img.shields.io/nuget/dt/Tharga.Team.Service)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Server-side API-key authentication, authorization enforcement, controller registration, OpenAPI/Swagger setup, and audit logging for ASP.NET Core projects. Targets .NET 9.0 and .NET 10.0.

## Features

- **API key authentication** - Reads the `X-API-KEY` header, validates against a store, and populates `TeamKey`, `AccessLevel`, and scope claims.
- **Access level authorization** - `AccessLevelProxy<T>` enforces `[RequireAccessLevel]` on service methods via `DispatchProxy`.
- **Scope authorization** - `ScopeProxy<T>` enforces `[RequireScope]` with audit logging.
- **Works in API and interactive Blazor** - the proxies resolve the caller via `ITeamPrincipalAccessor` (default: `IHttpContextAccessor`). `AddThargaTeamBlazor` swaps in a circuit-aware accessor (HttpContext when present, else `AuthenticationStateProvider`), so one `[RequireScope]`/`[RequireAccessLevel]` enforces both surfaces. Register a custom `ITeamPrincipalAccessor` to plug in another principal source.
- **Controller + Swagger registration** - Single-call setup for MVC controllers, OpenAPI document with API key security scheme, and Swagger UI.
- **API key management** - Default MongoDB-backed `ApiKeyAdministrationService` with key hashing. Configurable via `ApiKeyOptions` — see [API key options](#api-key-options).
- **Audit logging** - `CompositeAuditLogger` with `ILogger` and MongoDB backends. ⚠️ Stores to `ILogger` only by default — see [Audit logging](#audit-logging).
- **API-key lifecycle hook** - Capture the private token on create/recycle (plus a delete signal) via `IApiKeyLifecycleHandler` — see [Capturing the private token](#capturing-the-private-token).
- **Pluggable** - Implement `IApiKeyAdministrationService` (from Tharga.Team) to bring your own storage backend.

## Quick start

```csharp
using Tharga.Team;
using Tharga.Team.Service;

// Program.cs
builder.Services.AddThargaControllers();
builder.Services.AddAuthentication()
    .AddThargaApiKeyAuthentication();
builder.Services.AddThargaApiKeys();

var app = builder.Build();
app.UseThargaControllers();
app.UseAuthentication();
app.UseAuthorization();
app.Run();
```

## Reading the audit log over REST

`AddThargaControllers` registers one controller of its own — `GET /api/audit` — so audit data is reachable
from a script or an agent, not only from the Blazor view.

```
GET /api/audit?teamKey=ABC123&from=2026-01-01&take=100
X-API-KEY: <key>
```

Filters: `teamKey`, `from`, `to`, `feature`, `action`, `success`, `skip`, `take` (capped at 500).
**Omitting `teamKey` reads across all teams** and requires a *system* `audit:read` grant.

Authorization is the same `AuditAccess.CanRead` rule the Blazor `AuditLogView` uses, so the two surfaces
cannot drift. A team grant reaches only its own team; `audit:read` is registered at
`AccessLevel.Administrator`, so Viewer- and User-level callers are refused even for their own team.
Denials are `403` rather than `404`, so they do not reveal whether a team exists.

## Which credentials reach the API

`ThargaControllerOptions.AuthenticationSchemes` lists the schemes Tharga's controllers accept, defaulting
to the API-key scheme. Add your own to also admit a signed-in user:

```csharp
using Microsoft.AspNetCore.Authentication.Cookies;

builder.Services.AddThargaControllers(o =>
    o.AuthenticationSchemes.Add(CookieAuthenticationDefaults.AuthenticationScheme));
```

A policy naming no scheme falls back to the application's **default** scheme — OIDC in a Blazor host — so
an unauthenticated API call gets a 302 to a login page rather than a 401, and an agent following it
receives HTML with a 200. Naming schemes explicitly is what avoids that.

## Customizing the OpenAPI document

`AddThargaControllers` owns the OpenAPI document (it registers the API-key security scheme on it). To add your own `IOpenApiDocumentTransformer` / `IOpenApiOperationTransformer` — for example, to filter the generated spec down to the operations the current caller is authorized for — use the `ConfigureOpenApi` hook instead of calling `AddOpenApi("v1", …)` yourself:

```csharp
builder.Services.AddThargaControllers(o =>
    o.ConfigureOpenApi(api => api.AddDocumentTransformer<ScopeFilteringDocumentTransformer>()));
```

The callback receives the same `OpenApiOptions` Tharga configures, so your transformers run against the document Tharga already manages. Multiple `ConfigureOpenApi` calls compose (each runs, in registration order). This avoids a second `AddOpenApi("v1", …)` registration — which would leave it ambiguous whether your document composes with or overrides Tharga's, and, in .NET 10, forces the OpenAPI XML-comment source generator to emit an interceptor into your project (requiring `<InterceptorsNamespaces>$(InterceptorsNamespaces);Microsoft.AspNetCore.OpenApi.Generated</InterceptorsNamespaces>` just to compile).

> **.NET 10+ only.** On .NET 9 the document is built by Swashbuckle and this hook is not present; use Swashbuckle's `IDocumentFilter` / `IOperationFilter` there.

## System API keys

For infrastructure-level credentials that aren't tied to a team (MCP gatekeepers, CI/CD callers, cross-team admin tooling), use **system keys** — API keys with no `TeamKey`.

Create and manage them via the `<SystemApiKeyView />` component in `Tharga.Team.Blazor` (gated by the `Developer` role), or programmatically via `IApiKeyAdministrationService.CreateSystemKeyAsync(name, scopes, expiryDate, createdBy)`.

System keys authenticate through the same `X-API-KEY` header. The principal they produce carries the `IsSystemKey=true` claim and the explicit scopes granted at creation time — no `TeamKey` claim.

Protect system-only endpoints with the system policy:

```csharp
app.UseThargaMcp().RequireAuthorization(ApiKeyConstants.SystemPolicyName);
```

The two policies are mutually exclusive: `ApiKeyPolicy` rejects system keys, `SystemApiKeyPolicy` rejects team keys.

## What a key can reach

The boundary between the two kinds of key is a security guarantee, not a convention, and it is worth
knowing precisely before handing either one out:

| | Team key | System key |
|---|---|---|
| Claims issued | `TeamKey` + scopes as **team** grants | `IsSystemKey` + scopes as **system** grants |
| Its own team | ✅ subject to access level | n/a — a system key has no team |
| Another team | ❌ **always**, even when naming that team explicitly | consent-dependent |
| System-wide | ❌ **always** | ✅ for the scopes granted at creation |

Two properties follow, and both are covered by tests:

- **The team a key acts for comes from the key record, never the request.** Knowing a team's key is not
  authority over it — naming another team is futile rather than merely refused.
- **A team grant never satisfies a system check.** The scope claim carries its provenance, so a team key
  holding `audit:read` cannot use it to read across teams. This is why the two claim types exist.

Access level still applies within the team. `audit:read` is registered at `AccessLevel.Administrator`, so a
Viewer-level key holding a team's credential is refused its own team's audit log — holding a team's key is
not the same as holding every grant inside it.

## Which team service to inject

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
| `ITeamManagementService` | One team: its details, roster, members, custom roles, and every mutation | `team:read` on reads, `team:manage` / `member:manage` on mutations |
| `ITeamDirectoryService` | The caller's **own** teams, with or without rosters | Recomputed per team from that membership — teams not granting `team:read` are omitted |
| `ITeamOversightService` | **Every** team, regardless of membership | `teams:read` system scope. Discovery only |
| `ITeamInvitationService` | Resolving an invite code | **The code itself.** An invitee holds no scope for the team they are joining |
| `ITeamLifecycleService` | Creating a team | Authenticated caller plus `AllowTeamCreation` |

### `ITeamService` is the contract you implement, not the one you inject

It is the host's own storage seam and is **deliberately unchecked** — framework code reads through it
while constructing the very claims that would authorize the read, so gating it would be circular and
break sign-in.

> A first-level surface injecting it bypasses authorization entirely. That is not hypothetical: it is
> how `team:read` came to be registered, documented, granted — and checked by nothing.

It is marked `[EditorBrowsable(Never)]`, so it no longer appears in IntelliSense. `InternalServiceInjectionTests`
(in `Tharga.Team.Blazor.Tests` for components and `Tharga.Team.Mcp.Tests` for providers) fails the build if
anything in this repo injects it — by constructor parameter or `[Inject]` property. Internal services are
discovered by the attribute, not a list, so a newly-marked contract is covered automatically.

Nothing protects a consumer project, which is what the planned Roslyn analyzer is for. Until then, the cheap
substitute is the same test over your own assembly: reflect over your `IComponent` types and assert none
depends on a type marked `EditorBrowsableState.Never`.

**Three categories, not two**, and only the first is marked by an attribute:

| Category | Marked by | Rule |
|---|---|---|
| **Gated** | `[RequireScope]` on every method | All-or-nothing. The interface is wholly team-bound or wholly system-wide, so one registration is true of every method |
| **Filtered** | nothing — stated in XML docs | A first-level read naming no team, so it cannot be gated. Recomputes the caller's scopes per item and omits what they may not see |
| **Internal** | `[EditorBrowsable(Never)]` + XML docs | The contract a host implements. Never inject from a component, controller or MCP provider |

**An entry point's check need not be a scope.** An invitation is authorized by its invite code, because
the invitee is not yet a member and holds nothing. The rule is that a first-level call is *checked*, not
that it is checked by a scope.

## Which policy to gate an endpoint with

Three are registered. **The first two are disjoint, not a hierarchy** — `SystemApiKeyPolicy` is not
"`ApiKeyPolicy` plus more", and the naming invites that reading.

| Policy | Team key | System key |
|---|---|---|
| `ApiKeyConstants.PolicyName` (`ApiKeyPolicy`) | ✅ | ❌ |
| `ApiKeyConstants.SystemPolicyName` (`SystemApiKeyPolicy`) | ❌ | ✅ |
| `ApiKeyConstants.AnyKeyPolicyName` (`AnyApiKeyPolicy`) | ✅ | ✅ |

```csharp
app.MapGet("/reports", …).RequireAuthorization(ApiKeyConstants.AnyKeyPolicyName);
```

> [!WARNING]
> **Requiring both admits nothing.** ASP.NET Core *combines* policies when several are named, so
> `RequireAuthorization(PolicyName, SystemPolicyName)` demands a key that is simultaneously a team key
> and a system key — which no key is. Use `AnyKeyPolicyName` for an endpoint both kinds should reach.
> This is asserted by a test rather than left as advice.

**MCP endpoints need none of these.** `UseThargaMcp()` builds its own policy from
`ThargaMcpOptions.AuthenticationSchemes`, asserting nothing about `IsSystemKey`, so it already admits
both kinds — provided a bridge has contributed a scheme, which `mcp.AddTeam()` does. Naming a policy
there would narrow the endpoint rather than secure it.

## Team API keys

Protect endpoints with the built-in policy:

```csharp
[Authorize(Policy = ApiKeyConstants.PolicyName)]
[ApiController]
[Route("api/[controller]")]
public class MyController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var teamKey = User.FindFirst(TeamClaimTypes.TeamKey)?.Value;
        return Ok(new { teamKey });
    }
}
```

Enforce access levels on services:

```csharp
public interface IMyService
{
    [RequireAccessLevel(AccessLevel.Viewer)]
    IAsyncEnumerable<Item> GetAsync();

    [RequireAccessLevel(AccessLevel.User)]
    Task<Item> AddAsync(string name);
}

// Program.cs
builder.Services.AddScopedWithAccessLevel<IMyService, MyService>();
```

## API key options

API key behaviour is configured via `ApiKeyOptions` (passed to `AddThargaApiKeyAuthentication`, or `o.ApiKey` under `AddThargaTeam`):

| Option | Default | Purpose |
|---|---|---|
| `AdvancedMode` | `false` | When false, keys are auto-created per team and only refresh/lock are exposed. When true, full CRUD (name, access level, roles, scope overrides, expiry). |
| `AutoKeyCount` | `2` | Number of keys auto-created per team in simple mode. |
| `AutoLockKeys` | `false` | Lock keys immediately after creation so the raw value is shown only once. |
| `MaxExpiryDays` | `365` | Caps expiry for team and system keys. `null` = no cap. |
| `LastUsedThrottle` | `1 min` | Minimum interval between `LastUsedAt` writes for a key (avoids a DB write per request). `TimeSpan.Zero` = stamp every request. |
| `MinKeyLength` / `MaxKeyLength` | `24` / `32` | Random alphanumeric length of the key secret (base62, ≈5.95 bits/char). The length is chosen at random in `[Min, Max]` per key. ~190-bit at the default 32; 43 ≈ 256-bit. Floor 24 (≈143-bit). |

## Audit logging

`AddThargaAuditLogging` records mutations (team-service operations **and** API-key management) and authorization events via `CompositeAuditLogger`.

```csharp
builder.Services.AddThargaAuditLogging(o =>
{
    o.StorageMode = AuditStorageMode.MongoDB;   // see gotcha below
    o.RetentionDays = 90;                       // null (or <= 0) = keep forever
});
```

> **⚠️ Gotcha:** `StorageMode` defaults to **`Logger` only**, so the MongoDB-backed `AuditLogView` stays **empty** until you set `AuditStorageMode.MongoDB` (or `Logger | MongoDB`). `AuditStorageMode` is a `[Flags]` enum.

| Option | Default | Notes |
|---|---|---|
| `StorageMode` | `Logger` | `[Flags]`: `Logger`, `MongoDB`, or both. Set `MongoDB` to populate `AuditLogView`. |
| `CallerFilter` / `EventFilter` | `Api\|Web` / `All` | `[Flags]` — which caller sources / event types to record. |
| `ExcludedActions` / `ExcludedEndpoints` | empty | Skip noisy actions (e.g. `"read"`) or endpoints. |
| `RetentionDays` | `90` | `int?` → MongoDB TTL index (`Timestamp_TTL`). **`null` or `<= 0` = keep forever** (no TTL index). Changing/removing the TTL on an existing collection may need a manual index drop. |
| `BatchSize` / `FlushIntervalSeconds` | `100` / `5` | Background MongoDB writer tuning. |

### Auditing background work

Code with no HTTP request behind it — a hosted service, a message handler, a scheduled job — has no
principal to attribute. It used to be recorded as `CallerType.User` with a null identity, i.e. a row
claiming a person did it. It now records `Unknown` unless you declare an actor:

```csharp
using var _ = auditContext.Push(new AuditActor("nightly-retention", CorrelationId: runId));
auditLogger.Log(auditEntryFactory.Create("retention", "sweep", teamKey: teamKey));
// CallerType.System, CallerSource.Background, that identity and correlation id
```

Build the entry with **`IAuditEntryFactory`**: `IAuditLogger.Log` takes a pre-built entry and does not
consult the ambient actor, so one you construct by hand will not carry it. `Tharga.Team.Sample` has a
working example in `SampleBackgroundJob`.

`IAuditContextAccessor` is registered by `AddThargaAuditLogging()` regardless of storage mode. The scope
is `AsyncLocal`, so it survives `await` and nested calls, and restores the outer actor on dispose. An
**authenticated** caller always wins over an ambient actor, so a scope left open on a pooled thread cannot
relabel a real user's action; an *anonymous* request does not win.

> **`CallerFilter` and background entries.** A source that is neither `Api` nor `Web` is matched against
> `Api | Web`, so background entries are recorded under the default filter — and, less obviously, under a
> filter narrowed to just one of them. There is no `Background` flag to include or exclude them
> independently. If you need that distinction, say so and it can be added; the current behaviour errs
> toward recording.

### Operation metadata

Audited operations record **what changed** on `AuditEntry.Metadata` — create captures the team name,
rename the old and new name, a role change the old and new access level, consent the old and new level and
roles, and so on (keys are defined on `AuditMetadataKeys`). Capturing a "before" value is best-effort and
never fails the operation. Metadata is shown as an expandable row in `AuditLogView`, in CSV export (a
JSON-encoded `Metadata` column), JSON export, and the `Logger` output.

### Adding your own metadata

Register an `IAuditEnricher` to attach host-defined metadata to every entry the toolkit writes:

```csharp
public sealed class RequestIdAuditEnricher(IHttpContextAccessor http) : IAuditEnricher
{
    public void Enrich(AuditEntry entry, IDictionary<string, string> metadata)
    {
        if (http.HttpContext?.TraceIdentifier is { } id) metadata["request.id"] = id;
    }
}

builder.Services.AddThargaAuditEnricher<RequestIdAuditEnricher>();
```

Enrichers run in registration order for every entry that passes the filters. The merge is **add-only** —
an enricher cannot overwrite a key the toolkit (or an earlier enricher) set — is resolved as a
**singleton** (read request state via `IHttpContextAccessor`), and one that throws is logged and skipped so
enrichment can never fail the audited operation.

See the [implementation guide](https://team.tharga.net) for the full reference.

## Capturing the private token

The private token is shown once and never persisted, logged, or exposed over an API. To capture it (e.g. to re-deliver a minted key), register an `IApiKeyLifecycleHandler` — it receives the token on **create** and **recycle/regenerate**, plus a tokenless **delete** signal:

```csharp
public class MyHandler(ISecretProtector protector, IMyStore store) : IApiKeyLifecycleHandler
{
    public Task OnApiKeyLifecycleAsync(ApiKeyLifecycleContext ctx) => ctx.Reason switch
    {
        ApiKeyLifecycleReason.Deleted => store.RemoveAsync(ctx.ApiKeyId),
        _ => store.SaveAsync(ctx.ApiKeyId, protector.Protect(ctx.PrivateToken), ctx.TeamKey, ctx.Tags),
    };
}

builder.AddThargaTeam(o => o.AddApiKeyLifecycleHandler<MyHandler>());
```

A throwing handler propagates out of the originating operation (capture failures are not swallowed). You own whatever you capture — encrypt it at rest.

## Mail outside production

Every mail the toolkit sends goes through `IOutboundMailPolicy`. In production it is sent unchanged. Outside
production (`IHostEnvironment.IsProduction()` is false) it reaches only recipients in an allowed domain;
anyone else is redirected to the override address, with the intended recipient in the subject, or — with no
override address — not sent at all.

```json
"Email": { "Override": { "Address": "test-inbox@example.com", "AllowedDomains": [ "example.com" ] } }
```

**Upgrading changes what a test environment sends.** Set `Email:Override` first if it should keep receiving
mail. A custom `ITeamEmailSender` can inject `IOutboundMailPolicy` to apply the same rules; see *Mail outside
production* in the implementation guide.

## Dependencies

- [Tharga.Team](https://www.nuget.org/packages/Tharga.Team) - Domain models, authorization primitives, and service abstractions.
- [Tharga.MongoDB](https://www.nuget.org/packages/Tharga.MongoDB) - MongoDB repository infrastructure.
- [Tharga.Toolkit](https://www.nuget.org/packages/Tharga.Toolkit) - Shared utilities including API key hashing.
- [Swashbuckle.AspNetCore](https://www.nuget.org/packages/Swashbuckle.AspNetCore) - Swagger UI generation.

## Related packages

| Package | Description |
|---------|-------------|
| [Tharga.Team](https://www.nuget.org/packages/Tharga.Team) | Domain models and authorization primitives (plain .NET, WASM-safe) |
| [Tharga.Team.Blazor](https://www.nuget.org/packages/Tharga.Team.Blazor) | Team-specific Blazor UI components |
| [Tharga.Blazor](https://www.nuget.org/packages/Tharga.Blazor) | Generic Blazor UI components |
| [Tharga.Team.MongoDB](https://www.nuget.org/packages/Tharga.Team.MongoDB) | MongoDB persistence for teams and users |

## Links

- [GitHub repository](https://github.com/Tharga/Team)
- [Report an issue](https://github.com/Tharga/Team/issues)
