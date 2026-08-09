# Plan: text catalogue foundation (#204, phase 1)

## Steps

- [x] 1. NuGet check. Only `SixLabors.ImageSharp` 3.1.12 -> 4.0.0, held for its paid build-time licence.
- [x] 2. Verify what already exists before designing. The resolver, `TextKey` and registration were all
      present; only coverage and discoverability were missing.
- [x] 3. `TextSet` + `ThargaTextProviderExtensions.ResolveAsync`.
- [x] 4. `ThargaTextKeys.All` by reflection over public static readonly `TextKey` fields.
- [x] 5. `TeamSelectorText` + `AccessLevelText`; `AccessLevelBadge.Text` now takes a resolved `TextSet`.
- [x] 6. Migrate `TeamSelector`. Its second caller `TeamComponent` passes `TextSet.Empty` with a comment
      marking it unmigrated - deliberately not a defaulted parameter, which would have rendered English
      silently in a component nobody had flagged.
- [x] 7. `TextCoverageTests` ratchet. **It immediately caught a literal tooltip the manual pass missed.**
- [x] 8. Build + full suite: **1863 pass**, warnings at the **11** baseline.
- [x] 9. Documentation - "Finding every key you can override" and "Keys are whole strings, not substitutable
      nouns" in the implementation guide, plus an explicit note that three components are mid-migration.
- [ ] 10. Close-out: archive, `git rm -r plan`, final commit, push, PR. **Only when the user confirms.**

## Notes / decisions

- **Ratchet, not gate** - reasoning in `feature.md`.
- **Attribute strings only** - the limit is stated in the test class so a zero is not read as "translated".
- **`AccessLevelText` is shared, not per-component**, so a level cannot be named two ways.

## Last session

Steps 1-9 complete. Nothing pushed, no PR.

Remaining for #204: `TeamComponent` (24), `UsersView` (3), `AuditLogView` (47), plus inline prose. The ratchet
records each count.
