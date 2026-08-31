# Feature: read state and the "needs attention" query (#142, slice 3b groundwork)

**Type:** feat (additive)
**Branch:** `feature/support-read-state` — **stacked on `feature/support-slack-channel` (#242)**
**Issue:** [Tharga/Team#142](https://github.com/Tharga/Team/issues/142)
**Target release:** 3.16

## Goal

A host can ask two questions and render a count from either: *does this user have anything new?* and *which
cases are waiting on support?* Opening a case marks it read, so a user's indicator clears.

## Why it is stacked rather than branched from master

#242 is open and touches `ISupportCaseStore`, `ISupportCaseService` and `SupportRegistration` — the same
three files this needs. Branching from master would guarantee conflicts in all of them. Stacked, this rebases
onto master trivially once #242 merges. **If #242 is revised, rebase this before continuing.**

## The decision this rests on

**Read state, chosen 2026-08-26 (user).** The alternative was deriving the indicator — light it when the last
message is not yours — which needs no new state but never clears until the user replies. A header chip in
front of customers that will not clear is one people learn to ignore, so the per-user record is worth its
cost.

**Only the user's side needs it.** Support's question is answerable from what is already stored: a case is
awaiting an answer when its **last message came from the case author**. No read state, no new writes, and it
stays correct however many support people look at it.

## Two questions, two methods, two different checks

Not one record with both numbers in it. A combined result would either leak the support-wide figure to an
ordinary member or arrive half-populated, and a half-populated record is the kind of thing a component
renders as a zero.

| Method | Answers | Authorized by |
|---|---|---|
| `GetMyUnreadCountAsync(teamKey)` | cases of mine with entries I have not read | **Membership** |
| `GetAwaitingSupportCountAsync(teamKey)` | cases whose last entry came from their author | **`support:read`** |
| `MarkReadAsync(teamKey, caseId)` | records that I have read up to the latest entry | **The same check as reading the case** |

`MarkReadAsync` must be authorized exactly as reading is — anything less would let someone mark a case they
cannot see, which is a write on a case they have no business touching.

## Where read state lives

**Embedded on the case document**, as an array of *(user identity, last-read sequence)*.

- It matches how this codebase already stores things that belong to a parent: members embed in a team,
  the transcript embeds in a case.
- It is atomic with the case, so no second write can half-happen.
- **It is naturally bounded** — participants in one support case, not one row per user per case across the
  whole system. A separate collection would grow with users × cases forever.

**The sequence, not a timestamp.** The transcript is already sequenced and paging already uses it; a
timestamp would need to agree with clocks it has no reason to trust, and "read up to entry 7" is exact where
"read at 12:04" is a guess.

## Acceptance criteria

- [x] A case with entries beyond my last-read counts as unread; one I have just opened does not.
- [x] `MarkReadAsync` is idempotent — marking twice changes nothing and does not grow the document.
- [x] A **new reply arriving after I read** makes the case unread again.
- [x] My own reply does not make the case unread to me.
- [x] The awaiting-support count includes a case whose last entry is from its author, and excludes one whose
      last entry is a reply or a system entry.
- [x] A member without `support:read` cannot obtain the awaiting-support count.
- [x] A member cannot mark a case read that they could not read.
- [x] Both counts are **public API** a host can call — no calculation that only a shipped component can do.
- [x] Read state does not leak across users: two users on one case have independent unread states.
- [x] Full test suite green - 2196 passed, 0 failed.

## Out of scope

- **The components themselves** — the chip and the dashboard panel. This is the surface they will be built
  on, and building it first is what proves the surface is sufficient rather than assuming it.
- **Reminders** (3d), **presence** (3c), **the bot** (4), **Jira** (5).
- **Cross-instance notification of count changes.** The notifier is in-process; a chip that must update
  without a page interaction on a multi-instance deployment needs a backplane, which is not built and is
  recorded as such.
- **Read state for anything but cases.**

## Package updates — held, standing decision

Only the xunit 4.0 / Microsoft.Testing.Platform pair, which needs its own PR.
