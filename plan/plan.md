# Plan: profile access card (impersonation + demo mode)

Branch: `feature/profile-access-card` (from `master`).

## Steps

- [x] 1. Package updates up front. `dotnet list package --outdated` across the whole solution reports
      nothing outstanding — the ImageSharp entry that used to be the only one left with the SkiaSharp
      migration in 3.11.0. Nothing to apply.
- [x] 2. **`ProfilePath` / `TeamPath` options** added to `ThargaBlazorOptions` beside `CreateTeamPath` and
      `InvitePath`, same shape: optional, null means unchanged. New pure `TeamMenuNavigation` resolver
      (defaults as named constants) read by `LoginDisplay` in place of the two literals. Blank is treated
      as unset — an empty string is what an unset config binding yields, and taking it literally would
      break the menu in the way this exists to prevent. 10 tests; 897 Blazor tests green.
- [x] 3. **The demo-mode target.** `AccessSimulationTargets.FromDemo(ownTeamScopes)` +
      `AccessSimulationState.StartDemoAsync()`. **No fifth enum member** — the user chose to reuse
      `AccessSimulationKind.Scopes` with a `DemoLabel` constant, so this adds no public enum API. The
      audit downside is smaller than it looked when the choice was made:
      `AccessSimulationAuditEnricher:31` writes `simulation.target` from `Label`, so a demo records
      `kind=Scopes, target=Demo mode` and is still distinguishable — via the target rather than the kind.
      `DemoLabel` is deliberately **not** localizable, because audit metadata that varies by operator
      language cannot be searched across a deployment.

      Sets no `AccessLevel`, which is what keeps the team access identical — `ApplyAccessLevel` returns
      immediately on null (`AccessSimulationFilter:72`), so the clamp never runs and the level consent
      granted survives.
- [ ] 4. **Prove the consent question rather than assume it.** A test that a caller whose team access comes
      from consent still holds those team scopes once `DropAppRoles` has removed the application role the
      consent keys off.

      **Answered by reading, still worth pinning.** `TeamServerClaimsTransformation.ApplySimulation` runs
      **last** (`:71`), after `_membershipClaimsBuilder.BuildAsync` has added the consent claims, and its
      XML doc says why: *"Last, and after everything else has been added — the point is to remove from the
      complete set, and a filter applied earlier would be undone by whatever was issued after it."* So
      consent is resolved from the caller's real roles and only then is the app role stripped; the team
      access survives, and it does not compound across requests because each request re-issues from the
      identity provider's claims. Nothing currently pins that ordering *for the consent case*, which is the
      one demo mode depends on — hence the test.

      Note `AccessSimulationConsentAccessTests` already covers a neighbouring question (a consent-only
      Developer *can* simulate, via `TeamGrantResolver`), which also settles the worry that
      `simulation:use` at Administrator would be out of reach for them. It does not cover this.

      **Done** — `AccessSimulationDemoModeTests.Demo_KeepsTeamScopesThatArrivedViaConsent`, plus 7 more
      covering the drops, the untouched team access and level, the label, and that de-escalation still
      holds. 8 tests.
- [x] 5. **The card.** `AccessSimulationCard` (user's naming choice), expandable, exit ungated exactly as
      `AccessSimulationBar:44` does it. Starts expanded whenever a simulation is active regardless of the
      `Expanded` parameter — a way out folded behind a disclosure is one you have to already know about.
      Renders nothing when the feature is disabled or the caller cannot simulate. Strings via
      `AccessSimulationCardText` through `IThargaTextProvider`; added to `TextCoverageTests.Migrated` and
      passing at zero literals.
- [x] 6. Mounted on the sample's profile page, with a comment recording that the bar in `NavMenu` remains
      the guaranteed way out. The sample already had `Simulation.Enabled = true` and the bar in place.

      **And moved the sample's profile page to `/account`** — the thing the user originally asked for, and
      the only way the new option gets exercised rather than merely shipped. `o.Blazor.ProfilePath =
      "/account"` in `Program.cs` is what keeps the profile-menu item working; without it the item 404s,
      which is precisely the defect the option exists for. So the sample now demonstrates the failure being
      prevented rather than a setting nobody sets. `TeamPath` is left unset (commented) because the sample
      does mount `TeamComponent` at the default route — showing that either can be set independently.

      Swept the sample's own links too: `NavMenu`, `Home` and the `IconSettingsPage` prose all pointed at
      `/profile`. Those are host-side links, unaffected by the option — worth noting, because "the menu
      item still worked" would otherwise have masked three broken ones.
- [x] 7. Build clean, full suite **1941 passed, 0 failed**.
- [ ] 5. **The card component.** Expandable, with impersonation and the demo toggle. Exit control
      ungated, mirroring `AccessSimulationBar.razor:44`. Strings through `IThargaTextProvider` — the
      project has a `TextCoverageTests` ratchet and a new component with literals would fail it.
- [ ] 6. Mount it on the sample's profile page.
- [ ] 7. `dotnet build -c Release` + `dotnet test -c Release`, full suite.
- [ ] 8. Run the sample: toggle demo on, confirm the system surface disappears, navigate back to the
      profile page, toggle it off, confirm system access returns. This is the done condition and cannot be
      substituted by unit tests — the round trip is the feature.
- [ ] 9. Docs: `docs/articles/access-simulation.md` (the new kind and the card), the component table in
      `implementation-guide.md`, and the options. Land as a `docs:` commit.
- [ ] 10. Commit, push, ask the user to verify.

## Close-out (only once the user confirms)

- [ ] 11. Re-check package updates.
- [ ] 12. Close the records: the backlog entry under `## Features`, and a `Requests.md` follow-up.
- [ ] 13. Archive `feature.md` to the Plan directory `done/`, `git rm -r plan`, close-out commit, PR.

## Open decisions, to settle before step 5

- **Component name.** It is a public API, so it wants deciding rather than drifting.
- **Whether demo mode is a fifth enum member or a `Scopes` simulation with a label.** The enum is public,
  so adding a member is an additive API change; a label is invisible but makes the banner read "custom
  scopes" instead of "demo mode".
- **Whether the card replaces `AccessSimulationBar`'s entry point or adds to it.** The plan assumes adds —
  the bar stays as the guaranteed way out.

## Version

`MAJOR_MINOR` is currently `3.11`. New public API (options, component, possibly an enum member) is
additive, so a **minor bump to 3.12** is the right call at close-out unless something breaks.

## Last session

2026-08-10 — branch and plan created. Step 1 done (nothing to update). Starting step 2, the path options,
which the user asked for explicitly.
