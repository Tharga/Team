# Feature: idempotent system-scope registration (#237)

**Type:** fix
**Issue:** [Tharga/Team#237](https://github.com/Tharga/Team/issues/237)
**Branch:** `feature/idempotent-system-scope-registration`
**Target release:** 3.14.1 (patch — restores 3.13.1 behaviour, adds no API)

## Goal

A host may register a system scope the toolkit also registers, and may build its host more than once in a
process, without `AddThargaSystemScopes` throwing. Restores the ability to adopt 3.14.0.

## Why this is Tier 2

A consumer is pinned to 3.13.1 and cannot upgrade. Their integration suite went from 50 passing / 0 failing
to 2 passing / 48 failing on the version bump alone. The only host-side workaround is to probe for
"already registered" before every `Register` call, which pushes framework state management into every
consumer — so there is no workaround they can reasonably take.

## What actually broke, and what the issue gets wrong

The issue hypothesises that the scope registry "moved from per-container to process-wide (static)". **That is
not what happened.** Neither `ScopeRegistry` nor `SystemScopeRegistry` holds static state — `_scopes` is a
plain instance `List<>`. `SimulationScopes` is a static class of `const` strings only.

What changed in 3.14.0 is commit `fa279ec` ("split access simulation into a team run-as grant and a system
demo grant"): **the toolkit began registering `simulation:demo` itself**. Before that the scope did not exist
toolkit-side, so a host wanting demo mode had to register it — which is exactly what the reporting host does.
We took over a name consumers were already required to own.

Our own registration is guarded (`ThargaBlazorRegistration.cs:201`, following the precedent commit `ed42cc9`
set for `teams:purge`). The unguarded call is the **host's**, and we cannot guard code we do not own.

### The trap that makes the issue's option 2 insufficient

The issue proposes making re-registration idempotent "when key and description match". **The descriptions do
not match**, so that fix would leave the reported failure exactly as it is:

| Registrant | Description |
|---|---|
| Toolkit (`ThargaBlazorRegistration.cs:202`) | *Drop your own system scopes and application roles, to see the application as an ordinary tenant user does.* |
| Reporting host (issue body) | *Use demo mode and view-as on the profile page* |

So idempotency must key on **name alone**.

### Why the issue's option 1 is the wrong tool

Container-scoping the registry attacks a cause that is not present — the registry is already per-instance.
The collision is between two registrations **inside one container**, so scoping changes nothing about it.

### Still unexplained, and step 1 exists to settle it

The report says the *first* host builds fine and only later ones throw. A pure toolkit-vs-host collision in
one container would throw on the first build. So there is a second mechanism about repeated host
construction that is not yet identified. `AddThargaSystemScopes` probes for an existing registry by calling
`services.BuildServiceProvider()` and registers a concrete singleton **instance**
(`ScopeServiceCollectionExtensions.cs:41-46`) — that is where to look, and it may be a second defect behind
the first. **Do not write the fix before the reproduction fails for the same reason the consumer's suite
does.**

## Scope

In:

- `SystemScopeRegistry.Register` becomes idempotent on scope name.
- A genuine conflict — same name, materially different metadata — still fails, but with a message that names
  both registrants' descriptions instead of just the name.
- Whatever step 1 turns up about repeated host construction.
- Tests extending `SystemScopeRegistrationTests` (created by `ed42cc9` for this same bug class).
- Docs: `access-simulation.md` and `implementation-guide.md` must say the toolkit now registers
  `simulation:demo`, and that a host that also registers it is safe and may drop its own line.

Out, deliberately:

- **`ScopeRegistry` (team scopes) is not changed.** `ScopeRegistry.Add` throws on duplicate names too, so it
  carries the same bug class — but changing team-scope semantics interacts with grant-only scopes and with
  the open "let a host relevel the toolkit's own scope registrations" item (#232 residual). Widening this
  hotfix into that is how a patch turns into a design change. Record it; do not fix it here.
- **The `services.BuildServiceProvider()` probe is not redesigned**, unless step 1 proves it is the cause.
  Replacing it is a registration-pipeline change, not a patch.
- **Description precedence is not made configurable.** First registration wins; see the decision below.

## Decision: first registration wins, description included

On a duplicate name the later call is a no-op and the **first** description is kept.

The alternative — later wins, so a host can reword the catalogue entry — is attractive because these
descriptions are rendered in the host's own scope catalogue UI (`ScopeView`, `ScopeReference`). It is
rejected here because registration order is not something a host controls today (that is the substance of
the separate "One options surface for Team" request), so "later wins" would make the rendered description
non-deterministic rather than controllable. A host that wants to own the wording needs the relevel/replace
mechanism from the #232 residual, which is a feature, not this patch.

## Acceptance criteria

- [ ] A host registering a system scope the toolkit also registers does **not** throw, regardless of which
      runs first, and regardless of whether the descriptions differ.
- [ ] Building the same host twice in one process does not throw — asserted by a test reproducing the
      consumer's `WebApplicationFactory` shape. **Note: this passed before the fix**, which is what proved
      repeated construction was never the cause; it stays as a regression guard.
- [x] ~~A genuine metadata conflict still fails, with a message naming both descriptions.~~
      **Withdrawn 2026-08-24 — this criterion contradicted the one above it.** `SystemScopeDefinition`
      carries only `Name` and `Description`, so "a genuine metadata conflict" and "the descriptions differ"
      are the same case, and that case is precisely the reported failure. `Register` no longer throws on a
      duplicate name at all. See `plan.md` step 2 for the full reasoning.
- [ ] `simulation:demo` is registered exactly once in `All` after a double registration, so the scope
      catalogue shows no duplicate row.
- [ ] `ScopeRegistry` (team scopes) is untouched, and the same-bug-class note is recorded in the backlog.
- [ ] Docs state that the toolkit registers `simulation:demo` and that a host's own registration is now safe.
- [ ] Full test suite green.
- [ ] Issue #237 commented with what shipped and what the reporter can delete, then closed.

## Done condition

3.14.1 is published, and a host on the reporter's configuration can upgrade from 3.13.1 without editing its
scope registration.

## Package updates — held, deliberately, and needs your override to change

The workflow requires applying all outdated packages up front and bundling them into the feature PR. The
only updates available across the solution are **xunit.v3 3.2.2 → 4.0.0** and
**xunit.runner.visualstudio 3.1.5 → 4.0.0**, in all seven test projects.

That is the xunit 4.0 / Microsoft.Testing.Platform migration, which the backlog records as **attempted twice
and backed out both times**, failing in CI rather than locally: on the Ubuntu runner
`Tharga.Team.Mcp.Tests.dll` fails to load with `BadImageFormatException`, aborting the run at exit code 134
having run 1940 of 2047 tests. The backlog's own conclusion is *"Do it as its own PR."*

Bundling a twice-failed, CI-only-reproducible migration into a Critical consumer-blocking hotfix would very
likely prevent the hotfix from merging at all. **Held for this PR.** Say the word and I will apply it.
