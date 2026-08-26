# Plan: read state and the "needs attention" query

Feature scope: `plan/feature.md`. Conventional-commit prefix: **`feat:`**.

## Steps

- [ ] **0. Package updates — HELD.** xunit 4.0 pair only, per the standing decision.

- [x] **1. The model — DONE 2026-08-26, and narrower than planned.**
      `SupportCaseReadEntity` (identity, last-read sequence, read-at) embedded on `SupportCaseEntity`.
      **Deviation, deliberate:** the plan said to expose `UnreadForCaller` on the `SupportCase` contract. That
      would make the contract **caller-aware** — the store would have to be told who is asking on every read,
      changing several signatures — and nothing needs it: the counts answer the question, and per-case unread
      is only wanted by a panel that does not exist yet. **Read state stays entirely in the entity**, the port
      takes primitives, and no contract type is added at all.
      If a later panel wants per-case unread, that is an additive read rather than something this slice had to
      guess at.
      Kept from the plan: identity is the **stable subject** (a member key stops resolving when somebody
      leaves, and a case outlives a membership), and the marker is the **sequence** rather than a timestamp.

- [ ] **2. Port and store.**
      `MarkReadAsync(teamKey, caseId, identity, sequence)` and whatever the two counts need.
      **Count in the store, not by loading cases.** A count that reads every case to tally it in memory is
      fine with ten cases and a problem with ten thousand; the awaiting-support count is a filter on the last
      entry's author, and the unread count is a filter on this user's read entry. Both belong in the query.
      **Marking read must be idempotent and must not grow the document** — one entry per user, updated in
      place, not appended. A support agent opening a case fifty times must leave one entry.

- [ ] **3. The two operations, and their two different checks.**
      On `ISupportCaseService`, enforced in the authorization decorator like everything else:
      - `GetMyUnreadCountAsync` — membership.
      - `GetAwaitingSupportCountAsync` — `support:read`. **This is the one to get right**: it counts across
        everybody's cases, so it is exactly as privileged as reading them.
      - `MarkReadAsync` — **the same check as reading that case**. Reuse the existing case-access path rather
        than writing a second, similar one; a check that merely resembles the read check is how the two drift.

- [ ] **4. Tests.** Every acceptance criterion, and specifically:
      - a reply arriving after I read makes the case unread **again** — the case a naive "read once" flag
        gets wrong;
      - **my own reply does not make the case unread to me** — the mistake that makes a chip light every time
        the user types;
      - two users on one case have **independent** unread state;
      - marking twice leaves one entry;
      - a member without `support:read` cannot get the awaiting count;
      - a member cannot mark read a case they could not read.

- [ ] **5. Full test suite.** Green before any commit.

- [ ] **6. The sample page shows both counts.**
      Extend `/support`. It is still the design test: if the page cannot render a count from the public
      surface, the surface is wrong. Show the unread count, and the awaiting-support count when the caller
      holds the scope — which also demonstrates the two-audience split the chip will need.

- [ ] **7. Docs.** Extend `support-cases.md`: the two questions, what each requires, that opening a case
      marks it read, and that the counts are public API precisely so a host can build its own chip.
      State the in-process limitation again where the counts are described — a chip that must update live on
      a multi-instance deployment needs a backplane that does not exist.
      Separate `docs:` commit.

- [ ] **8. Close the records.**
      - **Plan 04** — record that the query surface for the chip and the dashboard exists, so 3b is the
        components alone.
      - **Issue #142** — comment; stays open.
      - `Requests.md` — no row; do not invent one.

- [ ] **9. Close out.** Archive `feature.md`, `git rm -r plan`, final commit
      `feat: read state and the needs-attention query complete`, push, PR **targeting the #242 branch or
      master depending on whether #242 has merged by then — check before opening it.**
      `MAJOR_MINOR` is already 3.16 and this adds to the same release; no bump needed.

## Notes and decisions

- **2026-08-26 (user)** — **Read state**, over the derived-only alternative. A header chip in front of
  customers that never clears is one people learn to ignore.
- **2026-08-26** — Support's side needs **no** read state: a case awaits an answer when its last entry came
  from the case author. Only the user's side needs a record.
- **2026-08-26** — Two methods rather than one record carrying both numbers, so the privileged count is
  behind its own check and nothing arrives half-populated.
- **2026-08-26** — **Stacked on #242** because it touches the same three files. Rebase if #242 changes.

## Last session

Branch created from `feature/support-slack-channel`, `feature.md` and `plan.md` written, awaiting plan
confirmation. No code changed yet.
