# Team & user icons

The platform gives teams and users real icons/avatars beyond email-based Gravatar. It's built on two
pluggable seams — **storage** (where bytes live) and **sourcing** (where a displayed image comes from) —
each with a working built-in default, so it works out of the box and can be swapped per site.

## The two seams

### `IIconStore` — storage
Where icon bytes are saved: `SaveAsync(kind, ownerKey, bytes, contentType) → reference`, `LoadAsync`,
`DeleteAsync`. The reference (an id/URL) is what's persisted on the team/user record.

- **Built-in default: `MongoIconStore`** (in `Tharga.Team.MongoDB`) — registered automatically by
  `AddThargaTeamRepository`, no extra package. Bytes live in their own `Icon` collection (keyed by
  reference), never inlined into the hot `Team`/`User` documents.
- **Custom:** `o.AddIconStore<T>()` for Azure Blob, S3, an existing DMS, etc. A custom store wins over the
  built-in one.

### `IIconSource` — sourcing
Where a displayed image comes from. An `IIconResolver` runs the registered sources in order and returns
the first non-null image; when none match, the avatar renders **initials**. Built-in sources, in order:

1. **`StoredIconSource`** — an explicitly-set (uploaded) icon. Registered first, so a stored icon takes
   precedence.
2. **consumer sources** — `o.AddIconSource<T>()`, so a host can supply images from its own system.
3. **`GravatarIconSource`** — for users with an email (when enabled).
4. **`DefaultIconSource`** — a configured generic default image, if any.

Resolution: **stored icon → custom sources → Gravatar → default image → initials.**

## Options

### `IconOptions` (upload limits — startup)
| Property | Default | Meaning |
|---|---|---|
| `MaxBytes` | 256 KB | Max **stored** size, validated after processing. |
| `MaxUploadBytes` | 10 MB | Max **original upload** accepted for reading, before downscaling. |
| `MaxDimension` | 256 | Max width/height (px) an image processor downscales to. 0 disables. |
| `AllowedContentTypes` | png, jpeg, gif, webp, svg | Accepted image types. |

Configure via `o.Icon` on `AddThargaTeam`.

### `IconSettings` (display/behavior — runtime-adjustable)
Registered as a singleton, so a host can change these at runtime (the sample has a page that does):

| Property | Default | Meaning |
|---|---|---|
| `GravatarEnabled` | true | Use Gravatar as a fallback for users without an uploaded icon. |
| `GravatarStyle` | `identicon` | Gravatar default-image style (`identicon`, `monsterid`, `retro`, `robohash`, `mp`, …). |
| `DefaultUserIconUrl` | null | A generic default image for users (after/instead of Gravatar). |
| `AllowUserUpload` | true | Whether users can upload their own icon. |
| `AllowAdminUpload` | true | Whether admins (`users:manage`) can upload an icon for a user. |

Configure initial values via `o.IconSettings`.

## Team icons

`ITeam.Icon` already exists on `TeamEntityBase`, so team icons need no entity change. A `team:manage`
holder sets a team icon from the team-management component (`TeamComponent` → the **Actions** button):
upload a file or point at an image URL (downloaded server-side). The operations —
`ITeamService.SetTeamIconAsync` / `ClearTeamIconAsync` — are gated by `team:manage` and audited
(`icon-set` / `icon-clear`); replacing or clearing deletes the previous blob. Rendered by `<TeamAvatar>`
(teams list, card title, team selector) with an initials fallback.

## User icons

Opt in by declaring `Icon` on your user entity (the same shape-based opt-in as `DirectoryId`/`LastSeen`):

```csharp
public record UserEntity : EntityBase, IUser
{
    public required string Key { get; init; }
    public required string Identity { get; init; }
    public required string EMail { get; init; }
    public string Name { get; init; }
    public string Icon { get; init; }   // opt in to user icons
}
```

> **Without that property, uploads are refused.** The reference write is a no-op on an entity that does
> not declare `Icon`, so an upload used to store the image bytes, silently discard the reference, and
> report success — an unchanged avatar, an orphan in the icon store, and nothing logged. Both
> `SetOwnIconAsync` and `SetUserIconAsync` now throw `NotSupportedException` naming the entity type,
> **before** any bytes are written. If you see it, declare the property.

### Forwarding `IIconStore` in a service subclass

`MongoIconStore` being registered by `AddThargaTeamRepository` is necessary but not sufficient.
`TeamServiceRepositoryBase` and `UserServiceRepositoryBase` take the store as an **optional** constructor
parameter, so a subclass that does not forward it receives `null` and every icon operation throws — even
though the store is correctly registered:

```csharp
public class UserService(
    IUserRepository<UserEntity> repository,
    IHttpContextAccessor httpContextAccessor,
    IIconStore iconStore = null)                       // accept it...
    : UserServiceRepositoryBase<UserEntity>(repository, httpContextAccessor, iconStore)   // ...and forward it
{
}
```

Omit the parameter and the failure reads as a registration problem, which sends you to look in the wrong
place. The exception text names both causes for that reason.

- **Self-service:** the profile page's **Change picture** action (`IUserService.SetOwnIconAsync` /
  `ClearOwnIconAsync`) lets a user upload their own icon as an alternative to Gravatar (gated by
  `IconSettings.AllowUserUpload`). The top-right profile avatar refreshes live.
- **Administrative:** on the users admin list, **Set icon** (`IUserService.SetUserIconAsync`, gated by
  `users:manage` + `IconSettings.AllowAdminUpload`) lets an admin set a user's icon.

Rendered by `<UserAvatar>` everywhere a user is shown (top-right menu, users list, member grids), which
resolves stored → Gravatar → initials.

## Serving endpoint

Stored icons are served at `GET /_tharga/icon/{reference}` to authenticated callers, with an immutable
cache header (the reference changes when the icon changes).

`UseThargaTeam` maps it for you. On the granular setup path, map it yourself — see below.

## The granular setup path

Hosts using `AddThargaAuth` + `AddThargaTeamBlazor` rather than the `AddThargaTeam` facade need no extra
icon registration: `AddThargaTeamBlazor` registers the whole chain (`IconSettings`, the icon sources,
`IIconResolver`, `AvatarChangeNotifier`). Configure it on the same options object the facade uses:

```csharp
builder.AddThargaTeamBlazor(o =>
{
    o.IconSettings.AllowUserUpload = false;
    o.Icon.MaxBytes = 512 * 1024;
    // o.AddIconStore<MyStore>();  o.AddIconSource<MySource>();
});

var app = builder.Build();
app.UseThargaAuth();
app.UseThargaTeamBlazor();   // maps GET /_tharga/icon/{reference}
```

`UseThargaTeamBlazor` is the counterpart to `AddThargaTeamBlazor`, mirroring `AddThargaAuth`/`UseThargaAuth`.
Skip it and avatars still render — stored icons simply 404, falling back to Gravatar or initials.

> **Upgrading from an earlier version?** The icon chain used to be registered only inside the facade, so
> `LoginDisplay` — which is in the layout and therefore renders on every page — threw on the granular path
> and took the Blazor circuit with it. `ValidateOnBuild` could not warn you, because Blazor resolves
> `@inject` properties at render time rather than through the constructor graph it walks. If you added a
> hand-rolled registration block to work around that, **delete it** now that the library registers the
> chain itself.

## Automatic squaring and downscaling — `Tharga.Team.Images`

Uploads larger than `IconOptions.MaxBytes` would be rejected. Add the optional **`Tharga.Team.Images`**
package to instead fit images within `MaxDimension` (256 px), **square them by padding the short side
with transparency**, and re-encode as PNG:

```csharp
builder.Services.AddThargaImageProcessing();
```

It registers an `IIconProcessor` (SkiaSharp) that the built-in store runs before validating/storing.
Formats it can't decode (e.g. SVG) pass through unchanged, as do images already square and within
bounds. Bring your own by registering a custom `IIconProcessor`.

The package carries its own Linux native assets, so there is nothing to install on the host and slim or
Alpine containers work as-is. **From 3.11 it uses SkiaSharp (MIT); 3.10 and earlier used ImageSharp**,
whose Six Labors Split Licence requires a paid licence for closed-source for-profit use above $1M annual
gross revenue — so if you held one on this package's account, you no longer need it. The processor type
was renamed `ImageSharpIconProcessor` → `SkiaIconProcessor` in the same release, which only affects code
that registered it by hand rather than calling `AddThargaImageProcessing()`. Output is unchanged.

### What squaring produces

**Content is never cropped and never upscaled.** The output side is
`min(max(width, height), MaxDimension)` — so an image that already fits is padded at its own size rather
than blown up to the box:

| Source | Output | Why |
|---|---|---|
| 1000×500 | 256×256 | scaled to 256×128, then padded |
| 100×50 | 100×100 | already fits, so padded only |
| 50×100 | 100×100 | tall sources take the same path |
| 300×300 | 256×256 | square, but larger than the box |
| 100×100 | unchanged | square and within bounds — the only pass-through case |

The point is avatar surfaces that reserve a square box: before squaring, each letterboxed a wide or tall
source in its own way. Cropping would square them too, and is precisely the failure to avoid — it takes
a face out of a portrait photo.

> [!IMPORTANT]
> **Behaviour change.** Output previously preserved the source aspect ratio, so a 1000×500 upload was
> stored as 256×128. New uploads are now squared. **Already-stored icons are not reprocessed** — there is
> no migration, and existing avatars keep their current shape until someone re-uploads.

**The upload dialogs say which behaviour is in effect.** Without a real processor the default is
`NoOpIconProcessor`, which does not resize, so the dialog reads *"Images larger than N MB are rejected"*
rather than promising processing that will not happen. Add the package and it switches to *"Images are
squared and downscaled automatically — the short side is padded, never cropped."*
`IconCapability.CanProcessImages(processor)` is the same check if you need it in your own UI.

## Quick start

```csharp
// Built-in Mongo store + Gravatar are automatic. Add downscaling and map admin/user upload to a role:
builder.Services.AddThargaImageProcessing();                 // optional: auto-downscale

builder.AddThargaTeam(o =>
{
    o.Icon.MaxDimension = 256;                               // resize target
    o.IconSettings.GravatarStyle = "identicon";              // or disable: o.IconSettings.GravatarEnabled = false
    o.ConfigureSystemRoles = roles => roles.Map("Developer", SystemUserScopes.Manage); // admin upload
});
```

Team icons work with no entity change; add `Icon` to your user entity for user icons.
