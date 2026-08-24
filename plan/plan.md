# Plan: support cases, site-only (#142 phase 2) — slice 1

Feature scope: `plan/feature.md`. Conventional-commit prefix for this branch: **`feat:`**.

## Steps

- [ ] **0. Package updates — HELD.** xunit 4.0 pair only, per the standing decision.

- [x] **1. Shape questions — SETTLED 2026-08-24. Answers below; step 2 may start.**
      - **a. Case identity: `string`**, matching the house style (`TeamKey`, `MemberKey`, `UserKey`) and
        `TeamServiceRepositoryBase`'s `Guid.NewGuid().ToString()` for member keys. Quotable in a URL and in a
        Slack message later without conversion.
      - **b. Owner: the team.** `TeamKey` owns the case; author recorded twice and allowed to dangle. See
        `feature.md`.
      - **c. Soft-deleted team: cases are NOT readable.** Follows the team's own rule — every team read
        filters `DeletedAt == null`, and a case list still answering after the team is gone would contradict
        soft delete everywhere else in this codebase. Restore brings them back with the team.
      - **d. Purge reaches the cases via option A (user, 2026-08-24):** purge calls `ISupportCaseStore`
        directly, which is legitimate because the port lives in `Tharga.Team` and the core therefore already
        knows the concept. **Resolved from the service provider, never from a new
        `TeamServiceRepositoryBase` constructor parameter** — that pattern already silently disables a
        feature when a subclass forgets to forward it, and a silently-skipped purge is far worse than a
        missing icon.
        **Ordering: delete the cases first, then the team record.** Same reasoning `DeleteTeamAsync` already
        documents for the record-then-drop order — the two writes cannot be atomic, so choose the failure.
        Cases-first fails to *leftover team with no cases*, which is visible and re-purgeable. Record-first
        fails to *orphaned cases belonging to a team that no longer exists*, which nothing can find or clean
        up. Write this reasoning next to the code.

- [x] **2. Contracts and the port — DONE 2026-08-24.** Eight records/enums plus `ISupportCaseStore`, all in
      `Tharga.Team`. `SupportContractShapeTests` (6 tests) guards rules 3 and 4 and **self-checks that it
      found the surface**, so it cannot pass while scanning nothing.
      Two shape decisions worth knowing, both made while writing it:
      - **`SupportCase` does not carry its transcript.** A case can accumulate years of messages, so the
        record is a header with a `MessageCount`, and history is a separate paged read. Embedding is a
        *storage* choice (step 3); it must not leak into the contract.
      - **The cursor is `SupportMessage.Sequence`**, so paging stays stable while a conversation is still
        being appended to — a new reply cannot shift entries the reader already passed. An offset would.
      Also: `AddCaseAsync(case, firstMessage)` and `CloseCaseAsync(…, closureMessage)` already express step
      3's atomicity requirement in the port's signature, which is where it belongs.

- [ ] **3. Decide and encode atomicity (rule 5) — do not skip because it looks trivial.**
      Raising a case creates a case *and* its first message. Closing sets a status *and* appends a system
      message. If those are two writes, the port must be able to express one unit, or a crash leaves a case
      with no message and the model's central invariant ("a case always has at least one message") is a lie.
      Preferred: shape the port so each operation is **one document write** — messages embedded in the case
      document, as members are embedded in a team today. That makes atomicity free rather than transactional,
      and it is the same trick `TeamEntityBase.Members` already uses.
      **If embedding is chosen, say what bounds it**: a case with thousands of messages is a growing
      document. Record the limit and what happens at it.

- [ ] **4. The Mongo adapter, in `Tharga.Team.MongoDB`.**
      `SupportCaseEntity` + `MongoSupportCaseStore`. Enum persisted **by name**
      (`[BsonRepresentation(BsonType.String)]`) — `shared-instructions.md` is explicit, and this repo has
      already been bitten: `SupportCaseStatus` must not be stored as an ordinal. Add it to the existing
      persisted-enum sweep test rather than asserting it locally.
      Registration follows `AddThargaTeamRepository`'s existing shape, conditional on the host opting in.

- [ ] **5. Operations and the single enforcement point, in `Tharga.Team.Support`.**
      `ISupportCaseService` with `RaiseCaseAsync` / `ReplyToCaseAsync` / `CloseCaseAsync` / `GetCaseAsync` /
      `GetCasesAsync` / `GetMyCasesAsync`, the implementation, and **one** authorization decorator
      implementing the table in `feature.md`. Register `support:read` and `support:manage` as team scopes.
      **Two things to get right:**
      - The author-or-`support:manage` checks are the interesting ones; a member must not be able to reply
        to someone else's case by guessing an id.
      - Reading a case must confirm the case's `TeamKey` matches the caller's team. An id alone must never
        be sufficient — that is the cross-tenant hole this shape invites.

- [ ] **6. Audit every operation.**
      Mirror `AuditingTeamServiceDecorator`: actor, case id, and what changed, via `AuditMetadataKeys`.
      Raise, reply and close are three distinct facts.

- [ ] **7. Tests.**
      Beyond the guards in step 2: the zero-binding case is complete and readable; paging returns a stable
      cursor and does not skip or duplicate on append; a non-member is refused; a member without
      `support:read` cannot read another's case; the author can; `support:manage` can. **A cross-tenant
      attempt using a valid case id from another team is refused** — that test is the one worth writing
      first.
      **Plus the lifetime tests, which are the requirement rather than edge cases:** deleting the author
      leaves the case and its history intact and readable with the name still rendering; removing the author
      from the team does the same; and a case whose author no longer exists is reachable through
      `support:read` while satisfying no author check.

- [ ] **8. Full test suite.** Green before any commit.

- [ ] **9. Docs.**
      A new `docs/articles/support-cases.md` — the docs follow one-file-per-feature-area, and this is a new
      area rather than a change to an existing one. Cover the model, the two scopes, what is *not* included
      (no Slack/AI/Jira/UI yet), and that a case can exist with no channel binding.
      Update the `Tharga.Team.Support` README, which currently describes notifications only.
      Separate `docs:` commit.

- [ ] **10. Close the records.**
      - `Requests.md` — no Team row for this; do **not** invent one.
      - Backlog — nothing to close; this comes from the issue and the plan directory.
      - **Plan 04** — mark phase 2 slice 1 delivered *in the spec*, and say plainly that the widget remains.
        A spec still reading as unstarted is what makes someone re-plan it.
      - **Issue #142 stays OPEN** — phases 3–5 and the widget are unbuilt. Comment with what shipped; do not
        close, and do not use a `Fixes #142` keyword in the PR, which would close it on merge.

- [ ] **11. Close out.** Archive `feature.md` to `$DOC_ROOT/…/done/`, `git rm -r plan`, final commit
      `feat: support cases slice 1 complete`, push, PR. Do not merge locally.
      **Check `MAJOR_MINOR`** — this adds public API, so the version line likely needs bumping to 3.15 in the
      same PR. Nothing in CI does it automatically.

## Notes and decisions

- **2026-08-24** — Branch created from `master` at `e5afe31` (post-#239). Tree clean, master synced, the two
  merged feature branches deleted.
- **2026-08-24** — Read `architecture-v4.md` in full before designing, per `mission.md`. It resolves the
  placement question the spec and the issue disagreed on: *"Ports live in `Team.Contracts` as a namespace"*,
  and `Tharga.Team` is the Contracts role.
- **2026-08-24** — `Tharga.Team.Support` already references `Tharga.Team.Service`, so it sits above the
  domain and can host support's own operations and enforcement without a new package.
- **2026-08-24** — Verified: no `SupportCase` or `ISupportService` type exists anywhere in the repo. The
  spec's 2026-08-10 check still holds.
- **2026-08-24** — `SupportChannelBinding` is modelled but unused in this slice. Recorded so a reviewer does
  not read the empty collection as an oversight.
- **2026-08-24 — finding, outside this feature's scope but worth filing.** While establishing what purge
  destroys: `TeamServiceRepositoryBase.PurgeTeamAsync` and `DeleteTeamAsync` delete the team record and drop
  the host's per-team database, and **neither appears to remove the team's API keys or icon references**,
  which live in the toolkit's own shared collections keyed by `TeamKey`. If that is right, purging a team
  today leaves its API keys behind — credentials outliving the tenant they authorized, which is a security
  question rather than a tidiness one. **Verify before filing**, then raise it as its own item; do not widen
  this feature to fix it.

## Last session

Branch created, `feature.md` and `plan.md` written, awaiting plan confirmation. No code changed yet.
