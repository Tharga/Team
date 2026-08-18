# Plan: system-scoped owner change (#225)

Branch `feature/system-owner-change`, from `master` at `3.13.0`.

## Status

**In progress.** Steps 0, 2, 3, 4 and most of 6 are done; build clean, 2025 tests green.

Revised twice on 2026-08-18 by user decision. Final shape: **one operation** (`SetOwnerAsync`;
`AssignOwnerAsync` **removed outright**, not obsoleted) under **one renamed scope**
(`teams:assign-owner` → `teams:set-owner`), enforcing **a team has exactly one owner**. The `teams:purge`
brace defect is folded in and shipped. xunit 4.0 was attempted and **backed out** — see step 0.

Still to do: authorization tests (4b), audit tests (5b/5c), scope-satisfiability and retired-scope tests
(2c/2d), UI labelling for the three owner-count states (6b/6c), docs (step 8).

---

## Step 0 — Package updates (leading step, mandatory)

- [x] **0a. BACKED OUT.** Bumped `xunit.v3` 3.2.2 → **4.0.0** and `xunit.runner.visualstudio` 3.1.5 → **4.0.0** in all
      seven test projects: `Tharga.Team.Blazor.Tests`, `Tharga.Team.Service.Tests`, `Tharga.Team.Mcp.Tests`,
      `Tharga.Team.MongoDB.Tests`, `Tharga.Team.Entra.Tests`, `Tharga.Team.Images.Tests`,
      `Tharga.Team.Support.Tests`.
      **This is a major bump** — flagged and approved before starting. No shipping package has an update;
      all eleven are current.
- [x] **0b. Done, then reverted.** `dotnet build -c Release`, then `dotnet test -c Release`. Full suite green (974 at branch
      point) **before any feature code is written**.
- [x] **0c. Not committed — reverted instead.** xunit.v3 4.0 moves to Microsoft.Testing.Platform, which
      drops the VSTest target on the .NET 10 SDK: the solution builds and **all 2006 tests pass** when each
      assembly is run directly, but `dotnet test` fails for every project and CI's
      `--collect:"XPlat Code Coverage"` flags need rewriting. It is a test-platform migration, not a package
      bump. Filed in the backlog under *Build & test tooling*; shipping packages were all current anyway.

> If xunit 4.0 breaks enough tests to become its own project, stop and report rather than absorbing it into
> this feature silently.

## Step 1 — The rules, pure and tested first

- [x] **1a.** `Tharga.Team/TeamOwnership.cs` — add `CanSetOwner(members, candidateUserKey)` (candidate must
      be an existing member; unlike `CanAssign`, an existing owner does **not** disqualify) and
      `OwnersToDemote(members, newOwnerUserKey)` returning every member at `Owner` except the incoming one.
      Keep both pure and static, for the reason the type's own remarks already give.
      **Rewrite the type-level remarks** — they currently frame ownerless-ness as the only non-takeover
      state, which stops being true here.
- [x] **1b.** `Tharga.Team.Service.Tests/TeamOwnershipTests.cs` — extend. Cases: several owners incl. the
      candidate; several owners excl. the candidate; single owner ≠ candidate; candidate already sole owner
      (empty demote set); ownerless; candidate not a member; empty roster; null members; null/empty key.
      Keep the existing `CanAssign` tests untouched and passing.

## Step 2 — The scope (widen, do not add)

- [x] **2a.** `Tharga.Team/SystemTeamScopes.cs` — **rewrite** `AssignOwner`'s XML docs. The refusal-based
      anti-takeover argument is now false and must go, replaced by what the grant actually authorizes:
      make any existing member the sole owner of any team, whatever its current owner count. Keep the
      constant string `teams:assign-owner` — renaming it would break every host's role mapping.
- [x] **2b.** `ThargaBlazorRegistration.cs:180` — update the registration *description* string to match.
      No new scope to register.
- [ ] **2c.** Test that the scope is *satisfiable* end to end, not merely present — registered in the
      **system** registry via `AddThargaSystemScopes` and read by `HasSystemScopeAsync`. The closed
      `mcp:discover` request was exactly this mistake: registered in the team registry, checked as a system
      scope, so nobody could ever satisfy it.
- [ ] **2d.** Test that an **in-team** claim named `teams:assign-owner` does **not** satisfy it.

## Step 3 — The operation

- [x] **3a.** `Tharga.Team/ITeamService.cs` — declare `SetOwnerAsync<TMember>` on the internal contract
      (`[EditorBrowsable(Never)]` type; the contract a host implements).
- [x] **3b.** `Tharga.Team/ITeamManagementService.cs` — declare it with
      `[RequireScope(SystemTeamScopes.AssignOwner)]`, mirroring `AssignOwnerAsync` at `:104`. The proxy fails
      closed on an unattributed method, so this is what makes it gated.
- [x] **3c.** `Tharga.Team/TeamManagementService.cs` — forward it.
- [x] **3d.** `Tharga.Team/TeamServiceBase.cs` — implement:
      read the team; refuse a non-member candidate by name; if the candidate is already sole owner return
      **without writing anything**; otherwise **promote the new owner first**, then demote each
      `OwnersToDemote` entry to `Administrator` — that order is what keeps the team from being ownerless at
      any point; drop the member cache for every changed member; raise `TeamsListChangedEvent`.
      Use the protected `SetTeamMemberRoleAsync`, as transfer and assign both do — `SetMemberRoleAsync`'s
      Owner guard stays untouched.
- [x] **3e.** Return the set of demoted members (or expose it to the audit decorator) so the audit entry can
      name them rather than recording only the promotion.
- [x] **3f. Changed by user decision: removed, not obsoleted.** `AssignOwnerAsync` is gone from both
      interfaces, the base class, both decorators and the UI. Original wording kept below for the record.
      ~~Reimplement as a forwarder, mark
      `[Obsolete("Use SetOwnerAsync, which also handles a team that already has an owner. Removed in 4.0.")]`
      on both interfaces. Its existing tests must pass **unchanged** against the new implementation — that is
      the evidence existing callers are unaffected.
      **One deliberate behaviour change to state in the notes:** `AssignOwnerAsync` no longer throws on a
      healthy team. Nothing in the toolkit relied on that throw; the UI only ever offered the action on
      ownerless teams.

## Step 4 — Authorization

- [x] **4a.** `Tharga.Team.Service/AuthorizationTeamServiceDecorator.cs` — gate `SetOwnerAsync` on
      `HasSystemScopeAsync(SystemTeamScopes.AssignOwner)`, mirroring `AssignOwnerAsync` at `:102`.
      **System grant only, no in-team fallback** — two reasons now, and the second is new: no in-team caller
      can exist on an ownerless team, *and* the in-team caller who should change a healthy team's owner is
      the owner, who already has `TransferOwnershipAsync`. An in-team fallback would let an Administrator
      depose the owner, which `SetMemberRoleAsync` exists to refuse.
- [ ] **4b.** Tests: refused with no scope, refused with an in-team claim of the same name, allowed with the
      system scope.

## Step 5 — Audit

- [x] **5a.** `Tharga.Team.Service/Audit/AuditingTeamServiceDecorator.cs` — log `set-owner` on success and
      failure, mirroring `assign-owner` at `:484`. Metadata: team key, new owner, every demoted owner key.
- [ ] **5b.** Test the entry carries the demoted owners — the part most likely to be dropped, because the
      operation "works" without it.
- [ ] **5c.** Test the **no-op writes no entry**. An audit log that records "ownership changed" on every pass
      of a sync that changed nothing is worse than silence.

## Step 6 — UI

- [x] **6a.** `Tharga.Team.Blazor/Features/User/UserAdminGate.cs` — extend `CanAssignOwner` at `:97` to allow
      the action on a team that already has an owner. Same scope, wider precondition.
- [ ] **6b.** `Tharga.Team.Blazor/Features/User/TeamsListView.razor` — **two affordances over one
      operation**: *Reduce to a single owner* when the team has several owners, *Change owner* when it has
      one, *Assign owner* when it has none. Resolve the scope with `TeamScopeGate.HasSystemScope`, never a
      bare `HasClaim`, per the rule at `:253`.
- [ ] **6c.** Confirm dialog names **who will be demoted**, by name — this is the one operation where the
      operator can silently strip several people of ownership. **Cancel rightmost**, using the shared
      `CancelButton` component. Do not hand-roll it — every drift found so far came from a hand-rolled button.
- [ ] **6d.** New strings go through the text catalogue, not hardcoded — `TextCoverageTests` counts may only
      go down.

## Step 7 — Verify

- [ ] **7a.** `dotnet build -c Release` and `dotnet test -c Release`, full suite green.
- [ ] **7b.** Re-read the acceptance criteria in `feature.md` one by one against the code.

## Step 8 — Docs

Three places assert the safety property being removed. **Rewrite, do not amend** — a doc claiming a guard the
code no longer has is worse than no doc.

- [ ] **8a.** `docs/articles/user-management.md:267+` — the `### After — teams:assign-owner` section. Delete
      *"an attempt to 'repair' a team that is not broken is what taking one over would look like"* and the
      two-condition table's ownerless row. Replace with the four cases from `feature.md`.
- [ ] **8b.** `docs/articles/implementation-guide.md:1719` — the scope table row still says *"Refused when the
      team already has one."*
- [ ] **8c.** `README.md` where it covers scopes.
- [ ] **8d.** Document the three ownership operations and who each is for: the owner transfers
      (`TransferOwnershipAsync`, no scope, in-team); an operator sets (`SetOwnerAsync`, `teams:assign-owner`,
      system); `AssignOwnerAsync` is obsolete and forwards.
- [ ] **8e.** Land as its own `docs:` commit before close-out.

## Step 9 — Close-out (only when the user says it is done)

- [ ] **9a.** Re-run `dotnet list package --outdated`; apply anything new in this PR.
- [ ] **9b.** `Requests.md` — no entry exists for #225; add one under `## Tharga.Team` marked Done with
      evidence, so the record exists.
- [ ] **9c.** Backlog `Toolkit/Team.md` — update the Roadmap sweep note; #225 is no longer a live gap.
- [ ] **9d.** Comment on issue #225 with what shipped and what the reporter can now delete — specifically
      the direct entity write and their duplicated ownerless-team invariant — then close it.
      **Nothing further**: no follow-up entry, no adoption tracking. They are an external consumer.
- [ ] **9e.** Archive `plan/feature.md` to `$DOC_ROOT/Tharga/plans/Toolkit/Platform/done/system-owner-change.md`.
- [ ] **9f.** `git rm -r plan`, commit `feat: system-owner-change complete`, push, open PR.

## Notes

- **Version line:** this adds public API, so `MAJOR_MINOR` in `build.yml` needs bumping to `3.14` **in this
  PR** — nothing in CI does it, and the planned/README warns that a version-line-only PR queues a
  content-free release.
- **PR description is the release notes** (mission override) — write it for package consumers. It must say
  **"`teams:assign-owner` now authorizes more than it did"** in those words, not present this as a new
  capability. Any host that has already granted the scope gains the ability to depose a sitting owner on
  upgrade, without doing anything. Practical exposure is near zero (scope shipped 3.9.0, 2026-08-01;
  consumers still on 3.8.x) — but a widened grant is a widened grant, and the notes are the only place a host
  will see it.
- **Also mention** that `AssignOwnerAsync` is obsolete but working, that it no longer throws on a healthy
  team, and that no host action is required.
