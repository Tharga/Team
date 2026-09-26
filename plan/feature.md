# Feature: roster guards fail closed

Backlog (`Toolkit/Team.md` → *Bugs*): "The roster is read by reflection" and "Suspended administrators count as
cover for the last-administrator guard". Tier 1 under `mission.md` — both are authorization guards that do not
hold.

Branched from `origin/master` at `2144b84` (PR #303 merged). `dotnet outdated`: nothing to update.

## 1. `RemoveMemberAsync` skips its guards when the roster cannot be read

`RemoveMemberAsync` reads the roster through `GetMembersFromTeam`, which reflects a `Members` property and casts
it `as ITeamMember[]`. It gets `null` when the team type has no `Members` property, **or when it has one that is
not an array** (a `List<TMember>`, an `IReadOnlyList<TMember>`). On `null` it skips both guards and removes the
member anyway:

- **the Owner can be removed**, leaving a team only the `teams:set-owner` system scope can repair;
- an administrator can remove themselves as **the last administrator** of an ownerless team.

`LeaveTeamAsync` already shows the fix: read through `GetMembersAsync`, which is virtual so a store can answer it
properly, and refuse when the member is not on the roster.

**Fix:**

- `RemoveMemberAsync` reads `GetMembersAsync`. A member not on the roster is refused with the same "not a member"
  error `SetMemberSuspendedAsync` raises, so a roster the base cannot read fails closed rather than open.
- `GetMembersFromTeam` accepts any `IEnumerable<ITeamMember>`, so a store whose `Members` is a list stops
  reading as empty. That turns a would-be refusal into a working default for those stores.

## 2. Suspended administrators count as cover

`RequireNotLastAdministrator` counts members with `State == Member` and a level at or above Administrator, but not
`SuspendedAt`. A suspended administrator holds no scopes (`TeamGrantResolver` returns no grant for a suspended
member), yet satisfies "somebody still holds `member:manage`".

**Decided 2026-09-26 (user): suspension does not count.** Add `SuspendedAt == null` to the count.

## Not in scope

- Removing `GetMembersFromTeam` altogether. It still backs `GetMembersAsync`'s default for stores that do not
  override it.
- The UI's own gating (`TeamActionGate`). It does not count administrators; the service is the enforcement point.

## Acceptance criteria

- [ ] On a store whose team type has no `Members` property and that does not override `GetMembersAsync`, removing
      the Owner — or anyone — is refused, not performed.
- [ ] On a store whose `Members` is a `List<>`, the Owner guard and the last-administrator guard both apply.
- [ ] Removing an ordinary member on the built-in-shaped test store still works; removing an invited member still
      works.
- [ ] An administrator cannot remove or leave as the last administrator when the only other administrator is
      suspended.
- [ ] A suspended Owner — which cannot happen through the service, since suspending the Owner is refused — is not
      special-cased; the Owner never needs cover.
- [ ] Docs: `RemoveMemberAsync`'s behaviour on an unreadable roster is stated in the XML docs; the
      implementation guide mentions the fail-closed default for custom stores if it covers removal.
- [ ] Full suite green.

## Done condition

Criteria met; both backlog entries removed; `plan/` removed in the close-out commit; PR open.

## Version

**No `MAJOR_MINOR` bump proposed.** Both changes refuse operations that were only ever reachable through a defect:
removing an Owner, and leaving the last *working* administrator. A host whose store could not be read now gets an
error on remove instead of an unguarded removal — the same trade `LeaveTeamAsync` already made.
