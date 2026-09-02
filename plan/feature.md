# Feature: Slack support — link, presence, and an installable app

**Spec:** `$DOC_ROOT/Tharga/plans/Toolkit/Platform/planned/11-slack-support-polish.md`
**Issue:** [Tharga/Team#142](https://github.com/Tharga/Team/issues/142) — 3b/3c
**Branch:** `feature/slack-support-polish`, **stacked on `feature/support-case-lifecycle` (#245)**
**Target release:** 3.17

## Why it is stacked

#245 is green and mergeable but not merged, and both touch `SupportRegistration.cs`. Branching from master
would guarantee a conflict there; stacked, this rebases onto master trivially once #245 lands. Same reasoning
the read-state branch used against the Slack-channel branch earlier.

**If #245 is revised, rebase this before continuing.**

## Goal

Make the Slack side usable by somebody who is not us: a notification that links to the case it is about,
an honest answer to "is anyone on support right now", and an app a consumer can install without guessing at
scopes.

## Scope

1. **A notification carrying a link to the case.**
2. **Support presence**, cached and advisory.
3. **An app manifest**, in the repository and used by the sample.

## What is already true, verified 2026-09-02

Worth knowing before building, because two of these shorten the work considerably:

- **Slack intake and thread replies already shipped** (PR #242, 3.16). Never exercised, because
  `ISupportCaseService` could not be resolved until #244 — so it reads as missing when it is merely untried.
- **The case id already reaches a template.** `NotificationRouter.Resolve` falls through to audit metadata for
  any unknown placeholder, and the auditing decorator writes `support.case.id` and `support.case.subject`. So
  `{support.case.id}` works today with no code at all. **What is missing is the URL**, not the identifier.
- **No support event is in the default routes.** `team:create`, `team:invite`, `team:remove-member` and
  `user:delete` are the four built-ins, so a host gets no support notification until it adds a route.
- **`EveryBuiltInRoute_NamesAnEventTheToolkitEmits`** guards the defaults against a hard-coded list of emitted
  events. Adding a default means extending that list — and verifying it against what is actually emitted
  rather than just appending.

## Decisions

1. **The link is a host-supplied URL template, not a base address.** The toolkit does not know the host's
   routing — `/support` is the sample's choice, not a convention — so a base address would only work for hosts
   that happened to match it. A template the host writes (`https://app.example.com/support/{caseId}`) puts the
   route where the knowledge is. It renders **empty rather than broken** when unset.
2. **Presence is advisory and never a gate.** Unknown renders as nothing, not "offline" — telling a customer
   not to bother when support is in fact there is worse than saying nothing.
3. **An app manifest, not a shared Tharga app.** Slack issues tokens per workspace installation, so there is
   no arrangement where consumers use a Tharga-owned token; and a distributed app would make Tharga host an
   OAuth endpoint and hold other organisations' credentials without solving inbound routing, since events must
   reach the consumer's own deployment. Settled 2026-09-02 after the question was asked directly.

## Acceptance criteria

- [ ] A route template can carry a working link to the case, resolved from the audit metadata already present.
- [ ] With no URL template configured, the placeholder renders empty and the rest of the message is intact.
- [ ] A host can be notified when a case is raised, without writing a route by hand.
- [ ] Every built-in route still names an event the toolkit actually emits.
- [ ] Presence is cached, and the cache is shared across instances rather than process-local.
- [ ] Presence that cannot be determined renders as nothing, never as "offline".
- [ ] Presence never blocks or delays raising a case.
- [ ] The manifest in the repository creates an app with exactly the scopes these features need — verified by
      following it, not by reading it.
- [ ] The sample is configured from that manifest, with its steps written down.
- [ ] Full test suite green (baseline: 2258 passed, 0 failed).

## Out of scope

- **Reminders** (3d) — a background job with its own decisions about cadence and duplicate suppression.
- **Anything Tharga-hosted**, by decision 3.
- **Email** (spec 08) and **teamless cases** (spec 10).
