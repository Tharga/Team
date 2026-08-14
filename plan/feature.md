# Feature: simulation metadata reaches the audit log from a Blazor circuit

Fixes [Tharga/Team#220](https://github.com/Tharga/Team/issues/220), filed by Eplicta FortDocs
2026-08-10 against Tharga.Team.Blazor 3.12.0.

## Goal

An audit entry written from an interactive Blazor Server component while a simulation is active carries
`simulation.active`, `simulation.kind` and `simulation.target` — the same metadata the HTTP path already
records.

## The defect

`AccessSimulationAuditEnricher` has a single constructor taking `IHttpContextAccessor`, and returns at
`AccessSimulationAuditEnricher.cs:24` when `HttpContext` is null. In Blazor Server an interactive
component runs inside a SignalR circuit where there is no `HttpContext`, so the enricher returns early on
every entry written from a circuit and the simulation context is silently dropped.

The enricher is registered unconditionally as a singleton (`ThargaBlazorRegistration.cs:68`) whenever
`Simulation.Enabled` is set, and there is no seam through which a host can supply a principal — so this
cannot be worked around from the outside. FortDocs' acceptance criterion ("an audit entry written during a
demo records that a simulation was active") is unmeetable today.

Nothing here is unsafe: simulation removes scopes and roles and never identity claims, so entries already
name the real person. What is lost is the context that makes an otherwise puzzling record legible.

## Scope

- The simulation reaches the enricher inside a circuit, with no new host wiring.
- The HTTP path (controllers, SSR) keeps behaving exactly as it does today.
- The enricher stays a singleton — `CompositeAuditLogger` is a singleton that captures
  `IEnumerable<IAuditEnricher>` at construction, so a scoped enricher is not resolvable.

## Out of scope

- Audit **caller identity** in a circuit. That is the declared-actor path (`IAuditContextAccessor.Push`)
  and is working as designed; this feature adds no inference there.
- #219, #221 and #223, the other open simulation issues from the same reporter.

## Approach

`CircuitHandler.CreateInboundActivityHandler` wraps every inbound circuit activity — the supported seam for
flowing ambient state through a circuit's event handlers. A circuit-scoped handler resolves the principal
once per inbound activity and publishes it through an `AsyncLocal`-backed singleton accessor that the
enricher reads when `HttpContext` is absent.

The simulation is already stamped onto the principal as `AccessSimulationCookie.ClaimType` for exactly this
reason — the revalidator runs in a circuit where there is no cookie to read — so the fallback reads the same
source the HTTP path does, not a second one.

## Acceptance criteria

- [ ] With no `HttpContext` and an ambient principal carrying a simulation claim, the enricher writes all
      three metadata keys.
- [ ] With an `HttpContext` present, it is preferred over the ambient principal (controllers and SSR are
      unchanged).
- [ ] With neither source, nothing is added and nothing throws.
- [ ] A malformed claim value adds nothing and does not throw, on both paths.
- [ ] The ambient principal is cleared when the inbound activity completes, including when it throws.
- [ ] The enricher remains a singleton and needs no host registration beyond `Simulation.Enabled`.
- [ ] Full test suite green; `dotnet build -c Release` clean.

## Done condition

The user confirms the fix, the docs surface is reviewed, and #220 is closed with the shipped evidence
together with the corresponding entries in the central requests file and FortDocs' request file.
