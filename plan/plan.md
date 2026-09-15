# Plan: "View as another user…" works for a team Owner/Admin

NuGet: `dotnet outdated` on the whole solution reported **no outdated dependencies**
(checked at feature start), so there is no `chore(deps)` commit in this branch.

## Steps

- [x] 1. Write the failing tests first
  - New `AccessSimulationMemberNameTests` — 7 tests over name resolution and the identity source.
  - Fixed the shared fake in `AccessSimulationStateTargetsTests`: `FakeMember` declared
    `Name => Key`, so the per-team override was always set and display-name resolution was
    never reached. That fake is why the defect shipped. `Name` is now an init property
    defaulting to null, matching production.
  - Confirmed failing: 5 of 7 red, with `IUserService.GetUserByKeyAsync("bob")` named in the
    Moq failure output.

- [x] 2. Fix `AccessSimulationState`
  - New `IdentityLookupAsync()` resolves the source once per `GetMemberTargetsAsync` call via
    `UserDirectoryGate.Resolve(TeamScopeGate.HasSystemScope(..., SystemUserScopes.Manage))`
    and returns a key → `IUser` dictionary.
  - `DisplayNameAsync` became the static `DisplayName(member, users)` reading that dictionary.
    Override precedence and the `ResolveDisplayName` fallback are unchanged.
  - Decision: a `users:manage` holder keeps `GetAsync()`. `GetTeamsAsync()` is membership-only,
    so a consent-reaching administrator who is not a member of the selected team would
    otherwise lose names — and that caller is explicitly supported here.
  - N+1 against the user store removed as a side effect: one read per dialog open.

- [x] 3. Guard the class as a whole
  - New `GatedUserReadGuardTests`, generalising `FullDirectoryReadGuardTests`. That one covers
    a single member (`GetAsync`) on component files only, and both limits let this defect in:
    `AccessSimulationState` is a plain class and it called `GetUserByKeyAsync`.
  - The gated set comes from reflection over `[RequireScope(SystemUserScopes.Manage)]` on
    `IUserService`, so gating a new member automatically puts it in scope.
  - It found a second, pre-existing hit: `UserIconDialog.razor`. Investigated — not a defect.
    It serves both the self-service and admin case, branching on `AdminUserKey`, and the only
    caller that supplies it (`UsersListView.razor:260`) does gate on the scope. Recorded as a
    documented exemption with a staleness self-check, rather than weakening the marker.

- [x] 4. Run the full suite
  - Ran each test executable directly (SDK 10.0.302; see shared-instructions on local
    `dotnet test`). **2,763 tests, 0 failed** — Blazor 1178, Service 963, Support 363,
    MongoDB 103, Mcp 107, Entra 38, Images 11.

- [x] 5. Docs review
  - `docs/articles/access-simulation.md` was already **correct** — its capability table has always
    said `simulation:use` is a team scope at `Administrator` "reaching team owners and
    administrators, deliberately". The code disagreed with the documentation, not the reverse,
    so there was no wrong statement to correct.
  - Added two things instead, both in the article's existing style: a *fixed in 3.21.2* note
    recording the symptom and naming the `users:manage` workaround a host can now narrow, and a
    short **How members are named** paragraph under "Starting from a member" explaining the
    override → user record → email order and the raw-key fallback.
  - `README.md` and `implementation-guide.md` describe the components and the scope catalogue,
    neither of which changes. No edit needed.
  - Landed as `docs: record the run-as users:manage defect and how members are named`.

  Version: CI computes it from `MAJOR_MINOR: '3.21'` plus the next patch, and the latest tag is
  `3.21.1`, so this releases as **3.21.2**. A bug fix with no API change is a patch.

- [ ] 6. Close-out (only on the user's confirmation)
  - Re-run `dotnet outdated`.
  - Sweep records: `Requests.md`, backlog, GitHub issues.
  - Archive `plan/feature.md`, `git rm -r plan`, `fix: simulation-member-names-without-users-manage complete`.

## Last session

Steps 1-5 done. The picker now names members from `GetTeamMemberUsersAsync()` for a caller
without `users:manage`, and a reflection-driven guard covers every `users:manage`-gated
`IUserService` member across the whole Blazor project rather than one member on components.
Full suite green at 2,763 tests. Two commits on the branch: the fix and the docs note.

Next: push the branch (awaiting approval — *never push without explicit approval*) so the
user can test run-as as a plain team Owner. Do not open the PR until they confirm; the
close-out commit has to be last.
