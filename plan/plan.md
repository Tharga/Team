# Plan: simulation metadata reaches the audit log from a Blazor circuit

Branch: `feature/simulation-audit-enricher-circuit` (from `master`, CI = GitHub Actions).

## Steps

- [x] 1. **Package updates up front.** Applied: `Tharga.Toolkit` 1.16.0 → 1.16.1, `Tharga.Mcp` 1.0.1 →
      1.1.0, `Tharga.MongoDB` 2.14.2 → 2.14.3, `Microsoft.NET.Test.Sdk` 18.8.1 → 18.9.0 (7 test projects).
      All patch or minor — no majors to flag. `dotnet build -c Release` clean (13 pre-existing warnings,
      0 errors); `dotnet test -c Release` green — 1967 passed, 0 failed. Committed as `523fb83`.

- [x] 2. **Tests first** — new `AccessSimulationInCircuitTests` (11): the enricher's fallback (ambient
      principal read, `HttpContext` preferred over it, neither source, malformed claim), the handler
      (publishes for the activity, released on completion and on a throw, a failing
      `AuthenticationStateProvider` does not break the activity), the accessor (nested scope restores its
      parent), and the acceptance criterion end to end with its self-check.

- [x] 3. **`AccessSimulationPrincipalAccessor`** — internal, `AsyncLocal`-backed, `Push` returns a
      restoring scope guarded against double dispose. Instance field rather than the static one
      `AuditContextAccessor` uses; nothing here needs static reach, and per-instance keeps tests isolated.
      Null is a legitimate value to push — an activity with no readable state must shadow an outer
      principal, not inherit it.

- [x] 4. **`AccessSimulationCircuitHandler`** — overrides `CreateInboundActivityHandler`, resolves the
      principal per inbound activity (not once per circuit: an `AsyncLocal` set at circuit start does not
      flow into the call stacks the renderer later begins). A failure reading the state is logged and
      treated as "no principal" rather than propagated — recording context must not become a new way for
      an interaction to fail.

- [x] 5. **Extended the enricher** — `httpContextAccessor?.HttpContext?.User ?? principalAccessor?.Current`.
      No extraction needed after all: only the principal differs, the claim read is already one line, so
      both paths share it as it stands.

- [x] 6. **Registered both** inside the existing `if (o.Simulation.Enabled)` block. Added two guard tests
      to `AccessSimulationRegistrationTests`: the container really does inject the accessor into the
      enricher (the parameter is optional, so a missed registration would silently disable the fix), plus
      the self-check that the enricher still resolves without it.

- [x] 7. **Verified** — `dotnet build -c Release` 0 errors; full suite green, 1980 passed / 0 failed
      (Blazor 919 → 932).

- [x] 8. **Docs review** — added a paragraph to the *Auditing* section of
      `docs/articles/access-simulation.md` saying the metadata covers entries written from an interactive
      component and how the principal reaches the enricher. `Tharga.Team.Blazor/README.md` needed no
      change: its one line ("audited as the real person, with `simulation.*` metadata") was already the
      promise — it just was not true in Blazor Server until now. Checked the `simulation:use` scope name
      in both surfaces against `SimulationScopes.Simulate` while there; the docs are right and the issue
      text's `simulation:simulate` was the reporter's shorthand.

- [ ] 9. **Close-out** (only after the user says it is done) — package re-check, `plan/` archived to the
      Plan directory and removed, records closed: GitHub #220, the central `Requests.md`, the project
      backlog, and FortDocs' `requests.md` "Watching" entry.

## Notes

- Nothing to bump by hand: CI computes the package version from tags.
- This is a bug fix, so the commit prefix is `fix:` throughout.

## Last session

2026-08-14 — implemented and committed (`523fb83` packages, `0a94a9b` fix, `cd2d8fc` docs). Full suite
green at 1980. Next: push the branch for the user to test against FortDocs. Step 9 (close-out) waits for
the user to say the feature is done.
