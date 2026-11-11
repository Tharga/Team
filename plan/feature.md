# Feature: consented-access-audit

## Goal

Let a host stop recording **access traces** for callers who reach a team through its **consent** rather than
membership, while still recording what those callers **change** and what they were **refused**. Make every entry
written for a consented caller say so.

## Why

A caller holding a role a team consented to reaches that team like a member, and the enforcement proxies trace
every call they make exactly as they trace a member's. A host whose consented roles are its own staff (support,
operations) cannot keep their browsing out of a team's log, and a reader cannot tell from an entry that it was
written for an outsider let in by consent rather than a member. The provenance is already on the principal
(`TeamClaimTypes.MemberKey` is issued only from a membership, which `TeamScopePolicy.HasDirectTeamScope` relies
on), but neither proxy reads it, and `AuditMetadataKeys.AuthorizedVia` has a value only for a system grant.

## Scope

- `AuditMetadataKeys.AuthorizedViaConsent = "consent"`, written by `ScopeProxy` and `AccessLevelProxy` on every
  entry for a call admitted through consent, next to the existing `system-grant` value.
- `TeamScopePolicy.HasConsentedAccess(principal, teamKey)` (internal, like the rest of the policy): the caller holds
  the team's identity but neither a membership nor a team key for it. `HasDirectTeamScope` reads the same rule.
  Public to hosts as `AuditProvenance.IsConsented(principal, teamKey)`, the way `AuditAccess` exposes `CanRead`.
- `AuditOptions.RecordConsentedAccess` (default `true`, so no existing host changes). When `false`,
  `CompositeAuditLogger` drops `ServiceCall` entries stamped `authorization.via = consent`, whoever wrote them.
  `DataChange` entries (a method declaring `AuditMode.Change`, a decorator's change, a host's own change entry)
  and denials are written regardless.
- README and `docs/articles` audit sections.

## Out of scope

- Stamping consent on entries built by `IAuditEntryFactory` automatically. A host writing its own entry stamps
  it with the helper; the factory sees only an HTTP principal, so doing it there would work for REST and silently
  not for a Blazor circuit.
- System grants (`authorization.via = system-grant`) — unchanged.

## Acceptance criteria

- A consented caller's access trace carries `authorization.via = consent`; a member's carries no `authorization.via`.
- With `RecordConsentedAccess = false`: a consented caller's access trace is not written; their `Change` entry is
  written as `DataChange`, stamped; their denial is written; a member's access trace is written.
- With the default: everything is written as before, now stamped.
- `HasConsentedAccess` is true only for a consented caller of that team: false for a member, a team API key,
  another team's caller, and a caller with no team.
- The option survives `AddThargaAuditLogging` and `AddThargaTeam` (the reflection copy).

## Done when

All of the above is covered by tests, the suite is green, the docs describe the option and the stamp, and the
issue closes through the PR.
