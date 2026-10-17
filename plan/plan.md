# Plan: An unset access level grants nothing (4.0)

Branch: `feature/nullable-access-level` (from `master` at 91124c2; GitHub Actions flow).

## Steps

- [x] 0. NuGet updates: `dotnet outdated` on the whole solution found nothing outdated (2026-10-17), so there's no deps commit.
- [x] 1. The contract: `ITeamMember.AccessLevel` and `TeamMemberBase.AccessLevel` become `AccessLevel?` (Bson string and ignore-if-null). Rewrite `AccessLevelDefaultTests` to assert null.
  - `AccessLevelDefaultTests` and `AccessLevelCompletenessTests` now pin null, and a new test checks that a level-less member is stored without the field.
- [x] 2. The registry and privilege: `GetScopesForAccessLevel` and `GetEffectiveScopes` take `AccessLevel?` and return empty for null first. `AccessLevelPrivilege` treats null as the floor. Tests, including the `EveryAccessLevelIsAccountedFor` trip-wire.
  - `ITenantRoleService.GetEffectiveScopesAsync` takes `AccessLevel?` too and returns empty for null; custom roles don't rescue it either. `AccessLevelPrivilege`: null ranks below Custom, and a nullable `Clamp` overload was added. The trip-wire didn't fire, because the enum itself is unchanged.
- [x] 3. Compile-driven pass over every read site: grant resolver, `CallerTeamAccess`, the ordering sites (last-administrator count, `HasAccessLevel`), simulation, MCP, the API-key owner check, and the test fakes. Tests assert that each ordering site fails closed.
  - `TeamGrantResolver.ResolveFromMemberAsync` returns null for a level-less member, like a suspended one, so `TeamGrant.AccessLevel` stays non-null and the claims builder, MCP, the API-key owner check and `TeamContextResolver` need no change. The last-administrator count and `HasAccessLevel` use explicit patterns. In the grid, a null level shows "No access level", with a nullable dropdown and no clearing back to null. `ScopeReference.ToSelectableLevel(null)` returns null. The view models are nullable. No test fake relied on the old default of Owner.
  - `TeamComponent.HasAccessLevel` lives in razor with no bUnit, so it isn't unit-tested; the explicit pattern is the guard.
- [x] 4. Claims: omit the `AccessLevel` claim when null (`TeamMembershipClaimsBuilder`, `TeamClaimsAuthenticationStateProvider`). Tests.
  - The builder is covered through the resolver: no claims, and no fall-through to consent. The legacy `TeamClaimsAuthenticationStateProvider` emits nothing for a null level. The API-key owner refusal theory gained a `no-level` case. New `UnsetAccessLevelTests` has 11 cases. Suite: Blazor 1444, Service 1242, MongoDB 189, Mcp 116, Support 402, Email 189, Entra 58, Images 11, all green. (`dotnet test` reports zero tests on this SDK, so the test executables were run directly, per shared-instructions.)
- [x] 5. The invite boundary: `InviteUserModel.AccessLevel` becomes `AccessLevel?` and null is rejected. The post-`CreateTeamMember` guard (D1). Tests.
  - `TeamServiceBase.AddMemberAsync` throws `ArgumentException` before touching the store. `TeamServiceRepositoryBase.AddTeamMemberAsync` throws `InvalidOperationException` naming `<Type>.CreateTeamMember` when the override returns no level. The invite dialog's dropdown is nullable, still seeded with `User`. Three existing tests invited without a level and now name one. New tests: `Invite_WithNoLevel_IsRejected` and `CreateTeamMemberLevelGuardTests`.
- [x] 6. UI: the member grid shows "No access level" and the change-level action works from null. `AccessLevelCompletenessCheck` is reworded (D2).
  - The grid was done in step 3. The check's warning, its type docs, `CheckMemberAccessLevels` and the registration comment now say "no access until a level is set; before 4.0 Owner, so an original owner may be among them". Suite green: Service 1243, MongoDB 190, the rest unchanged.
- [x] 7. Removals: the `Developer` oversight fallback, the four-string `SendInviteAsync`, and, if D3, the obsolete `AddThargaPlatform` forwarding. Update the sample and tests.
  - `CanOversee` reads only the `apikeys:oversee` system scope. The tests now assert that Developer alone no longer oversees, and the owner-scope tests use the scope. The sample already maps the scope to Developer. `ITeamEmailSender` has one abstract `SendInviteAsync(TeamInviteMail)`; `InvitationMailSender`, the fakes and `TeamInviteMailTests` were updated (that file now has a one-member guard). Deleted: `ThargaPlatformOptions`, `ThargaPlatformRegistration` (Blazor), the vestigial `Tharga.Team.ThargaTeamRegistration` (core, also marked "removed in 4.0"), and `ThargaPlatformCompatibilityTests`.
  - Not touched: `SystemApiKeyView` still gates on the `Developer` role. That's a separate gate (system keys, not private-key oversight) and not in plan 05.
- [x] 8. Full test suite; commit; push for testing (no PR yet).
  - Blazor 1440, Service 1243, MongoDB 190, Support 402, Email 189, Mcp 116, Entra 58, Images 11: 3649 in all, none failed or skipped.

## Notes

- 2026-10-17 (user): plan confirmed. D1 throw on a level-less `CreateTeamMember`, D2 keep and reword the completeness check, D3 also remove the obsolete `AddThargaPlatform` forwarding. All as proposed.

## Last session

2026-10-17: all steps are done and pushed for testing. Next: the user tests in the sample (a member with no level is refused and shows "No access level"; an invite needs a level; Developer oversight comes only through the mapped scope). Then close-out: NuGet re-check, docs (*Migrating to 4.0*), version line `4.0`, plan 05 and the backlog, archive the plan, remove `plan/`, and open the PR.

## README / docs changes needed at close

- `docs/articles/implementation-guide.md`: a *Migrating to 4.0* section covering the null level, invites, `CreateTeamMember`, oversight, and `ITeamEmailSender`. Link *Members with no access level*, and update its "treated as Owner" wording.
- The private-keys section: drop "the `Developer` role still grants oversight until 4.0".
- README and the email docs, wherever they show the four-string `SendInviteAsync`.
- `.github/workflows/build.yml`: `MAJOR_MINOR: '4.0'`.
- Plan 05: mark item 7 and the item 6 removals shipped in 4.0, and retarget the rest. Backlog: close the entry. Requests.md: none known.
