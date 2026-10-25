# Plan: an idle open tab keeps the user's last seen fresh

- [x] 1. Package updates (2026-10-25): every update applied, in `chore(deps)`. The .NET 10.0.14 wave is now complete, and bUnit 2.13.3 fixes the AngleSharp 1.8.6 break (bUnit-dev/bUnit#1957), so the AngleSharp hold is lifted and its csproj comment removed (#390). Full suite green.
- [x] 2. Tests first: `UserServiceBaseActivityStampingTests` (+4: no stamp with `recordActivity: false`, the throttle is left for the next real resolve, `true` stamps as before, the directory id is still backfilled); `UserEvictionTests.TheCheck_DoesNotCountAsTheUserBeingActive`; `TeamClaimRevalidatorTests.TheRefresh_DoesNotCountAsTheUserBeingActive`. `DecoratorDefaultMemberTests` covers the decorators by itself.
  - Red as expected with only the interface member: the revalidation tests (still the recording call) and the guard on both user-service decorators.
- [x] 3. `IUserService.GetCurrentUserAsync(ClaimsPrincipal, bool recordActivity)` with a default body forwarding to the one-argument resolve; `UserServiceBase` implements it (the one-argument resolve now calls it with `true`; only the last-seen stamp depends on the flag); both decorators forward it.
- [x] 4. `IsDisabledAsync` and the claim refresh resolve with `recordActivity: false`. `TeamMembershipClaimsBuilder.BuildAsync` gained `recordActivity = true` and keeps the one-argument call when recording, so the sign-in paths and their mocks are unchanged.
  - Two access-simulation test mocks set up only the one-argument resolve and drive the revalidator; they now set up both. A consumer's test that mocks `GetCurrentUserAsync(principal)` and drives revalidation will need the same: noted for the release notes.
- [x] 5. Version line to 4.6, in its own `build:` commit.
- [x] 6. Full build at the warning baseline (14). Full suite green: Service 1532, Blazor 2342, Email 985, Support 823, MongoDB 472, Mcp 121, Entra 58, Support.Anthropic 15, Images 11.
- [x] 7. Docs: `IUser.LastSeen` and the user-management article ("An open tab alone does not count").
- [ ] 8. Close-out: plan 15 phase 1 and the backlog bug marked done, NuGet re-check, archive, remove `plan/`, PR (closes #390).

## Last session

2026-10-25: steps 1-7 done and committed. Next: close-out and PR on the user's word.
