# Plan: Member edits in the MongoDB store write only what changes

Branch: `feature/atomic-member-writes` (from `master` at 582ace2, after #337; GitHub Actions flow).

## Steps

- [x] 0. NuGet updates: checked 2026-10-18 (#317 close-out), nothing outdated. No deps commit.
- [~] 1. Field edits as positional updates with `FirstOrDefault`; `$unset` for cleared `[BsonIgnoreIfNull]`
  fields. Tests render filters and updates, require the mode, and assert no read.
- [ ] 2. Removals as `$pull` (`RemoveMemberAsync`, `RemoveMemberFromAllTeamsAsync`). Tests.
- [ ] 3. Run every new update against a real MongoDB server (throwaway database), including two overlapping
  edits.
- [ ] 4. Docs: remarks; `ITeamRepository` guidance for a host's own store.
- [ ] 5. Full test suite; commit; push for testing (no PR yet).

## Notes

- 2026-10-18: issue #338 filed from the #317 work. The access-request writes (`PendingRequest`) and
  `SetLastSeenAsync` are the pattern to copy.

## Last session

2026-10-18: plan written; step 1 next.

## README / docs changes needed at close

- Release note: the duplicate-row warning no longer comes from these writes; writes to a missing team are no-ops.
- Version: a bug fix on the 4.1 line, released as the next 4.1 patch.
