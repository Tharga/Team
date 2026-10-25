# Fix: an idle open tab keeps the user's last seen fresh

Phase 1 of `plans/Toolkit/Platform/planned/15-activity-and-work-time.md`, the backlog's one bug. Branch:
`fix/idle-tab-last-seen`, from master after #394.

## The defect (verified 2026-10-25)

`IUser.LastSeen` is documented as "when the user last made an authenticated request". The claim revalidation loop
(`TeamRevalidatingAuthenticationStateProvider`, every `ClaimRevalidation.Interval`, 30 minutes) resolves the user for
each live circuit through `IUserService.GetCurrentUserAsync(principal)`, which stamps last seen
(`UserServiceBase.TouchUserAsync` → `StampLastSeenAsync`, throttled to 15 minutes). It does so twice per pass:
- `IsDisabledAsync`, the eviction check;
- `TeamClaimRevalidator.TryRefreshAsync` → `TeamMembershipClaimsBuilder.BuildAsync`, the claim refresh.

So a tab left open overnight shows the user as seen within the last 30 minutes, all night.

## Fix

- `IUserService.GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal, bool recordActivity)`: the same resolve, and with
  `recordActivity: false` no last-seen stamp. The directory-id backfill and the profile refresh still run: they keep
  the record right, they are not activity. A default body forwards to the one-argument overload, so a host's own
  `IUserService` keeps compiling and behaves as before.
- `UserServiceBase` implements it; both decorators forward it.
- The revalidation loop resolves with `recordActivity: false`, in the eviction check and in the claim refresh
  (`TeamMembershipClaimsBuilder.BuildAsync` gains the flag; sign-in keeps recording).

## Not in scope

The rest of plan 15 (one browser activity signal, presence, work time). Member last seen
(`ITeamMember.LastSeen`) is stamped from reads of the selected team, not by the revalidation loop, so it is not
affected by this defect.

## Done when

- A revalidation pass for an idle circuit writes no last seen, for the user, by either route.
- Signing in, and every ordinary resolve, still stamps as before.
- A host's own `IUserService` without the overload keeps working.
- Full suite green at the warning baseline (14). Plan 15 and the backlog record phase 1 as done.
