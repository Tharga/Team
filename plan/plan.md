# Plan: ScopeView reads the team's custom roles (#292)

Spec: `plan/feature.md`. Branched from `origin/master` at `2568af0`; packages clean at branch time.

- [x] 1. **Named `BuildForRoles`, not an overload** — a second `Build` taking a list makes the existing
      `Build(scopes, null)` ambiguous, which is a compile error in every caller passing a bare null,
      consumers included. Two of our own tests hit it immediately. A display fix should not cost anyone a
      cast, and an overload would have meant a version bump. Originally planned as: `ScopeReference.Build(IScopeRegistry, IReadOnlyList<TenantRoleDefinition>)`, with the existing
      `(IScopeRegistry, ITenantRoleRegistry)` signature delegating to it. Tests first in
      `ScopeReferenceTests`: a scope granted **only** by a custom role is credited to that role in the built
      rows, and the registry-taking overload still behaves as before.
- [x] 2. `[Parameter] public IReadOnlyList<TenantRoleDefinition> Roles` on `ScopeView`, mirroring
      `RoleEditor.Roles`. Supplied wins; otherwise resolve. This is what makes the rest testable without DI.
- [x] 3. Resolve the team and rebuild — **done in `OnInitializedAsync`, not inside
      `ApplyCurrentUserDefaultsAsync` as the issue suggested.** That method returns early under an access
      simulation and is skipped entirely without a principal, so rebuilding there would leave the role bar
      on code-only roles in exactly the cases somebody is inspecting narrowed access. Originally:  keep the synchronous code-roles build for the first render, then
      rebuild `_rows` and `_allRoleNames` from `ApiKeyRolePicker.ResolveAsync(tenantRoleService, registry,
      team.Key)` once the team is known. Reuses the `GetSelectedTeamAsync` call already made a few lines
      above the faulty filter — no new lookup.
- [x] 4. **The symptom-3 fix**, with the narrowing extracted to `ScopeReference.PreselectedRoles` so it is
      testable at all — the project has no bUnit, so a decision left in the component cannot be tested.
      The guard walks the whole chain (resolved roles → rows → preselected → grant), because asserting one
      link would miss the defect: the *narrowing* used a different role list from the *crediting*. Its
      negative twin pins the old behaviour so the defect is described rather than merely gone. Originally: : filter the member's `TenantRoles` against the **merged** list, then
      `RecomputeGrants`. Test in the shape of `ApiKeyRolePickerTests` — a member assigned a custom role has
      it preselected and its scopes are not greyed. This is the regression guard for the page/principal
      disagreement, so it is the one test that must not be skipped.
- [x] 5. Write the visibility-provider decision into the code as a remark, with the reason (a hidden role is
      still assigned and still grants at runtime, so filtering here recreates symptom 3). See `feature.md`.
- [x] 6. Prose: the component comment at `ScopeView.razor:15` and the README's `ScopeView` bullet — both
      still say `ITenantRoleRegistry`. Check `docs/articles/` for the same claim while there.
- [x] 7. Confirm `ScopeView.razor` still counts 14 literals for the text ratchet; if the edit moved it,
      update the recorded number deliberately rather than letting the test tell us.
- [ ] 8. Full suite. Manual check in the sample: define a custom role in `<TenantRoleManager />`, assign it
      to yourself, open `/scopes` and confirm the role is listed and its scopes are lit.
- [ ] 9. Push for user testing.

## Notes

- **Do not migrate `ScopeView` off literal text here.** It is `Pending` at 14 in `TextCoverageTests`; that is
  #204's work, and mixing it in would make this diff unreviewable.
- The fix is additive and opt-in — no `MAJOR_MINOR` bump.
- PR #298 (`feature/team-access-requests`) is open and touches no file this work touches; whichever merges
  second takes an ordinary merge from master.
- Close the issue with what shipped when the feature closes — it came from a consumer report, so the comment
  is the outward-facing half of the work.
