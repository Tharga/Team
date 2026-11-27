# Feature: audit entries name the caller inside a Blazor Server circuit

GitHub issue: #572. Branch: `fix/audit-caller-in-circuit`. Patch release, no public API change.

## Goal

An operation made from an interactive Blazor Server page is audited with the person who made it, so the
audit log shows them and notifications name them instead of "an unknown caller".

## Cause

`AuditHelper.BuildEntry` reads the caller from `IHttpContextAccessor` only. A circuit has no usable
`HttpContext`, so the entry gets no caller. Every audit writer goes through that helper: the team, API key and
icon settings decorators, and `IAuditEntryFactory`, which the support decorators, the Email services and the
team entry evaluator use. So the gap is wider than the team decorator the issue names.

## Scope

- The circuit's principal is published for each inbound circuit activity by one circuit handler that is always
  registered in Blazor Server hosts. It is held in an internal ambient in `Tharga.Team.Service.Audit`, visible
  only to `Tharga.Team.Blazor`, so host code cannot set who an entry is attributed to.
- `AuditHelper.BuildEntry` falls back to that principal, and the identity resolver that came with it, when
  there is no authenticated request principal. Caller type, source, identity, subject, team and access level
  are then filled exactly as for a request.
- The access-simulation enricher reads the same ambient; its own accessor and circuit handler are removed.
- Tests: the helper, the team decorator in a circuit (the issue's invite case, down to the notification's
  actor), the circuit handler, the simulation enricher, and registration.

## Out of scope

- Recording who sent a pending invitation (`InvitedBy`), suggested in the issue. A separate issue, if wanted.

## Acceptance criteria

1. A team operation made from a Blazor Server page (invite, remove, rename, ...) records the person who made
   it, and the default `team:invite` notification names them.
2. The same holds for every other audit writer used from a circuit (API keys, icon settings, support, mail).
3. An authenticated request principal still wins; with neither a request nor a circuit, entries are recorded
   as before (declared background actor, or unknown).
4. Work that outlives its circuit never names the person: the published principal is cleared when the circuit
   closes.
5. Simulation metadata is still recorded in a circuit.
6. All tests pass; docs describe it.

## Done when

Acceptance criteria met, the user has tested from the pushed branch and confirmed.
