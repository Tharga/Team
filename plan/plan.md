# Plan: An unset access level grants nothing (4.0)

Branch: `feature/nullable-access-level` (from `master` at 91124c2; GitHub Actions flow).

## Steps

- [x] 0. NuGet updates: `dotnet outdated` on the whole solution found nothing outdated (2026-10-17), so there's no deps commit.
- [~] 1. The contract: `ITeamMember.AccessLevel` and `TeamMemberBase.AccessLevel` become `AccessLevel?` (Bson string and ignore-if-null). Rewrite `AccessLevelDefaultTests` to assert null.
- [ ] 2. The registry and privilege: `GetScopesForAccessLevel` and `GetEffectiveScopes` take `AccessLevel?` and return empty for null first. `AccessLevelPrivilege` treats null as the floor. Tests, including the `EveryAccessLevelIsAccountedFor` trip-wire.
- [ ] 3. Compile-driven pass over every read site: grant resolver, `CallerTeamAccess`, the ordering sites (last-administrator count, `HasAccessLevel`), simulation, MCP, the API-key owner check, and the test fakes. Tests assert that each ordering site fails closed.
- [ ] 4. Claims: omit the `AccessLevel` claim when null (`TeamMembershipClaimsBuilder`, `TeamClaimsAuthenticationStateProvider`). Tests.
- [ ] 5. The invite boundary: `InviteUserModel.AccessLevel` becomes `AccessLevel?` and null is rejected. The post-`CreateTeamMember` guard (D1). Tests.
- [ ] 6. UI: the member grid shows "No access level" and the change-level action works from null. `AccessLevelCompletenessCheck` is reworded (D2).
- [ ] 7. Removals: the `Developer` oversight fallback, the four-string `SendInviteAsync`, and, if D3, the obsolete `AddThargaPlatform` forwarding. Update the sample and tests.
- [ ] 8. Full test suite; commit; push for testing (no PR yet).

## Notes

- 2026-10-17 (user): plan confirmed. D1 throw on a level-less `CreateTeamMember`, D2 keep and reword the completeness check, D3 also remove the obsolete `AddThargaPlatform` forwarding. All as proposed.

## README / docs changes needed at close

- `docs/articles/implementation-guide.md`: a *Migrating to 4.0* section covering the null level, invites, `CreateTeamMember`, oversight, and `ITeamEmailSender`. Link *Members with no access level*, and update its "treated as Owner" wording.
- The private-keys section: drop "the `Developer` role still grants oversight until 4.0".
- README and the email docs, wherever they show the four-string `SendInviteAsync`.
- `.github/workflows/build.yml`: `MAJOR_MINOR: '4.0'`.
- Plan 05: mark item 7 and the item 6 removals shipped in 4.0, and retarget the rest. Backlog: close the entry. Requests.md: none known.
