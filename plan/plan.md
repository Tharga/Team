# Plan: Member edits in the MongoDB store write only what changes

Branch: `feature/atomic-member-writes` (from `master` at 582ace2, after #337; GitHub Actions flow).

## Steps

- [x] 0. NuGet updates: checked 2026-10-18 (#317 close-out), nothing outdated. No deps commit.
- [x] 1. Field edits as positional updates with `FirstOrDefault`; `$unset` for cleared `[BsonIgnoreIfNull]`
  fields. Tests render filters and updates, require the mode, and assert no read.
  - Every member write, `SetLastSeenAsync` included, goes through one `UpdateMemberAsync` helper. `SetOrUnset`
    handles the ignore-if-null fields. The repository's logger went: its only use was the duplicate warning.
  - `TeamRepositoryMemberWriteTests` (35). Run against the old repository, 33 fail; the 2 that pass are
    `SetLastSeenAsync`, already atomic since #317.
- [x] 2. Removals as `$pull` (`RemoveMemberAsync`, `RemoveMemberFromAllTeamsAsync`). Tests.
  - Same filter as before (team key only); `RemoveMemberFromAllTeamsAsync` still counts the teams it found.
- [x] 3. Run every new update against a real MongoDB server (throwaway database), including two overlapping
  edits.
  - A scratch test (not committed; CI has no server) resolved the real `ITeamRepository` through
    `AddMongoDB` + `AddThargaTeamRepository` against `localhost:27017/Tharga_Team_Scratch338`, dropped after.
    Every write landed as expected: cleared fields absent rather than null, the invite code kept on
    extension, an accepted invitation returned the team, writes to a missing team were no-ops.
  - Ten members renamed at once: all ten stuck, three runs out of three. On the old repository the same run
    lost 7 to 9 of the 10 renames, three runs out of three, so the race is real and easy to hit.
- [x] 4. Docs: remarks; `ITeamRepository` guidance for a host's own store.
  - Remarks on the helper and `SetOrUnset`; an interface remark on `ITeamRepository`; a "Writing your own
    store?" note after the TeamService example in `implementation-guide.md`.
- [x] 5. Full test suite; commit; push for testing (no PR yet).
  - Blazor 1501, Service 1308, MongoDB 229, Support 402, Email 189, Mcp 116, Entra 58, Images 11: 3814, all green.
    No new warnings.

## Notes

- 2026-10-18: issue #338 filed from the #317 work. The access-request writes (`PendingRequest`) and
  `SetLastSeenAsync` are the pattern to copy.

## Last session

2026-10-18: steps 0–5 done and pushed for testing. Next: the user's test, then close-out.

## README / docs changes needed at close

- Release note: the duplicate-row warning no longer comes from these writes; writes to a missing team are no-ops.
- Version: a bug fix on the 4.1 line, released as the next 4.1 patch.
