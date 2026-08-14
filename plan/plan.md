# Plan: simulation metadata reaches the audit log from a Blazor circuit

Branch: `feature/simulation-audit-enricher-circuit` (from `master`, CI = GitHub Actions).

## Steps

- [ ] 1. **Package updates up front.** `dotnet outdated -u` across the whole solution, then
      `dotnet build -c Release` + `dotnet test -c Release` before any feature code.
      Available: `Tharga.Toolkit` 1.16.0 → 1.16.1, `Tharga.Mcp` 1.0.1 → 1.1.0,
      `Tharga.MongoDB` 2.14.2 → 2.14.3, `Microsoft.NET.Test.Sdk` 18.8.1 → 18.9.0 (7 test projects).
      All patch or minor — no majors to flag.

- [ ] 2. **Tests first** in `Tharga.Team.Blazor.Tests`, extending `AccessSimulationAuditEnricherTests`:
      ambient principal used when there is no `HttpContext`; `HttpContext` preferred when both are present;
      neither source adds nothing; malformed claim on the ambient path adds nothing and does not throw.
      Plus tests for the accessor itself — the value is visible inside the scope and cleared after it,
      including on an exception.

- [ ] 3. **`AccessSimulationPrincipalAccessor`** (internal, `Features/Simulation`, `AsyncLocal`-backed
      singleton) with a `Push(ClaimsPrincipal)` returning `IDisposable`, mirroring `AuditContextAccessor`.

- [ ] 4. **`AccessSimulationCircuitHandler : CircuitHandler`** overriding `CreateInboundActivityHandler`:
      resolve the principal from `AuthenticationStateProvider`, push it for the duration of the activity,
      release afterwards. Circuit-scoped, so it may take the circuit's own `AuthenticationStateProvider`.

- [ ] 5. **Extend the enricher** to fall back to the accessor when `HttpContext` is null. Extracts the
      claim-reading half so both paths share one implementation. Optional constructor parameter, so the
      existing tests and any hand-construction keep compiling.

- [ ] 6. **Register both** in `ThargaBlazorRegistration` inside the existing `if (o.Simulation.Enabled)`
      block — accessor as singleton, handler as scoped `CircuitHandler`. No host change.

- [ ] 7. **Verify** — `dotnet build -c Release`, `dotnet test -c Release`, full suite green.

- [ ] 8. **Docs review** — `docs/articles/access-simulation.md` and `README.md`. The audit-metadata section
      is the one that changes; decide whether the circuit behaviour deserves its own note.

- [ ] 9. **Close-out** (only after the user says it is done) — package re-check, `plan/` archived to the
      Plan directory and removed, records closed: GitHub #220, the central `Requests.md`, the project
      backlog, and FortDocs' `requests.md` "Watching" entry.

## Notes

- Nothing to bump by hand: CI computes the package version from tags.
- This is a bug fix, so the commit prefix is `fix:` throughout.

## Last session

2026-08-14 — branch created, plan written, awaiting confirmation before code changes.
