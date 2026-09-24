# Plan: `teams:read` on a custom team service fails loudly

Spec: `plan/feature.md`. Branched from `origin/master` at `f964eb8`; packages clean at branch time.

- [x] 1. **Pin the defect with tests first.** Replace `CrossTeamListingTests.TeamServiceBase_DefaultGetAllTeams_IsEmpty`
      with tests that the default throws `NotSupportedException` from both `GetAllTeamsAsync` overloads, naming
      the member and `teams:read`; that an override of either member is honoured; that a caller without
      `teams:read` still gets `UnauthorizedAccessException` from the decorator, never the new throw.
      **Done:** four tests in `CrossTeamListingTests`; the two default-throws tests went red first.
- [x] 2. **Make the default throw** (`TeamServiceBase.cs:231`), wording matched to `ITeamRepository.GetAllTeamsAsync`.
      Rewrite its summary: why it throws rather than returning empty. Check every stub test service in the
      suite that relied on the empty default.
      **Done:** throws eagerly (no longer an iterator), as `ITeamRepository.GetAllTeamsAsync` does; the message
      also names the symptom. No stub in the suite relied on the empty default — 3,015 green.
- [x] 3. **Completeness rule**, pure and testable without a host: a `TeamServiceCompleteness`-style helper that,
      given a team service type, reports whether cross-team enumeration is overridden below `TeamServiceBase`
      (walking the base chain, as `UserServiceCompleteness.Overrides` does). Tests: direct derivative → gap;
      `TeamServiceRepositoryBase` derivative → none; override via an intermediate host base → none; override
      of the public `GetAllTeamsAsync` only → none.
      **Done:** public `TeamServiceCompleteness` + `TeamServiceGap` in `Tharga.Team`, the exact shape of
      `UserServiceCompleteness`. `Overrides` matches any overload by name (`GetAllTeamsAsync` has two, so
      `GetMethod` would be ambiguous). Mongo cases live in `Tharga.Team.MongoDB.Tests`.
- [x] 4. **Wire it into `TeamServiceCompletenessCheck`** with reachability: `teams:read` mapped on any role in
      `SystemRoleRegistry` (covers `Consent.GrantTeamsRead`), or registered in `ISystemScopeRegistry`. Error
      message names the type, the member, the scope and the symptom. Log by default, throw under
      `ThrowOnIncompleteTeamService`. Tests alongside `TeamServiceRegistrationCompletenessTests`.
      **Done:** reported before the facet check, through one shared `Report`. Tests in
      `TeamsReadCompletenessCheckTests` boot a real registration. 3,028 green.
- [x] 5. **XML docs**: `SystemTeamScopes.Read`, `ThargaTeamOptions.ConfigureSystemRoles`,
      `ConsentOptions.GrantTeamsRead`, `ThrowOnIncompleteTeamService` (it now covers a second kind of gap).
      **Done.** No version number in the prose: the line is 3.23, and the release number is CI's to decide.
- [x] 6. **Docs**: `Tharga.Team/README.md` contract table gains the TeamServiceBase members (silent vs throwing);
      implementation guide *Cross-team visibility for oversight roles* and `Tharga.Team.Blazor/README.md`
      section of the same name state the store requirement and name the symptom.
      **Done.** Contract table verified against `TeamServiceBase` before writing, not taken from the survey.
      Root README has no `teams:read` content, so nothing to change there.
- [~] 7. Full suite. Verify in the sample: a team service without the override plus a `teams:read` role —
      startup error logged, oversight page fails with the named exception rather than "not a member".
- [ ] 8. File the stale-doc findings (`InvitationExpiryWiringCheck`, `SetInvitationExpiryAsync`) on the backlog.
- [ ] 9. Push for user testing.

## Notes

- **Do not widen the `UnauthorizedAccessException` catches** in `TeamStateService`, `SupportUnassignedView` or
  `TeamManagementService.GetMyAccessRequestsAsync` — catching the new exception re-silences the defect.
- The startup check is the part a consumer meets first; the throw is the backstop for a host that ignores logs.
- Version: no bump proposed (see `feature.md`), pending the user's confirmation.
