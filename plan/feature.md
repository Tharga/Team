# Feature: team reads honour consent and custom roles

**Issue:** [Tharga/Team#248](https://github.com/Tharga/Team/issues/248)
**Branch:** `feature/team-read-honours-consent` (from `master`)
**Target release:** 3.18 (`MAJOR_MINOR` in `build.yml`)
**New published packages:** none
**Public API change:** none intended — `TeamGrantResolver` is `internal`

## Goal

A caller who reaches a team through **consent** rather than membership can read that team. Today the write
half of an operation is permitted and the read half is refused, so `TeamComponent.AddUser` creates the
invited member and then reports "Access denied".

## The defect

Two enforcement paths compute "may this caller read this team?" and they have drifted.

| Path | Where | Reads |
|---|---|---|
| Mutations | `AuthorizationTeamServiceDecorator` → `TeamAuthorizer` | the caller's **claims** |
| Reads | `TeamManagementService.GrantsTeamRead` | the caller's **member row**, recomputed |

Claims already carry consent-derived scopes, because `TeamServerClaimsTransformation` builds them through
`TeamGrantResolver`, which handles both the member branch and the consent branch. `GrantsTeamRead` handles
only the member branch, so:

1. **Consent-based access is invisible to reads.** `_inner.GetTeamMemberAsync` returns null for a non-member,
   `GrantsTeamRead(null)` returns `false`, and the read is refused. This is #248.
2. **Per-team custom roles are invisible to reads.** `GrantsTeamRead` calls
   `IScopeRegistry.GetEffectiveScopes` directly, where `TeamGrantResolver` prefers
   `ITenantRoleService.GetEffectiveScopesAsync`. A tenant role granting `team:read` is honoured when claims
   are built and ignored when a read is gated. Not reported; same root cause.
3. **Suspension is invisible to reads, and this one grants access rather than refusing it.** `GrantsTeamRead`
   never looks at `SuspendedAt`, where `TeamGrantResolver` refuses a suspended member outright. A suspended
   Owner therefore kept full read access to the team's details and roster. Found while proving the
   reproduction actually fails against the old code — not reported by anyone, which is what you would expect
   of a defect whose symptom is that nothing goes wrong for the person experiencing it.

`TeamGrantResolver` is documented as *"the single copy of that rule"* and its remarks already name this
failure mode: *"the toolkit has already paid for that shape once: the `team:read` hole existed because two
enforcement paths each carried their own copy and drifted apart."* `GrantsTeamRead` is the third copy.

## Scope

- Move `TeamGrantResolver` from `Tharga.Team.Service` into `Tharga.Team`, so the package that gates reads can
  reach it. It depends only on `ITeamService`, `IScopeRegistry` and `ITenantRoleService`, all of which are
  already in `Tharga.Team`. It is `internal`, so this is not a public API change.
- Rewire `TeamManagementService.RequireTeamReadAsync` **and** the `GetTeamsAsync<T>()` per-team filter onto
  the resolver, and delete the `GrantsTeamRead` pair.
- Pin the result with an architecture test, so a fourth copy cannot appear quietly.
- Bundle the mandated package updates: xunit.v3 3.2.2 → 4.0.0 (the Microsoft.Testing.Platform migration) and
  SkiaSharp 4.151.1 → 4.151.2.

## Explicitly not in scope

- **A pre-flight check in `AddUser`.** The issue offers this as one of two readings ("`AddUser` should refuse
  before writing rather than after"). It is the wrong one: the read was refused in error, so the fix is to
  stop refusing it, not to refuse the write as well.
- **Anything about `UserAuthenticatedId` being absent from the exception.** The issue notes this may be #163's
  missing ambient actor. Separate defect, separate issue.
- **The invitation-token redesign** — that is #249.

## Why this moves towards architecture v4

Rule 2, *one enforcement point*: "invariants and authorization live in the domain and nowhere else." This
deletes an enforcement path rather than adding one. Rule 6, *claims carry their provenance*: the resolver
distinguishes a member grant from a consent grant (`IsMember`), which a recomputation from the member row
cannot.

It also repeats a move this codebase has already made for the same reason. `ConsentOptions` says so in its
own remarks: *"It lived under `Tharga.Team.Blazor.Framework` until the MCP surface needed it too and could
not reach it, which briefly left the same policy configured in two places."*

## Acceptance criteria

1. A caller who is not a member but holds a role the team consented to can call `GetTeamAsync`,
   `GetTeamByKeyAsync`, `GetMembersAsync`, `GetTeamMemberAsync` and `GetTeamCustomRolesAsync` on that team.
2. The consent access level is honoured: a team consenting at `Viewer` grants `team:read`; a team that has
   consented to nothing still refuses.
3. `TeamComponent.AddUser` completes without throwing for a consent-based operator, and the invited member is
   created exactly once.
4. A member whose **tenant role** grants `team:read` can read the team, where today they cannot.
5. A **suspended** member is refused — including one who would otherwise be reached by consent. This is a
   behaviour *change*, not a preservation: today a suspended member can still read.
6. A caller with no membership and no consented role is still refused, with the message unchanged.
7. `GetTeamsAsync<T>()` filtering agrees with `RequireTeamReadAsync` — a team readable by one is readable by
   the other.
8. An architecture test fails if any type other than `TeamGrantResolver` computes effective team scopes.
9. Full suite green on xunit.v3 4.0.0, with a **non-zero test count** read from the run, not inferred from
   the exit code.

## Done condition

All nine criteria met, `docs/` and `README.md` reviewed, #248 commented and closed with the evidence, and the
backlog and `Requests.md` swept.
