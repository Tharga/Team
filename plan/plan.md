# Plan: Only the Owner may delete a team

Branch `feature/owner-only-delete`, from `master` at e4d109f.

## Steps

- [x] 1. NuGet package sweep (mandatory, before any feature code).
      `dotnet outdated Tharga.Team.sln` — **no outdated dependencies**. Nothing to apply.

- [x] 2. `TeamAuthorizer.IsOwnerOfAsync(string teamKey)` — true when the `TeamKey` claim equals the target
      **and** the `AccessLevel` claim parses to `Owner`. An unparseable or absent level is false, so it
      fails closed. XML docs state why both halves are needed: the access level is emitted for the
      selected team, so without the team binding it would authorize the wrong team.

- [x] 3. `AuthorizationTeamServiceDecorator.RequireDeleteAsync` — swap the `team:manage` check for
      `IsOwnerOfAsync`, keeping the `AllowTeamCreation` conjunction and the `teams:delete` early return
      exactly as they are. Rewrite the refusal message to name the Owner requirement. Update the
      class-level authorization list.

- [x] 4. XML docs on `ITeamManagementService.DeleteTeamAsync`, which currently states the old rule.

- [x] 5. `MAJOR_MINOR` 3.20 → 3.21 in `.github/workflows/build.yml`. An administrator who can delete today
      cannot afterwards — a consumer has to act, which is the project's bar for a bump.

- [x] 6. Tests: Owner deletes; Administrator refused; bare `team:manage` refused; `teams:delete` still
      deletes as a non-member and with `AllowTeamCreation` off; Owner of A cannot delete B; Owner refused
      with `AllowTeamCreation` off; restore mirrors all of it.

- [x] 7. Full suite green, `dotnet outdated` re-checked, README and docs reviewed, backlog item closed.

## Notes

- **The UI needs no change**, and that is the evidence the direction is right: `TeamActionGate.CanDelete`
  has required the Owner all along and its tests stay green untouched. The service is what moves to meet it.
- Restore shares `RequireDeleteAsync` deliberately and keeps doing so.
- API keys carry an `AccessLevel` claim too (`ApiKeyAuthenticationHandler`), so a key configured at `Owner`
  can delete and one at `Administrator` no longer can. That is the same tightening, applied consistently.

## Last session

2026-09-06 — All seven steps done. Build clean, full suite **2543 passed, 0 failed** (2539 before).
`dotnet outdated` re-checked at the end: nothing outstanding.

**The Blazor gate tests passed untouched**, which is the evidence the direction was right —
`TeamActionGate.CanDelete` has required the Owner since it was written, and only the service moved.

Docs: a new *Deleting a team is an Owner act* section in the implementation guide with the caller table,
the team-operation authorization row, the `team:manage` scope row (delete removed from what it grants),
the selected-team paragraph, and a pointer in `user-management.md` saying `teams:delete` is now the only
way to delete a team you do not own.

`MAJOR_MINOR` 3.20 → 3.21: an administrator who can delete today cannot afterwards, which is a consumer
action and therefore a bump rather than a patch.

**Not yet done:** branch unpushed, no PR, backlog item not yet closed — awaiting the user's testing.
