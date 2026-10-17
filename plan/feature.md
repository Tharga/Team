# Feature: A team member's "Last Seen" follows activity in the team, not only team switches

Source: [Tharga/Team#317](https://github.com/Tharga/Team/issues/317) (bug). Priority tier 2: a consumer can't
fix it from outside the library.

## Goal

A member who only ever uses one team shows a team-page **Last Seen** within the stamp interval of their user
Last Seen (the issue's acceptance criterion).

## Today

- `IUser.LastSeen` (users page) is stamped by `UserServiceBase` on every authenticated resolve, throttled to
  once per 15 minutes per user per process.
- `ITeamMember.LastSeen` (team page; `TeamActivity.LastUsed` on the teams list) is stamped only by
  `TeamStateService.SetSelectedTeamAsync`: the team selector, an invitation accept, or `SelectTeamEvent`.
  A returning user's team is restored by `TeamStateService.ResolveAsync` from the claim, cookie or local
  storage, with no stamp. Someone who never switches teams keeps the date they joined.
- The Mongo store writes the stamp by reading the whole team document and replacing it
  (`TeamRepository.SetLastSeenAsync`). Today that's rare. Stamped on activity, it would run every 15 minutes
  for every active member and could undo an administrator's concurrent change to the same team: a rename, a
  demotion, a suspension, or a removal.

## Scope

1. **The Mongo stamp becomes one atomic update** of that member's `LastSeen` (`Members.$.LastSeen`), on a live
   team only. Nothing else in the document is written, so it can't overwrite a concurrent change. A precondition
   for stamping more often.
2. **`TeamServiceBase.SetMemberLastSeenAsync` is throttled** per team and user, per process, like the user
   stamp: a new `protected virtual TimeSpan? LastSeenStampInterval` (default 15 minutes; null disables,
   zero stamps every time).
3. **`TeamStateService` stamps the member whenever it resolves the selected team**, including when the team
   is unchanged. Activity tracking never breaks the page: a failure is swallowed and logged. The explicit
   stamp on selection stays.
4. Docs: `ITeamMember.LastSeen`, `IUser.LastSeen`, `ITeamService.SetMemberLastSeenAsync` and
   `TeamActivity.LastUsed` say it tracks activity in the team.

## Out of scope

- The issue's alternative, stamping from `UserServiceBase` when the principal carries a team claim, which
  would also cover API and MCP callers. `UserServiceBase` has no team store (team depends on user, not the
  reverse), so it would need a new seam. Blazor activity is what the issue reports; noted for later.
- Other member edits (role, suspension, tenant roles, name...) still read the member list and write it back
  whole, so two administrators editing the same team at once can lose one edit. Pre-existing and rare (both
  are deliberate actions); worth its own issue.

## Acceptance criteria

- A member who stays in one team gets a team-page Last Seen that keeps up with their activity, at most one
  stamp interval behind.
- The stamp writes only `Members.$.LastSeen`, never the whole team.
- A store or user lookup failure while stamping never breaks resolving the selected team.
