# Plan: support case lifecycle and components

Feature scope in `plan/feature.md`. Spec:
`$DOC_ROOT/Tharga/plans/Toolkit/Platform/planned/09-support-case-lifecycle.md`.

## Order, and why it is this one

Contracts and behaviour before components, deliberately. **The components are the test of the surface**: if
one of them needs something a host cannot reach, the surface is incomplete and the component is hiding it.
Building them last is what makes that test mean anything.

## Steps

- [x] **0. Package updates and baseline.** `dotnet outdated`: only the xunit 4.0 / Microsoft.Testing.Platform
      pair, the standing hold with its own documented reason and its own PR. **Nothing to apply.** Baseline on
      this branch: build 0 errors, `dotnet test` **2199 passed, 0 failed** across seven projects.

- [x] **1. The optional subject.** Done 2026-09-02. `SupportCaseOptions.UseSubject` (default `false`),
      `SubjectFromMessage.Derive`, `SupportCaseLimits.DerivedSubjectLength` (50), applied in
      `SupportCaseService.RaiseCaseAsync`. Suite **2214 passed, 0 failed** (+15).
      **The option turned out to be a UI hint, not a service rule, and that is a simplification worth
      keeping.** The service derives a subject whenever one is blank — regardless of `UseSubject` — because
      `SupportCase.Subject` is not nullable and a half-filled form with the field *shown* must not produce a
      case that renders as an empty row in every list. So the option decides what a person is *asked* for,
      never whether the case ends up with a subject. That also means the service needs no access to the
      option at all.
      **Whitespace is collapsed before anything is measured**, which is the whole of the derivation: a
      message opening with a blank line, or one pasted with hard-wrapped newlines, otherwise yields a subject
      that is empty or full of line breaks — both of which look like a bug in the list rendering them, not in
      the derivation.
      A single word longer than the limit is cut mid-word rather than discarded: half a word is a poor
      subject, none at all is a worse one.

- [x] **2. Closure reason.** Done 2026-09-02. Suite **2220 passed, 0 failed** (+6).
      **Not stored, and not persisted at all — derived from `ClosedBy`.** The plan assumed a new field on the
      entity and a reason threaded through `ISupportCaseStore.CloseCaseAsync`. That would have changed a
      signature hosts implement for their own storage: a compile-time break in somebody else's repository,
      for a value already recoverable from what the store records. `CloseCaseAsync` already takes `closedBy`,
      so the sweeper passes `SupportCaseActors.AutoClose` and `SupportCase.ClosedReason` reads it.
      **No entity change, no migration, no port change.**
      **The coupling this creates is the right one:** the reason is exactly as trustworthy as the actor, so a
      case closed by the sweeper is closed for inactivity *by definition* rather than by a flag that could
      disagree with who closed it.
      The `system:` prefix on the actor is what keeps it from colliding with a real authentication subject,
      and a test pins that. An open case reads as having no reason even when a stale actor is left on it,
      which is the state a reopen produces.

- [x] **3. Reopen.** Done 2026-09-02. `ReopenCaseAsync` through both decorators, `SupportCaseChange.Reopened`,
      Mongo and in-memory stores. Suite **2229 passed, 0 failed** (+9).
      Authorized through the same `RequireCaseAccessAsync` reply and close use, so the rule needed no new
      thinking: the member who raised it, or a holder of `support:read`/`support:manage`.
      **Reopening an already-open case does nothing and is not an error.** Two people looking at the same
      closed case both press the button; the second must see an open case rather than an error about it
      already being open.
      **The whole closure is cleared, not just the status** — `ClosedAt`, `ClosedBy` and therefore
      `ClosedReason`. A case left carrying who closed it would read as having a closure reason while open,
      which is the state step 2's test already anticipated.
      **On the store it is a default member that throws.** `ISupportCaseStore` is host-implementable, so a
      required member would be a compile error in somebody else's repository — but a default that silently did
      nothing would leave a case closed while telling the caller it had opened, so it throws with the store's
      own type name in the message.
      **`StoreWithoutReopen` is a test double worth keeping** (its own file): it implements every required
      member by hand and leaves only the default alone. That makes it exactly what a host has — so if a future
      member arrives *without* a default, this file stops compiling and says so before a consumer finds out.

- [~] **4. The auto-close sweeper.** A hosted service on an interval, closing cases where the newest entry is
      **a `User` entry whose author is not the case author** and is older than `AutoCloseAfter` (default 7
      days).
      **The direction is the whole feature and is easy to invert**: a case whose newest entry is the
      *customer's* is waiting on support and must never close. A *system* entry must not start the clock
      either, or a reopen note re-arms it immediately.
      `LastMessageFromAuthor` is already denormalized on the entity for the awaiting-support count, so the
      query is a plain filter rather than an aggregation — but it is a `bool`, and "system entry" is a third
      state it cannot express. **Decide in this step whether to widen it or to check the transcript tail.**
      Write conditionally on the case still being open, so two instances close it once.
      `AutoCloseAfter` of `TimeSpan.Zero` registers no hosted service at all.

- [ ] **5. Store and options.** `GetCasesForAutoCloseAsync`, the closure-reason writes, and the two new
      options. Mongo implementation plus the in-memory test store.

- [ ] **6. Tests for every acceptance criterion**, including the three negatives that matter: the customer's
      own entry never auto-closes, a system entry never auto-closes, and two sweepers close once.

- [ ] **7. The customer component** in `Tharga.Team.Blazor` — my cases, one conversation, reply, reopen.
      Public surface only.

- [ ] **8. The support component** — the team's cases, answer, close, reopen, and the close warning.
      Advisory: dismissible, never a block.

- [ ] **9. A guard that the components use only public surface.** Asserted rather than assumed, because the
      claim is the whole reason a shipped component is acceptable at all.

- [ ] **10. Sample.** Replace the hand-rolled `/support` page with the two components, so the sample
      demonstrates them rather than duplicating them.

- [ ] **11. Docs** (own `docs:` commit): `docs/articles/support-cases.md` for the subject option, reopen and
      auto-close, plus the components and how to build your own instead.

- [ ] **12. Version line.** `MAJOR_MINOR` → `3.17` in `.github/workflows/build.yml`, in this PR.

- [ ] **13. Close-out** (only when the user says it is done): re-run `dotnet outdated`; comment on #142; move
      spec 09 to `../done/`; update `planned/README.md`; archive `plan/feature.md`; `git rm -r plan`; final
      commit `feat: support case lifecycle complete`.

## Last session

**2026-09-02.** Branch cut from master after PR #244 merged (the two registration fixes). Step 0 done — no
package updates to apply, baseline green at 2199. Order agreed with the user: 09, then 11, then 08, then 10.

**Spec 10 corrected the same day (user):** a case raised *in* a team belongs to that team and a departed
member loses access, which is intended; only cases raised with **no team selected** are user-level. Anonymous
cases deferred, which removed 10's dependency on 08. Neither affects this feature.
