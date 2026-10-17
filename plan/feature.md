# Feature: An unset access level grants nothing (4.0)

Source: plan 05 (`plans/Toolkit/Platform/planned/05-v4-release.md`) item 7, plus two small removals from item 6.
Backlog: *`default(AccessLevel)` is `Owner`* (Critical).

## Goal

A team member whose access level was never set is denied instead of being treated as `Owner`. This ships as
4.0.0, on its own. The rest of plan 05 becomes a later major.

## Today

`AccessLevel` is `Owner=0, Administrator, User, Viewer, Custom`, so its zero value is `Owner`. A stored member
document with no `AccessLevel` field, or a host's `CreateTeamMember` that forgets to copy the invite's level,
silently produces an Owner, with no error and no log. `AccessLevelCompletenessCheck` (3.14.x) only warns.

## Scope

1. **The contract.** `ITeamMember.AccessLevel` and `Tharga.Team.MongoDB.TeamMemberBase.AccessLevel` become
   `AccessLevel?`. The Mongo property gets `[BsonRepresentation(String)]` and `[BsonIgnoreIfNull]`, like `State`.
2. **The registry.** `IScopeRegistry.GetScopesForAccessLevel` and `GetEffectiveScopes` take `AccessLevel?` and
   return empty for null, explicitly, as their first statement.
3. **The ordering sites** (`ScopeRegistry`, `TeamServiceBase`'s last-administrator count,
   `TeamComponent.HasAccessLevel`) fail closed for null. This is asserted in tests, not left to lifted operators.
4. **The claims pipeline.** No `AccessLevel` claim is emitted when the level is null. Today it would be an empty one.
5. **The invite boundary.** `InviteUserModel.AccessLevel` becomes `AccessLevel?`, and an invite with no level is
   rejected with an exception.
6. **`AccessLevelPrivilege`.** `Rank`, `IsNoMorePrivilegedThan` and `Clamp` handle null, with null as the floor.
7. **Every other read site** that the type change breaks: grant resolution, `CallerTeamAccess`, simulation, MCP,
   API-key ownership, and the UI. A null level shows as "No access level" in the member grid, and an
   administrator can set one.
8. **Removals from item 6:**
   - The `Developer` role fallback for private API key oversight: only `apikeys:oversee` grants it.
   - The four-string `ITeamEmailSender.SendInviteAsync`: the `TeamInviteMail` overload becomes the only,
     abstract member.
9. **The version line goes to 4.0.** Docs get a migration section, which the PR description links.

## Decisions (confirmed 2026-10-17)

- **D1: a host's `CreateTeamMember` that returns no level throws.** `TeamServiceRepositoryBase` checks the
  member after the override returns, and throws an `InvalidOperationException` naming the override. Otherwise
  the invite "succeeds" and produces a member who can do nothing, which is safe but silent.
- **D2: keep `AccessLevelCompletenessCheck`, reworded.** Its warning changes from "these members are treated
  as Owner" to "these members have no access until a level is set", and still links the back-fill.
- **D3: also delete the obsolete `AddThargaPlatform` / `ThargaPlatformOptions` forwarding** (the rest of item 6).
  It's another small 4.0-only removal, and doing it now saves a breaking change later. This one is optional.

## Out of scope

- The rest of plan 05 (items 1–5): contracts and ports, Mongo out of Service, the composition root, the
  component audit, and MCP enforcement. They stay in plan 05 for a later major.
- `ConsentOptions.AccessLevel`, `RequireAccessLevelAttribute.MinimumLevel`, `ConsentAccessLevel`, and
  `ApiKeyEntity.AccessLevel` (already nullable).
- A v4 preview channel. CI publishes only from master, so this ships as 4.0.0 directly.

## Acceptance criteria

- `ITeamMember.AccessLevel` is `AccessLevel?`. A member document with no stored level deserializes as `null`.
  `AccessLevelDefaultTests` is rewritten to assert this.
- `GetScopesForAccessLevel(null)` returns empty. The ordering sites and `AccessLevelPrivilege` are asserted to
  fail closed.
- No `AccessLevel` claim is emitted for a null level.
- An invite with no level is rejected, and so is a `CreateTeamMember` that returns none (D1).
- A member with no level appears in the member grid and can be given one.
- Oversight is granted only by `apikeys:oversee`. `ITeamEmailSender` has one abstract `SendInviteAsync(TeamInviteMail)`.
- No capability is lost apart from the deliberate removals. The full suite passes.

## Done condition

All acceptance criteria are met and the full suite passes. The docs carry a 4.0 migration section that links
*Members with no access level*. The version line is `4.0`. Plan 05 has item 7 and the removals marked shipped,
and is retargeted to the next major. The backlog entry is closed. The PR description is the release notes.
