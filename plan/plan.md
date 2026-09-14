# Plan: team access requests (spec 13)

- [x] 1. Branch `feature/team-access-requests` from `origin/master` (`da70d9d`, #278/#280 merged). No outdated packages. Baseline build + full suite green (2,750).
- [x] 2. Contracts: `TeamAccessRequest`, `TeamAccessRequestStatus`, `TemporaryConsent`; `ITeam` default members. Tests on shape/defaults. `TeamAccessRequest` (+ status enum), `TemporaryConsent`, `TeamAccessRequestRules` (levels, history cap 20, message max 1000) in `Tharga.Team/TeamAccessRequest.cs`; `ITeam.TemporaryConsent`, `ITeam.AccessRequests` default null.
- [x] 3. Consent-in-force rule + tests (unexpired, expired → previous, expired with no previous → none, standing). Named `TeamConsent.Resolve(team, utcNow)` → `ConsentInForce(ConsentedRoles, AccessLevel, ExpiresAt)` with `Covers(roles)`; public (MongoDB and MCP need it). `TeamConsentTests` (13).
- [~] 4. Route every consent read through it: `TeamGrantResolver`, `TeamContextResolver`, `GetConsentedTeamsAsync` post-filter, `TeamVisibility`/`TeamComponent`/`TeamsListView`, MCP team resource. Architecture test forbidding direct reads elsewhere.
- [ ] 5. Security tightening: `SetTeamConsentAsync` requires `team:manage` held as a member; a direct change clears `TemporaryConsent`. Tests (consent-derived Administrator refused).
- [ ] 6. Operations on `ITeamService` (default throwing members) + `TeamServiceBase` virtual persistence methods + rules (request/cancel/approve/deny). Tests first on the rules.
- [ ] 7. `AuthorizationTeamServiceDecorator` checks + `AuditingTeamServiceDecorator` entries; forward in every toolkit decorator. Tests.
- [ ] 8. MongoDB: entity fields (enums by name), atomic approve update, decided-request cap. Tests incl. BSON representation.
- [ ] 9. Facades: `ITeamManagementService` members (gated) and new `ITeamAccessRequestService` (filtered), registered by the library with `TryAdd`. Registration + architecture tests.
- [ ] 10. UI: request dialog + pending/cancel on non-member teams; manager approve/deny list; consent expiry display. Text catalogues.
- [ ] 11. `TeamNotificationMenu` + visibility/count logic tests; sample header placement.
- [ ] 12. Manual verification in the sample (two accounts ideally). Full suite.
- [ ] 13. Push for user testing.

## Notes

- Additive public API throughout (default interface members, virtual methods, new types). Hosts with their own `ITeamService` decorators must forward the new members (the #272 lesson) — release notes.
- Behaviour change: a consent-derived `team:manage` no longer changes consent — release notes.
- Docs when complete: new `docs/articles/access-requests.md`; implementation guide component table + consent section; README if it covers consent.
