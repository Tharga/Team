# Plan: forward `GetTeamKeyByInviteKeyAsync` through the `ITeamService` decorators

## Steps

- [x] 1. `chore(deps): nuget update` — `Microsoft.Testing.Extensions.CodeCoverage` 18.11.0 → 18.11.2 across
      all seven test projects. The only update `dotnet outdated` reported for the solution; patch only, no
      majors, nothing held back. Build clean, full suite 2654 passed / 0 failed. Committed as `a7435f4`.

- [x] 2. `InviteKeyForwardingTests` written before any production change. Both decorator tests failed
      against the unfixed code with `Expected: "team-1" / Actual: null` — the default body, confirmed rather
      than assumed. `TestTeamService` gained a `GetTeamKeyByInviteKeyInternalAsync` override so the fixture
      answers the lookup the way a real store does; without it the inner service returns null too and the
      test cannot tell a forward from a fall-through.

- [x] 3. `DecoratorDefaultMemberTests` added, using `GetInterfaceMap` to tell "the decorator declares it"
      from "the interface's own body is the target". Failed for exactly the two decorators and passed for the
      other six, which independently confirms the rest of the toolkit is sound.
      **Stated twice, deliberately:** a test can only reflect over assemblies its own project references, so
      the core assemblies are covered from `Tharga.Team.Service.Tests` and `Tharga.Team.Support` from
      `Tharga.Team.Support.Tests`. `ISupportCaseService` has no default members today — that guard is for the
      member somebody adds later. Both carry a self-check that fails if the scan finds no decorators.

- [x] 4. `AShortLink_ResolvesThroughTheFullDecoratorChain` drives `TeamManagementService` over
      authorization → audit → store, the order `ThargaBlazorRegistration` applies. Failed before the fix on
      the invitation being null.

- [x] 5. Both decorators now declare `GetTeamKeyByInviteKeyAsync` as a plain pass-through, placed with the
      other pass-through reads. Each carries a `<remarks>` saying why it is unchecked and unaudited, so the
      next reader does not "fix" it into a scope check that would refuse every invitee.

- [x] 6. Full suite green: 2672 passed, 0 failed, 0 skipped — up from 2654 at the dependency commit, the
      18 new tests accounted for. Committed.

- [x] 7. Documentation review done across both surfaces. **No API or documented behaviour changed** — the
      docs already described what the code was supposed to do, which is why nothing read as wrong while it
      was broken. One addition earned its place: `docs/articles/implementation-guide.md` told a host that
      short links work only if its store implements `GetTeamKeyByInviteKeyInternalAsync`, which sends anyone
      hitting this defect to inspect the one thing that was fine. It now names the affected versions beside
      that paragraph. `README.md` needed nothing — its Invitations section describes the link format, which
      is unchanged. No new article: a defect fix is not a feature area.

- [~] 8. Push the branch and hand it to the user to test. Do not open the PR yet.

- [ ] 9. On the user's confirmation: close-out. Re-run `dotnet outdated`, docs commit if any, update
      `Requests.md` / backlog if they carry this, archive `plan/feature.md` to the Plan directory `done/`,
      `git rm -r plan`, final `fix: <feature> complete` commit, push, open the PR.

## Release note

This is a **repair release**, not a routine one: 3.20.0, 3.20.1 and 3.21.0 all mint short links that never
resolve. Before publishing, put both mandatory deprecation questions to the user — what tag, if any, and
across which versions — with a proposed range. The affected range is 3.20.0 through 3.21.0, three versions.
Do not tag anything unilaterally, and publish 3.21.1 before tagging the old ones.

The version itself needs no edit: CI computes it from `MAJOR_MINOR: '3.21'` plus the tag count, so the
merge lands as 3.21.1 — correct for a defect fix with no consumer action beyond upgrading.

## Last session

2026-09-12 — Branch created off `master` (level with origin, clean tree). Issue #272 verified against the
code before planning: the missing member, both decorators, the registration order and the single caller all
check out. Confirmed the sibling case is sound — `IUserService.GetTeamMemberUsersAsync` is the toolkit's
only other default member, and both its decorators forward it, so #272 is the single hole. Dependencies
updated and committed. Tests written first and watched fail, fix applied, full suite green at 2672.
Documentation reviewed and one clarifying note added. Next: push for the user to test.
