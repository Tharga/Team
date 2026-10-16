# Plan: Personal API keys bound to their owner (#323)

Branch: `feature/personal-api-keys` (from `master` after PR #324 merged; GitHub Actions flow).

## Steps

- [x] 0. NuGet updates — `dotnet outdated` on the whole solution: nothing outdated (2026-10-16), no deps commit.
- [x] 1. Owner check in `ApiKeyAuthenticationHandler`: resolve the member and the user, refuse with a reason for gone, not `Member`, suspended, disabled or missing user; fail closed on fault (D1). Tests per reason, plus lifting a suspension or disable.
  - `ResolveOwnerAsync` in the handler; a new optional `IUserService` constructor parameter (DI supplies it via `AddScheme`). Without a team or user service to verify with, a private key is refused (fail closed). Each refusal is audited as an auth failure with its reason.
- [x] 2. Intersection: effective scopes = key ∩ owner grant (via `TeamGrantResolver`); `AccessLevel` claim = the less privileged of the two; `ApiKeyOwner` claim. Tests, including a demoted owner.
  - `AccessLevelPrivilege` moved from `Tharga.Team.Blazor.Features.Simulation` to `Tharga.Team` (still internal, unchanged) so the handler uses the same `Clamp` as simulation. `TeamClaimTypes.ApiKeyOwner` is new and deliberately not `MemberKey`, which `TeamScopePolicy` reads as "is a member". 15 tests in `ApiKeyOwnerBindingTests`; Service 1201/1201, Blazor 1419/1419, MCP 116/116.
- [x] 3. `apikeys:oversee` system scope (`SystemApiKeyScopes.Oversee`), registered with the other system scopes; `ApiKeyManagementService` grants oversight through it or the legacy `Developer` role (D2). Tests both ways.
  - `IsDeveloper` renamed to `CanOversee`; it reads the scope only as a `SystemScope` claim, so a team scope of the same name does not count. 5 tests in `ApiKeyOversightScopeTests`; Service 1206/1206.
- [x] 4. Cleanup on owner removal: find every removal path (remove member, leave, remove from all teams on user delete) and delete or disable the owner's private keys in that team; `ApiKeyOptions.OwnerRemovedAction` (D3). Tests per path and per option.
  - The shape copies the purge cascade. A new public `ITeamMemberRemovalParticipant` (hosts can add their own) and `TeamMemberRemovalCascade`, invoked from `AuthorizationTeamServiceDecorator` after each of `RemoveMemberAsync`, `LeaveTeamAsync` (current user via `IUserService`) and `RemoveUserFromAllTeamsAsync`. For the last, teams are read **before** removal, across every `AccessLevel`. The cascade never throws: the removal already happened, and authentication is the guarantee. `ApiKeyOwnerRemovalParticipant` goes through `IApiKeyAdministrationService`, so it covers a host's key store and fires lifecycle handlers; it is a no-op without a key service. Registered with `TryAddEnumerable` in the composition root.
  - Also fixed: `o.ApiKey` forwarding in `ThargaTeamRegistration` was a hand-written list (the #177 defect class); it is now `OptionsForwarder.Copy`, guarded by `ForwardsEveryApiKeyOption`.
  - Tests: 12 in `ApiKeyOwnerRemovalTests` + 2 in `AddThargaTeamTests`. Service 1217/1217, Blazor 1421/1421.
- [x] 5. Sample: map `apikeys:oversee` to the Developer role.
- [x] 6. Full test suite; commit.
  - Whole solution 3476/3476, none skipped.
- [x] 7. Push the branch for the user to test (no PR yet).

## Notes

- 2026-10-16 (user): D1 fail closed, D2 keep `Developer` until 4.0, D3 delete by default with `Disable` opt-in — all as proposed. Plan confirmed.

## Last session

2026-10-16 — All steps done and pushed for testing. Next: the user tests in the sample (create a private key, then remove, suspend or demote its owner); then close-out (NuGet re-check, docs, plan 05 v4 note, Requests.md note, version line 3.28, archive the plan, remove `plan/`, open the PR closing #323).

## README / docs changes needed at close

- `docs/articles/implementation-guide.md` → *Private (owner-scoped) keys*: lifecycle, the intersection, the oversight scope, the cleanup option, the owner claim.
- `README.md` API-key section, if it describes private keys.
- `plans/Toolkit/Platform/planned/05-v4-release.md`: remove the `Developer` role fallback.
- `Requests.md`: the owner-scoped keys entry (Done 2026-06-12) — add a note that the lifecycle gap is closed.
