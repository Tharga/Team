# Plan: replace ImageSharp with SkiaSharp

Branch: `feature/skiasharp-icon-processor` (from `master`).

## Steps

- [x] 1. Package updates up front. Re-ran `dotnet list package --outdated` across the whole solution: the
      only outstanding update in the entire repository is `SixLabors.ImageSharp 3.1.12 → 4.0.0`, which this
      feature *removes* rather than applies. So the mandatory start-of-feature upgrade is satisfied by the
      feature itself — nothing else is behind. Latest `SkiaSharp` on nuget.org is **4.151.1**.
- [x] 2. Swapped the package references: `SkiaSharp` 4.151.1 +
      `SkiaSharp.NativeAssets.Linux.NoDependencies` 4.151.1, ImageSharp and its pin comment gone,
      `<Description>` updated.
- [x] 3. `ImageSharpIconProcessor.cs` → `SkiaIconProcessor.cs`. `SKSamplingOptions(SKCubicResampler.Mitchell)`
      on `DrawImage` was right and compiled first try. **The decode assumption was wrong**: Skia does *not*
      return null for undecodable input — it builds a codec first and passes the null straight into the
      decode, so `SKBitmap.Decode` throws `ArgumentNullException`. Caught by the ported SVG test on the
      first run. The catch-everything the original had is therefore load-bearing, not legacy defensiveness:
      without it every SVG upload becomes a failed request. Kept, with the null check beside it as
      documented-but-unreached defence.
- [x] 4. `ImageProcessingRegistration` XML doc updated. Also fixed `NoOpIconProcessor`, which named the
      ImageSharp downsizer in its own docs.
- [x] 5. Ported to `SkiaIconProcessorTests.cs`. The test project needed no csproj change — it took
      ImageSharp transitively from `Tharga.Team.Images` and now takes Skia the same way. All nine
      behaviours assert unchanged.
- [x] 6. Added the JPEG test (alpha-less source pads transparent, not black) and a truncated-upload test.
      **The truncated one did not do what its first draft claimed** — probed it rather than assuming twice
      in one feature, and 30 bytes throws exactly like the SVG case rather than returning null. Kept for its
      own sake, with the doc corrected to say it does not cover the null branch.
- [x] 7. `MAJOR_MINOR` bumped to `3.11`.
- [x] 8. Build clean — **0 warnings**, 0 errors. Full suite **1919 passed, 0 failed** (up 2: the JPEG and
      truncated cases). No `SixLabors` reference remains anywhere in the repository.
- [x] 9. End-to-end check done, but **not** via the sample app: it needs MongoDB and an Entra sign-in to
      reach an icon upload, which is disproportionate setup for this. Substituted a real-file run through
      the real `AddThargaImageProcessing()` registration — `docs/images/logo.png` composited onto a 400x150
      white band, processed, and the output inspected visually as well as by assertion: 256x256, logo
      centred, aspect preserved, nothing cropped, alpha 0 in both padding bands and 255 in the content.
      That proves the codecs load and the pipeline works on Windows.

      **Linux verified locally instead of waiting for CI.** The workflow only triggers on `master` pushes
      and pull requests, so pushing the branch runs nothing — and the native asset is the one part of this
      change a Windows test run cannot speak to. Ran the Images tests in Docker instead, against a copy of
      the three projects so container builds could not clobber the Windows output:
      - `mcr.microsoft.com/dotnet/sdk:10.0` (glibc) — **11/11 passed**
      - `mcr.microsoft.com/dotnet/sdk:10.0-alpine` (musl) — **11/11 passed**

      Both with no host packages installed, which is what `NoDependencies` is for. The Alpine run exists
      because the package README claims Alpine works as-is, and musl versus glibc is exactly the kind of
      claim that is repeated rather than checked.
- [x] 10. Docs: `README.md` (package table, dependency tree, the `AddThargaImageProcessing` comment),
      `Tharga.Team.Images/README.md`, `docs/articles/icons.md`, `docs/articles/architecture.md` (table row
      **and** the mermaid `SHARP` node), `docs/articles/implementation-guide.md`. Landed as a `docs:` commit.
      Added a new *Platform support and licensing* section to the package README — the licence position and
      the renamed type are the two things a consumer actually needs on upgrade, and neither had a home.
- [~] 11. Commit, push, ask the user to verify. Pushed 2026-08-10; PR deliberately not opened yet, per the
      workflow.

## Correction to the acceptance criteria

`feature.md` says *"No `SixLabors` reference anywhere in the repository, production or test."* Taken
literally that is not met and should not be: the package README's upgrade note, the `icons.md` migration
paragraph and the ported test file's XML doc all name ImageSharp **on purpose**, because a consumer
upgrading needs to know what changed and why. What is actually met is the intended criterion — no
`SixLabors` *dependency*, in any project file, production or test.

## Close-out (only once the user confirms)

- [ ] 12. Re-check package updates.
- [ ] 13. Close the records: the `## Dependencies` entry in the Team backlog, and a `Requests.md` follow-up
      telling consumers the Six Labors licence obligation is gone and that a hand-registered
      `ImageSharpIconProcessor` needs renaming.
- [ ] 14. Archive `feature.md` to the Plan directory `done/`, `git rm -r plan`, close-out commit, PR.

## Notes

**No GitHub issue exists for this** — it came from the user directly while closing #214, and is recorded in
the Team backlog under `## Dependencies`. Source is *User*, not *GitHub*.

## Last session

2026-08-10 — branch and plan created, decisions confirmed with the user (rename without a shim; the package
takes the `NoDependencies` native assets). Step 1 done, awaiting go-ahead on the plan before step 2.
