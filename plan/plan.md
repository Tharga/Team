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

- [x] **4 and 5. The auto-close sweep, its store operations and its options.** Done together 2026-09-02 —
      the sweep and the store work are one decision, and splitting them would have meant writing the query
      before knowing what it had to answer. Suite **2245 passed, 0 failed** (+16).
      **The tri-state question, resolved: neither widen the bool nor re-read the candidates.** The transcript
      is *embedded*, so the store already holds the last entry when it loads a case. So the query narrows on
      indexed fields (`Status`, `LastMessageFromAuthor`, and a new `LastMessageAt`) and then finishes on the
      tail in memory — no third state to migrate, and no second read per candidate.
      **`LastMessageAt` is absent on existing documents, and that is deliberate.** A case last touched before
      the field existed never auto-closes until somebody writes to it. Applying new behaviour retroactively
      would close a backlog nobody has looked at, in bulk, on the first sweep after an upgrade.
      **The store owns the whole predicate**, including the half a filter cannot express. Splitting it — a
      cheap query plus a caller that re-reads each candidate to inspect it — would turn one query into one
      query per case.
      **The conditional write is the concurrency answer**, matching `ISupportEventLedger`:
      `TryCloseForInactivityAsync` applies only while the case is still open and reports whether *it* closed
      it, so two instances sweeping together close once. The store also sets the actor itself, so a caller
      cannot make an automatic closure claim to be a person's.
      **Both new store members are defaults** — the port is host-implementable — and a store that ignores
      them simply never auto-closes, which is the safe direction for a read and a write that are both
      optional.
      **Two things the tests caught.** `SupportContractShapeTests` rejected `IReadOnlyList<SupportCase>` on
      the port: target rule 3 forbids interface returns, and the guard was right — it is `SupportCase[]` now.
      And the sweep's own tests exercise an in-memory double that mirrors the adapter, which would pass
      just as happily if the two disagreed, so `SupportWroteLast` is now tested against the **real** Mongo
      adapter as well. That is where the system-entry rule actually lives.

- [x] **7 and 9. The customer component and the surface guard.** Done together 2026-09-02 —
      `Features/Support/SupportCasesView.razor`, `SupportCasesViewText`, `SupportComponentSurfaceTests`.
      Suite **2253 passed, 0 failed** (+8).
      **Decision 4 needed correcting first, and the user agreed (2026-09-02).** `Tharga.Team.Blazor` does not
      reference `Tharga.Team.Support`, deliberately — "nothing references this back, so no consumer acquires
      it by accident" — and since step 3 of the email work put MailKit there, a component reaching for
      `ISupportCaseService` would have made **every** `Tharga.Team.Blazor` consumer download MailKit for a
      feature they may not use. So `ISupportCaseService` moved to `Tharga.Team`, joining `SupportCase`,
      `ISupportCaseStore` and `ISupportCaseNotifier`, which were already there — it was the odd one out.
      **The move breaks nothing:** the namespace is unchanged (namespaces are independent of assemblies), so
      no `using` breaks, and `TypeForwardedTo` in `Tharga.Team.Support` keeps existing binaries loading. The
      whole solution rebuilt with no source edits anywhere.
      **`UseSubject` is a component parameter, not a read of the options.** The options live in
      `Tharga.Team.Support` and this component depends on contracts only — and a parameter also lets one page
      ask for a subject while another does not, which reading the options could not.
      **The guard caught its own rule being too loose.** It first flagged `ISupportCaseNotifier`, which is a
      legitimate public contract — but the fix is *not* to permit `Tharga.Team` by prefix, because that would
      admit `Tharga.Team.Service` and `Tharga.Team.MongoDB` too. Contract namespaces are matched **exactly**
      and UI ones by prefix, with a test asserting an internal service's namespace satisfies neither.

- [x] **8 and 10. The support component and the sample.** Done 2026-09-02 —
      `Features/Support/SupportQueueView.razor`, `SupportQueueViewText`, and the sample's `/support` page
      rebuilt on both components. Suite **2258 passed, 0 failed** (+5).
      **The close warning suggests the alternative rather than only warning.** A case closed while somebody is
      still typing reads as being dismissed, and the person best placed to say a problem is solved is the
      person who had it — so the confirmation offers "answer and let them close it" and its cancel button
      reads *Leave it open*. Advisory: support can still close a case that is finished.
      **The sample stopped being a second implementation.** It hand-rolled the whole panel, which had to be
      kept in step with the components by hand; it now renders them, with the queue gated on `support:read`
      so a member meets a rendering gate rather than a refusal they can do nothing about.
      **The compiler caught a cross-branch mistake:** the queue first showed `SupportMessage.Source` as a
      provenance badge, but that field lives on the email branch, not here. A comment now marks the spot —
      an email reply is attributed on a sender match rather than an authenticated caller, so that badge is
      worth having the moment spec 08 lands.
      **Both components are recorded in `TextCoverageTests.Migrated`**, so the no-literal-text ratchet applies
      to them. They were written against the catalogue from the start, which is exactly why they belong on
      that list: a component that never had a literal string must not acquire its first one.

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
