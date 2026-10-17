# Plan: A team member's "Last Seen" follows activity in the team

Branch: `feature/member-last-seen-on-activity` (from `master` at cf90c6d, after 4.1; GitHub Actions flow).

## Steps

- [x] 0. NuGet updates: checked 2026-10-18 (#334 close-out), nothing outdated. No deps commit.
- [x] 1. Mongo: `TeamRepository.SetLastSeenAsync` as one atomic positional update on a live team. Tests render
  the filter and update.
  - `$set Members.$.LastSeen` with a filter on key, `DeletedAt: null` and `Members $elemMatch Key`. A stamp on
    a deleted team, or one whose team vanished, is now a no-op rather than a null reference.
    `TeamRepositoryLastSeenTests` (4).
  - Found in the browser test against a real database: Tharga.MongoDB's default `UpdateOneAsync` mode finds the
    document and then updates it by id, so the server rejected the positional `$` ("The positional operator
    did not find the match needed from the query"). The page carried on as designed and logged a warning.
    `OneOption.FirstOrDefault` sends filter and update as one `findAndModify`; a test pins the mode. Rendering
    the update alone had looked right.
- [x] 2. `TeamServiceBase`: throttle `SetMemberLastSeenAsync` per (team, user) per process;
  `LastSeenStampInterval`. Tests.
  - A static dictionary, like `UserServiceBase`, since the service is scoped. The explicit stamp on selection
    is throttled too. `MemberLastSeenThrottleTests` (8); `TestTeamService` records stamps and takes an interval.
- [x] 3. `TeamStateService`: stamp the member for the resolved team on every resolve, never throwing. Tests.
  - In `GetSelectedTeamAsync` after the lock is released; a failure is logged (new optional logger parameter).
    `SelectedTeamActivityStampTests` (5).
- [x] 4. Docs: XML docs (`ITeamMember.LastSeen`, `IUser.LastSeen`, `ITeamService.SetMemberLastSeenAsync`,
  `TeamActivity.LastUsed`); guide/README mentions of member Last Seen.
  - `docs/articles/user-management.md` (two passages, plus a "Changed in 4.1" note) and the Blazor README.
- [x] 5. Full test suite; commit; push for testing (no PR yet).
  - Blazor 1501, Service 1308, MongoDB 193, Support 402, Email 189, Mcp 116, Entra 58, Images 11: 3778, all green.
    No new warnings.

## Notes

- 2026-10-18: the issue's option A (stamp on resolve) is taken. Found while reading the code: the Mongo stamp
  replaces the whole team document, which stamping on activity would turn into a frequent lost-update risk, so
  step 1 makes it atomic first.

## Last session

2026-10-18: steps 0–5 done. Browser test (driven for the user) first caught the positional-update failure above;
after the fix: the selected team's row went from "48 minutes ago" to "2 seconds ago" with no team switch, the
other team stayed at "68 days ago", two page loads made one team update, and the log was clean. Next: close-out.

## README / docs changes needed at close

- Any guide or README text that says member Last Seen tracks team selection.
- Version: 4.1 is the current line; a bug fix ships as the next 4.1 patch unless bundled with features.
