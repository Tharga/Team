# Plan: A team member's "Last Seen" follows activity in the team

Branch: `feature/member-last-seen-on-activity` (from `master` at cf90c6d, after 4.1; GitHub Actions flow).

## Steps

- [x] 0. NuGet updates: checked 2026-10-18 (#334 close-out), nothing outdated. No deps commit.
- [x] 1. Mongo: `TeamRepository.SetLastSeenAsync` as one atomic positional update on a live team. Tests render
  the filter and update.
  - `$set Members.$.LastSeen` with a filter on key, `DeletedAt: null` and `Members $elemMatch Key`. A stamp on
    a deleted team, or one whose team vanished, is now a no-op rather than a null reference.
    `TeamRepositoryLastSeenTests` (3).
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

2026-10-18: steps 0–5 done and pushed for testing. Next: the user tests in the sample (with the default 15-minute
interval a fresh stamp is visible on the team page after a reload once the previous one is older than 15
minutes; the sample can lower it with an override for the test). Then close-out.

## README / docs changes needed at close

- Any guide or README text that says member Last Seen tracks team selection.
- Version: 4.1 is the current line; a bug fix ships as the next 4.1 patch unless bundled with features.
