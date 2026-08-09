# Plan: audit sign-in and first-sign-in user creation

## Steps

- [x] 1. NuGet check. Only `SixLabors.ImageSharp` 3.1.12 → 4.0.0, held for its paid build-time licence.
- [x] 2. Establish why the events were missing and where the hooks are — no auth event handlers existed, and
      the creation point sits in a package that cannot see the audit types.
- [x] 3. `AuthAuditEntries` in `Tharga.Team.Service` — both entry shapes defined once, and testable without an
      authentication pipeline.
- [x] 4. `UserCreatedEvent` on `UserServiceBase`, raised by `UserServiceRepositoryBase` **only** on the branch
      that created the record.
- [x] 5. Subscribe per scope in `AddThargaTeamBlazor`, wrapped so a failed audit cannot fail the sign-in.
- [x] 6. OIDC `OnTokenValidated` in `AddThargaAuth`, chained onto any existing handler.
- [x] 7. Tests — 9 covering both shapes, the identity fallbacks and the null cases.
- [x] 8. Build + full suite green, warnings at the **11** baseline.
- [x] 9. `Tharga.Team.Support` README corrected — it documented both events as unavailable.
- [ ] 10. Close-out: archive, `git rm -r plan`, final commit, push, PR. **Only when the user confirms.**

## Notes / decisions

- **Event rather than constructor parameter** — avoids both the packaging problem and a new forwarding hazard.
- **Two entries, not one** — arrival and record creation are different facts.
- **No team on either entry** — sign-in precedes team selection; claiming one would be an invention.
- Test gaps are listed in `feature.md` rather than papered over.

## Last session

Steps 1–9 complete. Nothing pushed, no PR.

#142 stays open. Phase 1 (Slack notifications) shipped earlier; this makes two more of its three named events
routable. Phases 2–5 — support cases, Slack inbound, AI bot, Jira — are unstarted.
