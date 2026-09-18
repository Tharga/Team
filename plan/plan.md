# Plan: invite-accept-selects-team

Issue: [Tharga/Team#287](https://github.com/Tharga/Team/issues/287)
Branch: `feature/invite-accept-selects-team`

## Steps

- [x] 1. `chore(deps): nuget update` — SkiaSharp and SkiaSharp.NativeAssets.Linux.NoDependencies
      4.152.0 -> 4.152.1 in `Tharga.Team.Images`. Build clean, 2768 tests pass. Commit `341b958`.
      Nothing else was outdated across the solution.

- [x] 2. Pin the current behaviour in a test, before changing anything.
      A bUnit render of `TeamInviteView` with a faked invitation, a signed-in user who already has a
      different team selected, and a recording `NavigationManager`. Assert what happens today: the
      selection does not end up on the joined team, and the navigation target is the invitation page.
      No bUnit test renders this component yet, so this step also builds the harness the later steps
      reuse (`AddTestAuthorization`, Radzen services, `ILocalStorageService`, `ITeamInvitationService`,
      `ITeamManagementService`, `IUserService`, `IThargaTextProvider`).

- [x] 3. Add the destination option — `ThargaBlazorOptions.HomePath`.
      Where the toolkit navigates when it wants the site's landing page. `null` (default) means the
      base URI. Reuse `InvitePathResolver`'s shape for the string handling, or generalise it — a host
      writes `/start`, `start` or `/start/` and means the same thing. Unlike `InvitePath`, blank must
      resolve to the root rather than to a fallback route, so it needs its own resolver rather than a
      reuse of that one.

- [x] 4. Let a caller select a team without triggering the reload.
      `ITeamStateService.SetSelectedTeamAsync(ITeam, bool reload)` as a **default interface method**
      delegating to the existing one, so a host with its own implementation keeps compiling and keeps
      today's behaviour. `TeamStateService` overrides it and skips `_navigationManager.Refresh(true)`
      when `reload` is false. Every existing call site keeps the one-argument form.

- [x] 5. Rewrite `JoinTeam` and `Decline` in `TeamInviteView`.
      Accept: record the response, clear the stored code, read the team back through the gated
      `ITeamManagementService.GetTeamByKeyAsync` (which passes — `TeamGrantResolver` resolves from live
      membership, not from the caller's stale claims, so the freshly accepted member holds `team:read`),
      select it with `reload: false`, then one `NavigateTo(destination, forceLoad: true)`.
      Decline: clear the stored code and navigate to the same destination, selecting nothing.

- [x] 6. Deal with the racing `SelectTeamEvent` bridge.
      With step 5 in place the domain still raises `SelectTeamEvent` from `SetInvitationResponseAsync`,
      and that handler still calls `Refresh(true)` on its own. Decide between: (a) stop raising it from
      the accept branch and let the view own the selection, or (b) keep it and make the bridge yield
      when a caller has already taken the selection in hand. **(a) is the recommendation** — the domain
      driving UI selection is the wrong shape, and the view now does it explicitly and awaited — but it
      is a behaviour change for any host subscribing to `ITeamService.SelectTeamEvent`, so it needs a
      release note and it is the one decision in this plan worth confirming before it is made.
      `CreateTeamAsync` keeps raising the event either way.

- [x] 7. Invert the step-2 tests and add the rest.
      All six acceptance criteria in `plan/feature.md`, including that exactly one navigation happens
      on each path.

- [x] 8. Full test suite, then documentation.
      `README.md`, `Tharga.Team.Blazor/README.md` (which documents `TeamInviteView` and `InvitePath`
      directly) and `docs/articles/implementation-guide.md` (`InvitePath` section around line 1421).
      Land as a separate `docs:` commit.

- [ ] 9. Close-out (only once the user says the feature is done).
      Re-run `dotnet outdated`, archive `plan/feature.md` to the Plan directory `done/`, `git rm -r plan`,
      final commit `fix: invite-accept-selects-team complete`, push, open the PR, comment on and close
      #287.

## Decisions

1. **Step 6 is (a)** - decided with the user. `SetInvitationResponseAsync` stops raising
   `SelectTeamEvent` from its accept branch; `TeamInviteView` owns the selection explicitly and awaited.
   `CreateTeamAsync` keeps raising it. This is a behaviour change for any host subscribing to
   `ITeamService.SelectTeamEvent` and must be named in the release notes.
2. **The option is `ThargaBlazorOptions.HomePath`** - decided with the user. Matches the
   `ProfilePath` / `TeamPath` / `InvitePath` family and `ThargaAuthRegistration`'s existing
   `HomePath = "/"` constant, and stays usable the next time the toolkit needs the site root.
3. **Version: minor** - additive API plus a changed destination a host may need to override, so
   3.22.0 off the current 3.21.2.

## Notes

**Steps 3 and 4 were done before step 2 could run.** A test naming an API that does not exist yet does not
fail, it fails to *build*, and a red build is not a red test. So the surface landed first and the tests
still preceded every behaviour change: `InviteAcceptLandingTests` was written against the old behaviour and
failed on exactly the three things this feature changes.

**bUnit records navigation history newest-first.** A navigation is prepended, not appended, so reading the
click's navigations off the end of the list returns the navigation that set the scene — which is
indistinguishable from a handler that never navigated. Cost two diagnostic rounds; noted in the test's own
remarks so the next person does not pay it again.

**Step 6 turned out to be the substance of the fix, not a tidy-up.** The selection wiring already existed
end to end; what was missing was that one of its two writers could not be awaited and the other was
navigating away underneath it.

## Last session

Steps 1-8 complete and committed (`341b958` deps, implementation, `71be7aa` docs). Full suite green: 2790 tests, 22 of them new. Awaiting approval to push the
branch, then the user tests from origin before close-out (step 9). The PR stays unopened until the
close-out commit, so `plan/` never reaches master.
