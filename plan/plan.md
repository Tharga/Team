# Plan: Grant-only scopes

Branch: `feature/grant-only-scopes` (from `master`)

## Steps

- [x] **1. Package updates (mandatory, up front).** `Tharga.MongoDB` 2.15.0 → 2.15.1 in
      `Tharga.Team.MongoDB.csproj` and `Tharga.Team.Service.csproj`. Build + full suite verified green
      (2072 passed) before any feature code.
      *Deferred with reason:* xunit.v3 3.2.2 → 4.0.0 and xunit.runner.visualstudio 3.1.5 → 4.0.0. This is
      the Microsoft.Testing.Platform migration attempted twice on 2026-08-18 and backed out both times on
      a Linux-only `BadImageFormatException` in CI. The backlog's own recorded recommendation is "do it as
      its own PR — it needs Linux-side iteration and does not belong inside a feature PR."

- [ ] **2. `ScopeDefinition` + `ScopeRegistry` API.**
      - Add `bool GrantOnly = false` to the `ScopeDefinition` record (fourth positional, source-compatible).
      - Add `ScopeRegistry.RegisterGrantOnly(string scopeName, string description = null)`, storing
        `DefaultMinimumLevel = AccessLevel.Custom, GrantOnly = true`.
        Chosen over a `grantOnly:` flag on `Register` because a grant-only scope has no meaningful minimum
        level, and requiring one is exactly the `AccessLevel.Custom` trap the issue walked into.
      - Same duplicate-name guard as `Register`.
      - XML docs stating what is and is not exempted.
      - Tests in `ScopeRegistryTests`.

- [ ] **3. Exempt from access-level grants.** Filter `GrantOnly` out of both branches of
      `GetScopesForAccessLevel` (the Owner/Administrator all-scopes branch and the fall-through). Update
      the class-level XML summary. Tests: excluded at Owner, Administrator, User, Viewer, Custom; still
      present in `All`; still resolved by `GetEffectiveScopes` via a role and via an override.

- [ ] **4. Exempt from tenant-defined custom roles.** `AuthorizationTeamServiceDecorator.ValidateCustomRoles`
      rejects a grant-only scope with its own message ("is a grant-only scope and cannot be added to a
      custom role"), distinct from the existing not-app-registered message. Tests.

- [ ] **5. Exempt from the override pickers.** `TeamComponent.razor` and `ApiKeyView.razor` build
      `_allScopeNames` from non-grant-only scopes, unioned with the principal's inherited scopes so a
      legitimate holder still renders as checked + disabled. Tests.

- [ ] **6. Exempt from the custom-role scope picker.** `TenantRoleManager.razor` `_allScopeNames` excludes
      grant-only, so the UI cannot offer what step 4 rejects. Test.

- [ ] **7. Keep it visible in the catalogue.** `ScopeRow` gains `GrantOnly`; `ScopeReference.Build` sets it
      and returns an empty `AccessLevels` list for such a scope. `ScopeView.razor` renders a marker and a
      short explanation. Tests in `ScopeReferenceTests`.

- [ ] **8. Typo safety for code-registered roles.** A startup `IHostedService` (shape follows the existing
      `RetiredScopeCheck`) that **logs a warning** naming any `ITenantRoleRegistry` role scope absent from
      `IScopeRegistry`. Warn rather than throw: the currently documented role-only workaround deliberately
      puts unregistered scopes on code roles, and throwing would break every host following the guide as
      written. Tests.

- [ ] **9. Sample.** Demonstrate `RegisterGrantOnly` plus the role that grants it in
      `Tharga.Team.Sample/Program.cs`.

- [ ] **10. Version.** `MAJOR_MINOR` 3.13 → 3.14 in the workflow. Additive API, no break.

- [ ] **11. Full build + test suite green.** Commit.

- [ ] **12. Docs (`docs:` commit).** Rewrite `implementation-guide.md` → *Grant-only scopes* to lead with
      `RegisterGrantOnly`, keep the role-only approach as the alternative for hosts on ≤ 3.13, and state
      the three exemptions plus the toolkit-scope limitation. Check README for a scopes section.

- [ ] **13. Close-out (only on the user's confirmation).** Update `Requests.md` and the backlog entry
      under *Scopes & roles* (reduce to the deferred toolkit-releveling half), comment on and close #232,
      archive `plan/feature.md` to the Plan directory `done/`, `git rm -r plan`, final commit
      `feat: grant-only scopes complete`, push, open PR.

## Decisions

- **2026-08-24 — grant-only exempts three paths, not one.** A flag that only exempted
  `GetScopesForAccessLevel` would leave two self-grant routes open to any Administrator (custom roles via
  `team:manage`, override picker via `team:member:manage`). Confirmed with the user.
- **2026-08-24 — the override picker does not offer grant-only scopes.** Role assignment is the only UI
  grant path. Programmatic `ScopeOverrides` still carry it, since nothing validates those against the
  registry. User's choice.
- **2026-08-24 — toolkit-scope releveling deferred.** User's choice; kept in the backlog.

## Last session

2026-08-24 — Session start. Verified #232 against the code: part 1 shipped in 3.13.1, part 2 is the
residual. Branched, applied the MongoDB patch, verified 2072 tests green, wrote the plan. Awaiting
confirmation before step 2.
