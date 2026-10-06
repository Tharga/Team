# Tharga.Team.Entra

Microsoft Entra ID user-directory provider for Tharga Team. Implements `IUserDirectoryService` over
Microsoft Graph so the platform can:

- **Verify** that a local user still exists (and is enabled) in Entra — resolved by the stored directory
  object id, falling back to email/UPN lookup (which also relinks the user).
- **List directory-only users** — users that exist in Entra but not in the platform.
- **Delete** a user from Entra (opt-in, from the user-delete flow). Graph performs a soft delete: the
  user is restorable by an administrator for 30 days, but is removed org-wide immediately.

## Registration

```csharp
builder.Services.AddThargaEntraUserDirectory(builder.Configuration);
```

Configuration is read from the `AzureAd` section (`TenantId`, `ClientId`, `ClientSecret`) — the same
section the Tharga platform sign-in already uses. Override or supply values in code:

```csharp
builder.Services.AddThargaEntraUserDirectory(builder.Configuration, o =>
{
    o.ClientSecret = builder.Configuration["Entra:ClientSecret"];
    // or plug any Azure.Core TokenCredential (e.g. managed identity):
    // o.Credential = new ManagedIdentityCredential();
});
```

## Incomplete configuration hides the directory

Without complete credentials — no `TenantId`, `ClientId` or `ClientSecret`, and no explicit
`Credential` — the directory reports `IsConfigured == false`, and every directory feature (verify
actions, the Directory column, the directory-only tab, the delete-from-directory opt-in) stays hidden
**exactly as if nothing were registered**. Offering a Verify button that throws on click is worse than
not offering it.

Calling the service directly still throws `InvalidOperationException` naming the three settings, so a
host bypassing the UI gets a diagnosis rather than a silent failure.

**Absent and half-set are treated differently, because only one of them is a mistake:**

| Configuration | Directory features | Log |
|---|---|---|
| No credential field set at all | hidden | silent — reads as a deliberate opt-out |
| Some set, some missing | hidden | **Warning** naming exactly which values are missing |
| A `Credential` supplied | available | silent |
| All three set | available | silent |

Registering the directory in every environment and supplying secrets in only some is a normal shape, so
that stays quiet. Half-filling a credential is not something anyone does on purpose, and the symptom —
directory features quietly absent — gives no clue where to look, so it warns once at startup.

> **Azure AD B2C has no `TenantId` key.** The tenant is embedded in `Authority`, so binding the
> `AzureAd` section leaves `TenantId` null and the directory unusable. Set it explicitly in the
> `configure` callback.

## Writing a display name back

`IUserDirectoryService.SetUserNameAsync` writes `displayName` via Graph `PATCH /users/{id}`. It needs no
permission beyond the `User.ReadWrite.All` that deletion already requires.

**Off by default.** Set `o.Blazor.WriteNameToDirectory = true` to have an **administrative** rename push
the name to the directory as well. Self-service renaming is never pushed, whatever the option says.

The local write always happens first and is never rolled back — a directory failure is reported on
`UserNameChangeResult.DirectoryError`. Coupling them would let a Graph outage block renaming a user in
the application.

The driver: an application that collects no attributes at sign-up holds the real name while the directory
holds a placeholder such as `"unknown"`, so the good name exists and cannot reach anyone administering the
tenant. A host federating from a corporate directory wants the opposite, which is why this is opt-in.

## Reading sign-in identities

`AddThargaEntraUserDirectory` also registers `IUserIdentityDirectory`, which reads a user's Graph
`identities` (`GET /users/{id}?$select=identities`):

```csharp
var identities = await identityDirectory.GetIdentitiesAsync(user.DirectoryId);
var upstream = identities.FirstOrDefault(x => x.SignInType == "federated" && x.Issuer == "https://legacy.example/");
// upstream?.IssuerAssignedId is the subject the external provider assigned
```

In an Entra External ID tenant, a user federated through a custom OIDC provider carries a `federated`
identity whose `IssuerAssignedId` is the upstream subject — the key for linking the directory user to an
account in an older system. Needs only `User.Read.All`. An unknown directory id throws
`InvalidOperationException`, as deletion and renaming do; a user with no identities yields an empty list.

## Entra app-registration permissions

Grant the app registration **application** (app-only) Graph permissions, with admin consent:

| Feature | Permission |
|---|---|
| Verify users, list directory-only users, read sign-in identities | `User.Read.All` |
| Delete users from Entra | `User.ReadWrite.All` |

Deleting also requires the app's service principal to hold a directory role allowed to delete users
(e.g. *User Administrator*) when the target is an administrator-role holder.
