# Plan: a team's credentials and data do not outlive the team

Feature scope: `plan/feature.md`. Conventional-commit prefix: **`fix:`**.

## Steps

- [ ] **0. Package updates — HELD.** xunit 4.0 pair only, per the standing decision.

- [ ] **1. Prove both defects before fixing either.**
      The backlog entry says *verify before fixing, do not fix on the description alone* — so this step ends
      with failing tests, not with confidence.
      - **1a. A key for a soft-deleted team still authenticates.** The ordinary `teams:delete` path. Expect
        this to reproduce easily; it needs no purge and no key reuse.
      - **1b. A key for a purged team still authenticates**, and its record is still in the collection.
      - **1c. The crossover**, which is what made this Critical: purge team `X`, create a new team that takes
        the key `X`, and assert whether the old tenant's API key now authorizes against the new team. **If
        this does not reproduce, say so and downgrade the entry** rather than quietly leaving it Critical —
        the backlog says the crossover is unverified and that must not silently become fact.
      - Also record what a key for a soft-deleted team can actually *do* today. Most team reads exclude
        deleted teams, so the practical blast radius may be small even though the principal is authenticated.
        The difference matters for the release note.

- [ ] **2. Part A — refuse a key whose team is not live.**
      In `ApiKeyAuthenticationHandler`, after the `DisabledAt` check and **only for team keys**
      (`key.TeamKey != null`). A system key has no team and must be untouched.
      - Resolve the team through a path that already excludes soft-deleted teams, so "deleted" needs no
        second definition. Prefer the internal read used by claims construction — this is framework code
        building a principal, exactly the case `shared-instructions.md` says must not be scope-gated, because
        requiring a scope while issuing the claims that grant it is circular.
      - Audit the refusal via `LogAuthEvent` with a reason naming the cause, as the disabled-key path does.
      - **Fail closed, but not on a store outage.** A team lookup that *throws* is not evidence the team is
        gone. Decide deliberately and write it down: refusing every key when the store blips turns a
        transient fault into a total outage, which is how `IsDisabledAsync` already reasons — it evicts on
        the revalidation interval and is deliberately fail-open for the same reason.
      - Cost check: this adds a read per API-key authentication. `ITeamCache` exists for exactly this class
        of lookup; use it rather than adding a round trip to every authenticated call.

- [ ] **3. Part B — the participant seam.**
      `ITeamPurgeParticipant` in `Tharga.Team`; `PurgeCascadeTeamServiceDecorator` composed in
      `ThargaBlazorRegistration` beside the authorization decorator, where `sp` is in hand.
      Participants run **before** the team record is deleted; one that throws aborts the purge.

- [ ] **4. The three participants.**
      - **Support cases** — wraps the existing `DeleteCasesForTeamAsync`. Cheapest, and proves the seam.
      - **API keys** — needs a **new delete-by-team** on the API-key repository; only `DeleteAsync(string key)`
        exists today. Audit the bulk removal as one fact, not one entry per key.
      - **Icon references** — check what a team actually owns before writing this. If the icon store is
        content-addressed and shared, deleting by team may be wrong; say what was found rather than assuming
        symmetry with the other two.

- [ ] **5. Tests.**
      Turn every step-1 reproduction green, plus: a **system key is unaffected** (the obvious way to break
      Part A); restoring a soft-deleted team makes its keys work again, so the check follows state rather
      than latching; a throwing participant leaves the team present; and the cascade removes all three
      stores' data.

- [ ] **6. Full test suite.** Green before any commit.

- [ ] **7. Docs.**
      - `implementation-guide.md` — a key stops working when its team is deleted, and what purge now removes.
      - `support-cases.md` — **delete the purge limitation**, which stops being true.
      - Release notes must say the upgrade does **not** repair data already orphaned by past purges.
      - Separate `docs:` commit.

- [ ] **8. Close the records.**
      - Backlog — mark the purge/API-key entry fixed **with the evidence**, and correct the severity if
        step 1c failed to reproduce.
      - `Requests.md` — no row for this; do not invent one.
      - No GitHub issue exists; do not open one to close it.

- [ ] **9. Close out.** Archive `feature.md`, `git rm -r plan`, final commit
      `fix: a team's credentials and data do not outlive the team complete`, push, PR. Do not merge locally.
      `MAJOR_MINOR` is already 3.15 and this adds a small amount of public API (`ITeamPurgeParticipant`);
      check whether a minor bump is wanted or whether it rides the 3.15 line.

## Notes and decisions

- **2026-08-24** — Branch from `master` at `2426583` (post-#240). Tree clean.
- **2026-08-24** — **The order changed after reading the code.** The filed entry was about purge-and-reuse;
  the underlying defect is that `ApiKeyAuthenticationHandler` never looks the team up at all, so the
  *ordinary* `teams:delete` path already leaves keys working. Part A is therefore the security fix and Part B
  the cleanup — not the other way round.
- **2026-08-24** — The DI obstacle that deferred this during the support-cases work is solved by composing
  the cascade decorator in `ThargaBlazorRegistration` (`:425-439`), where the factory has `sp`. No base-class
  constructor changes, so the `IIconStore` forwarding trap is not repeated.

## Last session

Branch created, `feature.md` and `plan.md` written, awaiting plan confirmation. No code changed yet.
