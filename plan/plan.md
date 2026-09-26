# Plan: roster guards fail closed

Spec: `plan/feature.md`. Branched from `origin/master` at `2144b84`.

- [x] 0. `dotnet outdated` — nothing to update, so no `chore(deps)` commit.
- [x] 1. **Tests first.** `RosterGuardTests` (Service.Tests); `TestTeamService` gained `Shape` (`TestTeamShape`:
      array / no `Members` / `Members` as a list) and `RemoveTeamMemberCallCount`. Red on exactly the six expected
      cases; the four controls passed.
- [x] 2. **Fix.** `RemoveMemberAsync` reads `GetMembersAsync` and refuses a member not on the roster (new
      `NotAMemberMessage` constant, shared with `SetMemberSuspendedAsync`); `GetMembersFromTeam` accepts any
      `IEnumerable<ITeamMember>`; `RequireNotLastAdministrator` ignores suspended members. Service.Tests 1,089 green.
- [x] 3. **Docs.** Guide *Leaving a team*: suspended administrators are not cover, and the guards apply to self-removal. README override table: new `GetMembersAsync` row (refused without a roster).
- [x] 4. Full suite 3,075 green.
- [~] 5. Push for user testing.
