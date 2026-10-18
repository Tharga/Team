# Feature: Member edits in the MongoDB store write only what changes

Source: [Tharga/Team#338](https://github.com/Tharga/Team/issues/338) (bug). Priority tier 1: an overlapping edit
can put back a removed member or undo a suspension, which is access the toolkit should not grant.

## Goal

Two overlapping writes to the members of one team both survive. No member write in `TeamRepository` reads the
team and writes the member list back.

## Today

`TeamRepository` (Tharga.Team.MongoDB) has nine member writes that read the team, change one member in memory,
and `$set` the whole `Members` array:

- `RemoveMemberAsync`, `RemoveMemberFromAllTeamsAsync`
- `SetMemberRoleAsync`, `SetMemberSuspendedAsync`, `SetMemberTenantRolesAsync`, `SetMemberScopeOverridesAsync`,
  `SetMemberNameAsync`
- `SetInvitationExpiryAsync`, `SetInvitationResponseAsync`

The write carries every other member as it was when read, so any change saved in between is overwritten. Already
atomic: `AddMemberAsync` (`$addToSet`), `SetLastSeenAsync` (#317), and the access-request writes.

## Scope

1. **Field edits become positional updates**, filtered on the team key and `Members $elemMatch` (member key, or
   invite key for the two invitation writes), sent with `OneOption.FirstOrDefault` so the filter travels with
   the update (the access-request writes and #317 already do this).
   - A field marked `[BsonIgnoreIfNull]` (`AccessLevel`, `TenantRoles`, `ScopeOverrides`, `SuspendedAt`,
     `SuspendedBy`) is `$unset` when cleared, not set to null. Whole-member writes left it absent, and the
     unset-access-level detection relies on `$exists: false`.
   - `SetInvitationResponseAsync` still returns the team after the change, or null when no invitation matched.
2. **Removals become `$pull`** of the member's rows, which matches today's behaviour of dropping every row with
   that key. `RemoveMemberFromAllTeamsAsync` keeps its count of teams touched.
3. Docs: remarks on the writes; guidance on `ITeamRepository` for a host's own repository.

## Behaviour that changes on purpose

- A write to a team that does not exist is a no-op, where several threw a null reference.
- The duplicate-row warning these writes logged (`PickOneOrDefault`, #64) goes from these paths, since nothing is
  read. Duplicates are still edited the same way: a field edit changes the first match, a removal drops all.

## Out of scope

- **Check-then-act guards in `TeamServiceBase`**, such as "never remove or demote the last owner". The service
  reads the team, checks, then writes, so two overlapping demotions of two owners can both pass. Atomic writes do
  not close that; it needs conditional updates. Worth its own issue.
- Custom roles (`SetCustomRolesAsync`) are written as a whole set by design (the editor saves the set).
- Stores other than Tharga.Team.MongoDB.

## Acceptance criteria

- None of the nine writes reads the team before writing; each sends one update touching only that member's
  fields, or removing that member.
- Two overlapping edits to different members of one team both survive.
- Duplicate rows behave as today.
- Unit tests render each filter and update and require `FirstOrDefault`; every update is also run once against a
  real MongoDB server.
