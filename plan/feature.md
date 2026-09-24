# Feature: `teams:read` on a custom team service fails loudly instead of emptying the team page

`Requests.md` → *Tharga.Team* → "Granting `teams:read` to a consumer with a custom TeamService silently empties
the team page" (filed 2026-09-22, Priority High, on 3.21.2). Branched from `origin/master` at `f964eb8`
(PR #300 merged); `dotnet outdated` clean, so no leading `chore(deps)` commit.

**Priority tier 1 — authorization.** Granting a documented scope refuses access the toolkit should grant: every
user, a team Owner included, loses their team. Third project to hit it.

## The defect

`teams:read` switches every consuming surface into cross-team oversight mode (`TeamVisibility.CanSeeAllTeams`,
`TeamVisibility.cs:21`). Oversight enumerates through `TeamServiceBase.GetAllTeamsAsync` →
`GetAllTeamsInternalAsync` (`TeamServiceBase.cs:231`), whose default **yields nothing**. Only
`TeamServiceRepositoryBase` overrides it, so a host deriving `TeamServiceBase` directly gets, silently:

- `TeamComponent` and `TeamSelector`: "You are not member of a team" — the reported outage.
- `TeamStateService.GetVisibleTeamsAsync` (`:170`): the visible set is always empty, so the selection resolver
  always falls back to the caller's first own team and resets the cookie — an oversight user cannot keep any
  other team selected. *Read from the code, not yet reproduced.*
- `TeamsListView` / `UsersListView`: no teams, every per-user team count 0.
- `GetMyAccessRequestsAsync` (`TeamManagementService.cs:145`): a requester sees none of their requests.

"Returns nothing" is indistinguishable from "this user has no teams", so it presents as a data problem.

**The layer below already does it right.** `ITeamRepository<,>.GetAllTeamsAsync` (`ITeamRepository.cs:76`)
throws `NotSupportedException` naming itself and `teams:read`, with a remark saying exactly why a silent empty
list is wrong. The service-layer default is the odd one out.

## What changes

1. **The default throws.** `GetAllTeamsInternalAsync` throws `NotSupportedException` naming the type, the member
   to override and the scope — the same wording as the repository default beside it. It is reached only
   through the authorization decorator's `teams:read` check, so a host that never grants the scope never
   reaches it; a host that grants it without the override is broken today and becomes broken *visibly*.
   - The three sites that catch only `UnauthorizedAccessException` (`TeamStateService:178`,
     `SupportUnassignedView.razor:230`, `TeamManagementService:158`) **are not widened** to catch it — that
     would re-silence the same defect one layer up. This matches what a custom `ITeamRepository` already does
     on the Mongo path today.
2. **A startup check says so first.** `TeamServiceCompletenessCheck` gains the reachability rule
   `UserServiceCompleteness` already uses: when `teams:read` is reachable — mapped to any system role
   (including via `Consent.GrantTeamsRead`), or registered in the system scope registry, which is what a
   system API key draws on — and the registered team service overrides neither `GetAllTeamsInternalAsync`
   nor `GetAllTeamsAsync` below `TeamServiceBase`, report it. Logs an error by default, fails the boot under
   `ThrowOnIncompleteTeamService`, exactly as the facet half of the same check does.
3. **Documentation, where the decision is made.** The request rates this at least as highly as the code:
   - `SystemTeamScopes.Read` XML doc: it switches UI into oversight mode, which requires the team service to
     enumerate every team; a custom `TeamServiceBase` must override `GetAllTeamsInternalAsync`. Name the
     symptom — "you are not member of a team" for everyone — so the page is findable from the outage.
   - `ConfigureSystemRoles` and `ConsentOptions.GrantTeamsRead`: a pointer to the same.
   - **A stated contract for deriving `TeamServiceBase`**: extend `Tharga.Team/README.md` → *What you must
     override, and what happens if you do not* (UserServiceBase only today) with the TeamServiceBase members,
     marking which are silent and which throw. Mirror it in the implementation guide's *Cross-team visibility
     for oversight roles* and `Tharga.Team.Blazor/README.md`'s section of the same name.

## Not in scope

Found by the survey and deliberately left out — each is its own change, and bundling them makes this one
unreviewable:

- **The other silent defaults**: `GetTeamKeyByInviteKeyInternalAsync` / `GetByInviteKeyAsync` (that is #286),
  `GetInvitationInternalAsync` (invitation expiry never enforced), `GetInvitedMemberNameAsync`,
  `ITeamRepository.SetInvitationExpiryAsync`. They get **documented** in the contract table here; changing
  their behaviour is not.
- **Stale XML docs** found on the way: `GetInvitationInternalAsync`'s summary is orphaned above another
  member and cites an `InvitationExpiryWiringCheck` that does not exist; `ITeamRepository.SetInvitationExpiryAsync`
  claims a throw that the Mongo path never makes. To be filed on the backlog, not fixed here.

## Acceptance criteria

- [ ] A `TeamServiceBase` derivative without the override throws `NotSupportedException` from
      `GetAllTeamsAsync` (both overloads), naming the member to override and `teams:read`.
- [ ] `TeamServiceRepositoryBase` hosts, and hosts that override either member, are unaffected.
- [ ] A host that never grants `teams:read` never reaches the throw — the authorization decorator refuses first.
- [ ] The startup check reports the gap when `teams:read` is reachable by role, by consent grant or by the
      scope registry, and stays silent when it is not reachable or the member is overridden anywhere in the
      host's base chain.
- [ ] Under `ThrowOnIncompleteTeamService` the same gap fails the boot.
- [ ] Docs updated on `SystemTeamScopes.Read`, `ConfigureSystemRoles`, `GrantTeamsRead`, both READMEs and the
      implementation guide, including the TeamServiceBase contract table.
- [ ] Full suite green; `CrossTeamListingTests.TeamServiceBase_DefaultGetAllTeams_IsEmpty` replaced, not deleted.

## Done condition

Criteria met, the request marked Done in `Requests.md` with evidence, docs updated on every surface,
`plan/` removed in the close-out commit, PR open.

## Version

**No `MAJOR_MINOR` bump — proposed, to be confirmed.** A default changes, which is normally the bump case,
but the only consumers it reaches are those already broken by it: nobody who works today has to change code,
configuration or a grant to keep working. The PR says plainly that the default now throws.
