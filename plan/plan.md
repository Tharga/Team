# Plan: architecture article in the docs site

## Steps

- [x] 1. NuGet check (mandatory, up front). Only `SixLabors.ImageSharp` 3.1.12 → 4.0.0, held for its paid
      build-time licence. Nothing to apply.

- [x] 2. Derive the real graph from the seven `.csproj` files — project references **and** the third-party
      packages that decide what a deployment carries. Not transcribed from `architecture-v4.md`; three of its
      implied shapes turned out not to match (recorded in `feature.md`).

- [x] 3. Confirm mermaid renders before writing diagrams into a page. Nothing in `docs/` used it yet, but the
      DocFX template ships `mermaid.core-*.min.js`, so fenced blocks work. Verified again after the build —
      `architecture.html` contains the mermaid markup rather than a code block.

- [x] 4. `docs/articles/architecture.md` — package table, reference graph, request-flow sequence, a short
      authorization section linking to the implementation guide rather than repeating it, and a hosting note.

- [x] 5. TOC entry after **Getting started**.

- [x] 6. Correct `Tharga.Team.Blazor/README.md`, which claimed WebAssembly support the package cannot deliver.
      The article would otherwise have contradicted the package's own first line.

- [x] 7. `docfx docs/docfx.json` — **0 warnings, 0 errors**, 9 conceptual files (was 8). `docs/_site` is
      gitignored, so the build leaves nothing to commit.

- [ ] 8. Close-out: archive `feature.md` to the Plan directory `done/`, `git rm -r plan`, final commit, push,
      open the PR. **Only when the user confirms.**

## Notes / decisions

- **Documents what is, not what is intended.** The Plan directory asked for exactly this, and it is why the
  article says `Tharga.Team.MongoDB` quarantines nothing and that the UI package carries the Mongo driver.
  A docs article describing the target would have been the eighth record this session asserting something
  untrue.
- **No published-URL link.** The first draft linked the article by a guessed `github.io` URL. The repo's other
  READMEs use relative `docs/articles/...` paths and no published URL is verifiable from the workflow, so it
  now uses the same convention.
- **v4 deliberately excluded** — see `feature.md`.

## Last session

Steps 1–7 complete. Nothing pushed, no PR.

Still open: §3b and the `release` job `concurrency` block (both decisions for the user); plan 01 §6b;
`UserServiceBase` inconsistent failures; system-role assignment; one options surface for Team; #142.
