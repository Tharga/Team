# Feature: replace ImageSharp with SkiaSharp in Tharga.Team.Images

## Goal

Get `Tharga.Team.Images` off `SixLabors.ImageSharp` and onto **SkiaSharp** (MIT), removing the Six Labors
licence obligation from the toolkit and from every consumer who resizes icons.

## Why

Two reasons, and the second is the one with a date attached.

1. **The package's declared licence is currently untrue.** `Tharga.Team.Images.csproj` sets
   `PackageLicenseExpression = MIT` while taking a direct dependency on ImageSharp under the Six Labors
   Split Licence, which requires a commercial licence for closed-source for-profit use above $1M annual
   gross revenue. `architecture.md` already describes this package as existing to keep "**`SixLabors.ImageSharp`**
   — and its licence terms — off consumers who do not resize images", which is an accurate description of a
   problem being contained rather than solved.
2. **The upgrade path is closed.** ImageSharp **4.0.0 requires a build-time licence key** — a `sixlabors.lic`
   file must be present or the build *fails*. `Tharga.Team.Images.csproj:36` already records this as the
   reason the reference is pinned to 3.x. So the package is frozen on a line that will stop receiving fixes,
   and the only way forward is either a purchase or a replacement.

## Why SkiaSharp

The usage is one 68-line file doing four things: decode a raster from `byte[]`, pad-resize to a square with
transparent padding (never upscale, never crop), encode PNG, and pass undecodable input such as SVG through
untouched. Skia does all of it, is MIT, and is Microsoft-maintained.

Rejected: **Magick.NET** (Apache 2.0, but tens of MB of native binaries to square an avatar);
**PhotoSauce.MagicScaler** (Apache 2.0 and the fastest, but cross-platform needs separate native codec
packages — one deployment obligation traded for three); **System.Drawing.Common** (Windows-only since
.NET 7, a dead end for Linux-hosted Blazor server); **pinning ImageSharp 2.1.x** (the last Apache 2.0 line,
but end-of-life with no security fixes — buys time, not a solution).

## Scope

- `Tharga.Team.Images` references `SkiaSharp` + `SkiaSharp.NativeAssets.Linux.NoDependencies` instead of
  `SixLabors.ImageSharp`.
- `ImageSharpIconProcessor` → **`SkiaIconProcessor`**, no compatibility shim. Behaviour identical.
- The test project moves to Skia too — it currently builds its fixtures and reads pixels back *with*
  ImageSharp, so leaving it would keep the licence obligation in the repo.
- Docs: `README.md`, `Tharga.Team.Images/README.md`, `docs/articles/icons.md`,
  `docs/articles/architecture.md` (table row **and** the mermaid dependency node),
  `docs/articles/implementation-guide.md`.

## Decisions taken up front (user, 2026-08-10)

- **Rename with no shim.** Technically breaking, but only for a consumer who registered the type by hand
  rather than calling `AddThargaImageProcessing()`; the type is `sealed`, so nobody has subclassed it, and
  no document names it. A kept-but-obsolete `ImageSharpIconProcessor` would mean shipping a type named
  after a library the package no longer uses.
- **The package takes `SkiaSharp.NativeAssets.Linux.NoDependencies`.** A self-contained `libSkiaSharp.so`
  that works in slim and Alpine containers with no host packages. We decode, scale and encode but never
  render text, so the font stack the full variant requires is unused. Leaving this to consumers is the
  "library owns the registration, or the library owns the outage" trap from `shared-instructions.md`:
  nothing fails at build time and it surfaces as a broken icon upload in production.

## Out of scope

- Changing what the processor *does*. Same squaring, same no-upscale rule, same transparent padding, same
  pass-through. This is a dependency swap, not a behaviour change — any visible difference in output is a
  defect of this feature.
- Renaming the `Tharga.Team.Images` package. Its name is implementation-neutral and stays.

## Version

Renaming a public type breaks compatibility, so this wants a **minor bump: `MAJOR_MINOR` 3.10 → 3.11** in
`.github/workflows/build.yml:11`, making the first release 3.11.0.

## Acceptance criteria

- [ ] No `SixLabors` reference anywhere in the repository, production or test.
- [ ] All 9 existing behaviours still hold, asserted by the ported tests: downscale-then-square, pad without
      upscaling, tall sources, square-and-within-bounds passes through by reference, square-but-oversized
      downscales, padding is transparent, content is never cropped, `MaxDimension = 0` disables, undecodable
      data passes through with its original content type.
- [ ] The JPEG case the XML docs call out — a source with no alpha channel must pad transparent, not black —
      gets a test. It has never had one, and it is the failure the `Rgba32` decode exists to prevent.
- [ ] `PackageLicenseExpression = MIT` is true of the dependency graph.
- [ ] `MAJOR_MINOR` bumped to 3.11.
- [ ] Build clean, full test suite green.

## Done condition

Icon upload verified in the running sample app on this machine, docs updated, and the user confirms.
