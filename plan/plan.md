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

- [x] **2. `ScopeDefinition` + `ScopeRegistry` API.** Done 2026-08-24.
      - `ScopeDefinition` gained `bool GrantOnly = false` as a fourth positional parameter, with `<param>`
        docs on each member. Source-compatible: every existing construction site still compiles.
      - `ScopeRegistry.RegisterGrantOnly(string scopeName, string description = null)` stores
        `DefaultMinimumLevel = AccessLevel.Custom, GrantOnly = true`.
        Chosen over a `grantOnly:` flag on `Register` because a grant-only scope has no meaningful minimum
        level, and requiring one is exactly the `AccessLevel.Custom` trap the issue walked into. The XML
        `<remarks>` names that trap explicitly, so someone reading only the API doc does not repeat it.
      - The duplicate-name guard moved into a private `Add`, so `Register` and `RegisterGrantOnly` share
        one namespace rather than each guarding only against its own kind.
      - 5 tests in `ScopeRegistryTests`, including both collision directions.
      - *Behaviour is deliberately unchanged so far* — `GetScopesForAccessLevel` still returns a grant-only
        scope at Owner/Administrator until step 3. This step is the API surface only.

- [x] **3. Exempt from access-level grants.** Done 2026-08-24. Filtered before *both* branches of
      `GetScopesForAccessLevel` rather than only the Owner/Administrator one — a grant-only scope carries
      `DefaultMinimumLevel = Custom`, so the fall-through comparison `DefaultMinimumLevel >= accessLevel`
      would otherwise have granted it to User (4 >= 2) and Viewer (4 >= 3). That is the same arithmetic
      the issue's `AccessLevel.Custom` proposal fell foul of, so the `[Theory]` covers all five levels
      rather than just the two obvious ones. Class-level XML summary updated. 10 tests.

- [x] **4. Exempt from tenant-defined custom roles.** Done 2026-08-24.
      `AuthorizationTeamServiceDecorator.ValidateCustomRoles` rejects a grant-only scope with its own
      message, checked *after* the registered-scope check so the reason reported is the accurate one — a
      grant-only scope *is* registered, and reporting it as unregistered would send a host looking for a
      missing `ConfigureScopes` line that is actually present. A `<remarks>` on the method records why the
      check exists at all (custom-role management is gated by `team:manage`, which every administrator
      holds). 3 tests, one asserting the message is *not* the unregistered one.

- [x] **5. Exempt from the override pickers.** Done 2026-08-24. `TeamComponent.razor` and
      `ApiKeyView.razor` build `_allScopeNames` from non-grant-only scopes.
      **The union moved into `ScopeOverrideEditor` rather than the call sites.** `InheritedScopes` is
      computed per grid row while `AllScopes` is one shared array, so a per-row union at the call site was
      not expressible; the editor already receives both and now unions `AllScopes ∪ Inherited ∪ Overrides`
      for its option list. That also fixed a latent issue: a scope in `_value` but absent from `Data` is
      not something the Radzen dropdown renders sensibly.
      Existing overrides are included too, so a grant-only scope set programmatically stays *removable* —
      removal is de-escalation. Adding one is what the picker does not offer.

- [x] **6. Exempt from the custom-role scope picker.** Done 2026-08-24. `TenantRoleManager.razor`
      excludes grant-only, so the UI cannot offer what step 4 rejects and produce a save that always throws.
      Covered by the source scan in step 5's test file, which asserts all three feed sites filter and
      self-checks that it still finds an assignment in each — a scan matching nothing would otherwise pass
      forever while reading as "everything checked".

- [x] **7. Keep it visible in the catalogue.** Done 2026-08-24. `ScopeRow` gained `GrantOnly`;
      `ScopeReference.Build` passes it through. The empty `AccessLevels` came for free — `Build` already
      derives that list from `GetScopesForAccessLevel`, which step 3 taught to exclude grant-only. Without
      the flag such a row would read as an ordinary scope nobody happens to hold, which is why the marker
      and not just the empty list is the deliverable. `ScopeView.razor` renders a lock plus a legend line.
      4 tests.
      **Hit the #204 text ratchet.** `ScopeView` is an un-migrated component capped at 14 literal strings,
      and the tooltip made 15. Rather than raise the cap — the test says the number may only go down — the
      two new strings go through a new `ScopeViewText` catalogue, so the count stays 14 and the component
      is marginally closer to migration. New work does not get to grow the backlog it should shrink.

- [x] **8. Typo safety for code-registered roles.** Done 2026-08-24. `UnregisteredRoleScopeCheck` follows
      the `RetiredScopeCheck` shape and **logs a warning** per offending role scope, naming both the role
      and the scope. Warn rather than throw: naming an unregistered scope on a code role is the documented
      pre-3.14 way to get grant-only behaviour, so throwing would break every host that followed the guide
      as written. Registered in `ThargaBlazorRegistration` with `GetService` (both registries are optional)
      and tolerant of nulls. 6 tests, including one asserting a grant-only scope produces *no* warning —
      that it is registered is the whole point.

- [x] **9. Sample.** Done 2026-08-24. `case:read` registered grant-only, with a `CaseOfficer` role as its
      only grant path, in `Tharga.Team.Sample/Program.cs`. The sample already enables `EnableDynamicRoles`,
      so it also demonstrates the exclusion: the scope cannot be picked in `TenantRoleManager`.

- [x] **10. Version.** Done 2026-08-24. `MAJOR_MINOR` 3.13 → 3.14 in the CI workflow.

- [x] **11. Full build + test suite green.** Done 2026-08-24 — 2106 passed, 0 failed.

- [x] **12. Docs (`docs:` commit).** Done 2026-08-24. **Both** surfaces updated, per the rule that they
      are not alternatives:
      - `docs/articles/implementation-guide.md` → *Grant-only scopes* rewritten to lead with
        `RegisterGrantOnly`. New subsections: a table of the three exemptions with *why each has to be
        covered* (the two picker rows are the non-obvious half); what still grants it; what registering
        buys over hiding; the toolkit-scope limitation; and the pre-3.14 role-only approach kept as a
        still-working alternative rather than deleted, since hosts on ≤3.13 are following it today. The
        `AccessLevel.Custom` warning block was kept verbatim.
      - `README.md` — the scopes example now shows `RegisterGrantOnly`. The old inline comment explaining
        the unregistered-scope trick moved off `ConfigureTenantRoles`, which is no longer where the
        mechanism lives.

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

2026-08-24 — Verified #232 against the code: part 1 shipped in 3.13.1, part 2 is the residual. Branched,
applied the MongoDB patch, wrote the plan (2072 tests green). Completed steps 2–6: the `GrantOnly`
registration surface and **all three exemptions** — access-level grants, tenant-defined custom roles, and
the scope pickers. 2096 tests green.

Steps 4–6 were done as one unit deliberately: with only step 3 in place a grant-only scope is exempt from
level grants but still reachable by any administrator through a custom role or the override picker, which
is a half-built guard that reads as protection.

Then completed steps 7–11: catalogue visibility, the typo-safety startup warning, the sample, and the
3.14 version bump. 2106 tests green — the implementation is complete.

Step 12 (docs) is done as well — implementation guide and README both updated. 2106 tests green.

**The feature is complete and awaiting your testing.** Step 13 close-out — updating `Requests.md` and the
backlog, commenting on and closing #232, archiving `feature.md`, removing `plan/` and opening the PR —
starts only once you confirm it is done.
