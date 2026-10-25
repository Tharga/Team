# Plan: Scope enforcement check

Branch `feature/scope-registration-check`, from `master` at `1fb6f14` (4.4.0).

## Steps

- [x] 1. `dotnet outdated` across the solution. No updates available (2026-10-25), so no deps commit.
- [x] 2. Inventory every `[RequireScope]` interface in the shipped assemblies, with how each is registered
      and checked. Recorded below.
  - 13 interfaces: 10 behind `ScopeProxy`, 3 checked by hand. No toolkit interface uses
    `[RequireAccessLevel]`, so the guard covers `[RequireScope]` only.
  - Decision: the registration test does not resolve services for real. That would need every module's
    stores (MongoDB, mail). Instead it runs the last registration's factory against a stand-in provider,
    and asks what type the registration puts in front of the caller.
  - The shipped packages are read from `Tharga.Team.sln` (projects not marked `IsPackable=false`), so a new
    package cannot be skipped.
- [x] 3. Registration test plus exemption list, in `Tharga.Team.Blazor.Tests`, which references every
      package. Compose `AddThargaTeamBlazor` plus the modules that register the remaining interfaces
      (audit, API keys, mail, icon settings).
  - Check that it fails by temporarily attributing a method on a plainly registered interface. Not
    committed.
  - `ScopeEnforcementRegistrationTests`, `SelfCheckingServices`, `ShippedAssemblies`. 16 pass: 13 interfaces
    plus 3 guard checks.
  - Proven: `[RequireScope]` on `ITeamDirectoryService.IsSuspendedAsync` failed with the intended message.
    Reverted.
  - Added a ProjectReference to Support.Anthropic, the only shipped package the test project did not reference.
- [x] 4. In-memory team and user store for the proof test: one team, and a signed-in caller who is not a
      member and holds no system scope.
  - `StrangerTeamStore` / `StrangerUserStore`, inside `ScopeEnforcementProofTests`.
  - Every store write, and `GetTeamAsync`, throws `ReachedTheStoreException`.
  - Uses `SkipAuthStateDecoration`, the Blazor Server shape.
- [x] 5. Proof test over every attributed method on the exempted interfaces. Record each method that does
      not refuse with `UnauthorizedAccessException`.
  - 44 methods: 41 refuse. The 3 that don't are the ones predicted; see Findings.
- [x] 6. Show the findings to the user and apply the agreed decision for each.
  - Decided by the user 2026-10-25, all as recommended.
  - Finding 1: `AuthorizationTeamServiceDecorator.TransferOwnershipAsync` now requires `team:manage` on the
    team (`RequireTeamScopeAsync`), so a stranger is refused with `UnauthorizedAccessException` before the
    team is read. The owner-only rule stays in `TeamServiceBase`, and a system grant does not count. 4 tests.
  - Findings 2 and 3: removed `[RequireScope(team:read)]` from `SetMemberLastSeenAsync` and
    `SetInvitationResponseAsync`, and documented them as self-service and as checked by the invite code.
    No change at runtime.
  - The `userKey` sharp edge on the invitation response becomes a separate GitHub issue. Draft it for the
    user to review; it is not part of this PR.
  - Full suite green (Blazor 2296, Service 1474, the rest unchanged); warnings 14.
- [~] 7. Docs:
  - `RequireScopeAttribute` remarks.
  - `docs/articles/architecture.md`.
  - The implementation guide's note on plain `AddScoped`, if it needs a pointer.
- [ ] 8. Full suite and the warning baseline (14).
- [ ] 9. Notes: mark Plan 01 §3b done (option 1, with a pointer to this feature), and update the backlog's
      *Authorization guards* line so only §6b remains.

## Inventory

| Interface | Assembly | Registered by | Checked by |
|---|---|---|---|
| `IApiKeyManagementService` | Tharga.Team | `AddThargaApiKeys` → `AddTeamService` | `ScopeProxy` |
| `ISystemApiKeyManagementService` | Tharga.Team | `AddThargaApiKeys` → `AddSystemService` | `ScopeProxy` |
| `IIconSettingsService` | Tharga.Team | `AddThargaIconSettings` (from `AddThargaTeamBlazor`), proxy built by hand so the audit decorator sits inside | `ScopeProxy` |
| `IContactMessageService` | Tharga.Team | `AddThargaContactMessages` → `AddSystemService` | `ScopeProxy` |
| `IUserMessageService` | Tharga.Team | `AddThargaUserMessages` → `AddSystemService` | `ScopeProxy` |
| `IUserNoticeTeamService` | Tharga.Team | `AddThargaUserNotices` → `AddTeamService` | `ScopeProxy` |
| `ITeamMailboxService` | Tharga.Team | `AddThargaTeamMailbox` → `AddTeamService` | `ScopeProxy` |
| `IPostmasterService` | Tharga.Team | `AddThargaTeamMailbox` → `AddSystemService` | `ScopeProxy` |
| `IAuditReadService` | Tharga.Team.Service | `AddThargaAuditLogging` → `AddTeamService` | `ScopeProxy` |
| `IAuditOversightService` | Tharga.Team.Service | `AddThargaAuditLogging` → `AddSystemService` | `ScopeProxy` |
| `ITeamManagementService` | Tharga.Team | `AddThargaTeamBlazor`, a facet resolving to `TeamManagementService<TMember>` | By hand: reads in `TeamManagementService`, changes in `AuthorizationTeamServiceDecorator` |
| `IUserService` | Tharga.Team | `AddThargaTeamBlazor` → `AuthorizationUserServiceDecorator` outermost | By hand: self-service passes, the rest needs `users:manage` |
| `IUserManagementService` | Tharga.Team | `AddThargaTeamBlazor` → `AuthorizationUserManagementServiceDecorator` outermost, audit inside | By hand: `users:manage` |

`ITeamDirectoryService` and `ITeamInvitationService` mention `[RequireScope]` only in their XML docs. Mcp,
Support, Support.Anthropic, MongoDB, Entra, Images and Blazor declare no scope-bearing interface.

## Findings (step 5)

1. **`TransferOwnershipAsync`** carries `team:manage`. Nothing in the authorization layer checks it. The
   real check is in `TeamServiceBase.TransferOwnershipAsync`: only the current Owner, refused with
   `InvalidOperationException` after the team is read. That is stricter than the attribute, and the method is
   not virtual, so a custom store cannot drop it. This is the "related defect" Plan 01 already lists.
2. **`SetMemberLastSeenAsync(teamKey)`** carries `team:read` and passes through every layer. It stamps the
   *caller's own* membership (`GetCurrentUserAsync`), so a non-member changes nothing; it is self-service by
   its shape, like `LeaveTeamAsync`. `TeamStateService` calls it on every team selection, so gating it on
   `team:read` would throw for a `Custom`-level member who lacks it.
3. **`SetInvitationResponseAsync`** carries `team:read`, but its check is the invite code. The base enforces
   the code only when `InvitationOptions.Lifetime` is set; otherwise the store's own filter does
   (`TeamRepository.InvitationFilter`).
   - Sharp edge, not fixed here: `userKey` is a parameter rather than the caller. Only `TeamInviteView`
     calls it, passing the signed-in user's key.
4. **Not a scope finding:** `DecoratorDefaultMemberTests` listed its assemblies by hand and left out
   Email and Support.Anthropic. Switched it to `ShippedAssemblies`; it still passes (21).

## Last session

2026-10-25: plan confirmed; steps 2–6 done and committed. Next: step 7 (docs), and the issue draft for the
invitation `userKey`.
