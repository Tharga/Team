# Tharga.Team.Images

Image processing for Tharga Team icons. Registers an `IIconProcessor` (backed by
[SkiaSharp](https://github.com/mono/SkiaSharp)) that **squares and downscales** uploaded icons,
instead of rejecting ones larger than the configured maximum.

## Registration

```csharp
builder.Services.AddThargaImageProcessing();
```

The built-in icon store runs the processor before validating/storing. Any uploaded image (team or user)
is fitted within `IconOptions.MaxDimension` (default **256px**), **squared by padding the short side
with transparency**, and re-encoded as PNG. Formats Skia can't decode (e.g. SVG) pass through
unchanged, as do images that are already square and within the box.

**Content is never cropped and never upscaled.** The output side is `min(max(width, height), MaxDimension)`:

| Source | Output | Why |
|---|---|---|
| 1000×500 | 256×256 | scaled to 256×128, then padded |
| 100×50 | 100×100 | already fits, so padded only — no pixel is invented |
| 300×300 | 256×256 | square, but larger than the box |
| 100×100 | unchanged | square and within bounds |

Squaring exists so avatar surfaces that reserve a square box stop letterboxing wide and tall sources
inconsistently. Cropping would square them too, and is exactly what to avoid — it takes a face out of a
portrait photo.

> **Behaviour change.** Before this, output preserved the source aspect ratio, so a 1000×500 upload was
> stored as 256×128. New uploads are now squared. **Already-stored icons are not reprocessed** — only
> images uploaded from here on change shape.

Configure the maximum via the platform options:

```csharp
builder.AddThargaTeam(o => o.Icon.MaxDimension = 256);
```

## Platform support and licensing

**Nothing to install.** The package references `SkiaSharp.NativeAssets.Linux.NoDependencies` itself, so a
Linux host needs no `fontconfig` or `libfreetype`, and slim or Alpine containers work as-is. That
dependency is taken here rather than left to you deliberately: a missing native asset builds cleanly and
fails at upload time in production, which is the one failure a package reference can rule out entirely.

**SkiaSharp is MIT**, as is this package. Versions **3.10 and earlier used
[ImageSharp](https://github.com/SixLabors/ImageSharp)**, which is distributed under the Six Labors Split
Licence and requires a paid commercial licence for closed-source for-profit use above $1M annual gross
revenue. If you took a Six Labors licence on this package's account, you no longer need one for it.

### Upgrading from 3.10 or earlier

`ImageSharpIconProcessor` is now **`SkiaIconProcessor`**. If you call `AddThargaImageProcessing()` — which
is what the docs have always shown — nothing changes. Only code that registered the type by hand needs the
new name:

```csharp
services.AddScoped<IIconProcessor, SkiaIconProcessor>();   // was ImageSharpIconProcessor
```

Output is unchanged: same squaring, same no-upscale rule, same transparent padding, same pass-through for
undecodable formats. Stored icons are not reprocessed.
