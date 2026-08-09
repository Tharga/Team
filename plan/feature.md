# Feature: architecture article in the docs site

## Goal

Document how the packages actually fit together and where a request goes, so the structure can be understood
without reading seven `.csproj` files.

## Source

The Plan directory's "Small, unblocked" list: *"the reference graph and the request-flow sequence, as mermaid,
in `docs/articles/`. For v3 it documents the structure as it actually is."*

## Scope

`docs/articles/architecture.md`, linked from the TOC after **Getting started**:

- **Package table** — role, and what each one separates. Three of the seven exist purely to quarantine a
  dependency, which is the test a package has to pass here.
- **Reference graph** (mermaid) — project references solid, the third-party packages that decide what a
  deployment carries dotted.
- **Request-flow sequence** (mermaid) — a team-scoped read from a component, following the two things worth
  following: which interface the component holds, and where the scope is checked.
- **Short authorization section** pointing at the implementation guide rather than duplicating it.

**Everything in it was verified against the project files**, not copied from `architecture-v4.md`. That
mattered — see below.

## What verifying turned up

The graph is not what the summary descriptions imply, in three ways worth stating in the article:

1. **`Tharga.Team.Blazor` is not WebAssembly-clean**, despite being the UI package. It references
   `Tharga.Team.Service`, which references `Tharga.MongoDB` and `Swashbuckle.AspNetCore` **directly** — so
   taking the components also takes the MongoDB driver and the OpenAPI generator.
2. **`Tharga.Team.MongoDB` quarantines nothing.** `Tharga.Team.Service` references `Tharga.MongoDB` itself,
   so the persistence package keeps no dependency out of anyone's graph — unlike `Entra` and `Images`, which
   each keep a real one off consumers who do not want it.
3. **The storage seam lives in `Tharga.Team`**, not in the persistence package, so a second store would
   implement the same abstract members without any packaging change.

## A README corrected

`Tharga.Team.Blazor/README.md` opened with *"Works with both **Blazor Server** and **Blazor WebAssembly**."*
The article would have contradicted the package's own first line. Replaced with an accurate note: the
components are hosting-agnostic, the **package** is not WASM-clean today, and `Tharga.Team` is the one a
browser client can take cleanly.

That claim was not wrong when written — it describes the components — but it reads as a statement about the
package, and a consumer choosing packages for a WASM client would be misled by it.

## Acceptance criteria

- [x] Reference graph matches the `.csproj` files exactly, verified rather than transcribed.
- [x] Request flow shows the gated facet, the unchecked store contract, and where the scope is checked.
- [x] States why the store contract is deliberately unchecked (claims construction reads through it).
- [x] Does not duplicate the implementation guide's authorization rules — links to them.
- [x] Docs site builds with **0 warnings, 0 errors**; both mermaid diagrams render.
- [x] No claim in it contradicts a package README.

## Done condition

A reader can see the real dependency graph, the real request path, and the two places the structure is
awkward — without being told a target shape is the current one.

## Deliberately not included

The **v4 target**. It is a goal document, not the current structure, and putting it in the consumer docs site
would describe packages that do not exist. It lands with the `/v4` docs path if and when that release happens.
