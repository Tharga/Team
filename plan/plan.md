# Plan: route UsersView through the text catalogue

## Steps

- [x] 1. NuGet check. Only `SixLabors.ImageSharp` 3.1.12 -> 4.0.0, held for its paid build-time licence.
- [x] 2. Confirm the three literals are the whole job - the inline-prose matches in this file turned out to be
      XML doc comments, not rendered markup, so there is nothing beyond the attribute strings here.
- [x] 3. `UsersViewText` catalogue with an `All` array.
- [x] 4. Migrate `UsersView`: inject the provider, one `ResolveAsync`, indexer in markup.
- [x] 5. Move `UsersView` from pending to migrated in `TextCoverageTests`.
- [x] 6. Assert `ThargaTextKeys.All` picks up the new catalogue - the first catalogue added after the
      reflection was written, and the property a consumer relies on when upgrading.
- [x] 7. Correct the guide, which still listed `UsersView` among the unmigrated components.
- [x] 8. Build + full suite: green, warnings at the **11** baseline.
- [ ] 9. Close-out: archive, `git rm -r plan`, final commit, push, PR. **Only when the user confirms.**

## Notes / decisions

- **No new abstraction was needed.** The phase-1 mechanism transferred as-is, which is the result this
  increment was meant to test.
- `UsersView`'s existing `const int UsersTab/TeamsTab` do not clash - keys are always qualified.

## Last session

Steps 1-8 complete. Nothing pushed, no PR.

Remaining for #204: `TeamComponent` (24) and `AuditLogView` (47), plus inline prose in both.
