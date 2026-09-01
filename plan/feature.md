# Feature: support case lifecycle and components

**Spec:** `$DOC_ROOT/Tharga/plans/Toolkit/Platform/planned/09-support-case-lifecycle.md`
**Issue:** [Tharga/Team#142](https://github.com/Tharga/Team/issues/142) — the site-first slice
**Branch:** `feature/support-case-lifecycle` (from `master`)
**Target release:** 3.17 (new public API)
**New published packages:** none — components land in `Tharga.Team.Blazor`

## Goal

The path a customer and a support agent actually walk on the site, made complete: raise a case without having
to invent a subject, read your own conversation, reopen a case that was closed, and have a case nobody
answered close itself rather than sit open forever.

Everything here works with **no channel configured at all**, which is the ordinary shape for a host that only
wants the site.

## Scope

| Audience | Gets |
|---|---|
| Team user | raise a case, read **My cases** and the full conversation, reply, reopen |
| Support | every thread in the team, read and answer, close and reopen, with a warning before closing |

Plus the two behaviours behind them: an optional subject, and automatic closure after inactivity.

## Decisions, settled 2026-09-01/02 (user)

1. **The subject is optional and off by default.** With no subject, the first 50 characters of the message
   become it — derived in the *service*, so a host writing its own UI cannot forget it.
2. **A case can be reopened**, authorized as replying is, with a system entry recording it.
3. **A case closes itself** after a configurable span (default 7 days) **when support wrote the last entry**.
   Never when the customer did — that case is waiting on support, and closing it would hide work rather than
   tidy it. Marked as closed for inactivity, and reopenable like any other.
4. **Components live in `Tharga.Team.Blazor`**, which already builds on `Tharga.Blazor`'s base components.
   They may use only the public surface a host could use.
5. **Closing from the back office warns first** and suggests letting the person who raised it close it.
   Advisory, never a block.

## Acceptance criteria

- [ ] With `UseSubject` off, raising a case with no subject derives one from the first 50 characters.
- [ ] Derivation cuts on a word boundary, collapses whitespace, and marks truncation.
- [ ] A message shorter than the limit becomes the subject unchanged, with no ellipsis.
- [ ] With `UseSubject` on, a supplied subject is kept exactly.
- [ ] A closed case can be reopened by its author, and by a holder of `support:manage`.
- [ ] Reopening is refused for someone who could not reply to the case.
- [ ] Reopening writes a system entry and returns the case to `Open`.
- [ ] A case whose newest entry is support's, older than the configured span, closes automatically.
- [ ] A case whose newest entry is the **author's** never auto-closes, however old.
- [ ] A case whose newest entry is a **system** entry never auto-closes.
- [ ] An auto-closed case is marked `Inactivity`; a manually closed one is marked `Manual`.
- [ ] Two sweepers running together close it once.
- [ ] `AutoCloseAfter` of zero registers no background work at all.
- [ ] An auto-closed case can be reopened.
- [ ] The customer component lists my cases, shows a conversation, replies and reopens.
- [ ] The support component lists the team's cases, answers, closes and reopens.
- [ ] Closing from the support component warns first, and the warning can be dismissed to proceed.
- [ ] Both components use only public surface — asserted, not assumed.
- [ ] Full test suite green (baseline: 2199 passed, 0 failed).

## Done condition

All acceptance criteria met, `MAJOR_MINOR` moved to 3.17 in the same PR, docs landed as their own `docs:`
commit, and the user has confirmed the feature is done.

## Out of scope

- **The email channel** (spec 08) and **Slack polish** (spec 11) — both independent, both after this.
- **Teamless and anonymous cases** (spec 10) — after 08.
- **Reminders before auto-close.** A "closing in two days" message is a reminder, which is 3d.
- **Richer statuses.** `SupportCaseStatus` stays two-valued; a closure reason answers the question asked
  without inventing a support-desk workflow.
