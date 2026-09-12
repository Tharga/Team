# Feature: a provider-agnostic AI responder for support cases

Phase 4 of [Tharga/Team#142](https://github.com/Tharga/Team/issues/142), plus the sample gaining the whole
support surface.

## Goal

A support case can be answered by an AI before, or instead of, a human — against **any** model the host can
reach: Ollama, llama.cpp, a self-hosted model, OpenAI or Anthropic. The toolkit takes no vendor dependency
and names no provider.

## The decision this rests on

**The toolkit depends on `Microsoft.Extensions.AI.Abstractions` and nothing else.** The host constructs an
`IChatClient` and registers it; the toolkit consumes it.

This reverses the issue's own proposal of a `Tharga.Team.Support.Ai` package carrying the `Anthropic` SDK.
That package existed for exactly one reason — dependency quarantine under the rule in #147 — and an
abstraction package is not a vendor dependency to quarantine. So the AI phase folds into
`Tharga.Team.Support` as phases 1, 2, 3 and 6 did. Six proposed packages become one; this is the last of them.

Anthropic-specific capability is not lost by this, it moves: prompt caching, effort and token counting stay
available to a host that wants them, because the host owns the client it registers.

## Scope

- `ISupportResponder` in `Tharga.Team` — the port. One operation: answer a case, or decline to.
- `ChatSupportResponder` in `Tharga.Team.Support` — an `IChatClient` adapter over it.
- `SupportMessageKind.Assistant`, so a transcript can render an AI answer as what it is.
- Tools over case and team state, as `AIFunction`, gated by the existing scopes.
- Registration that degrades: no `IChatClient` registered means no responder, and every existing path
  behaves exactly as it does today.
- Sample: the AI responder, **plus** the three parts of the shipped support surface the sample never picked
  up — presence, the member's unread count and support's awaiting count.

### Out of scope

- Reminders (3d) and Jira (5) — separate phases of #142.
- Anonymous cases — deferred by the user 2026-09-02.
- Shipping any provider adapter. The host registers its own `IChatClient`; the toolkit ships none and
  references no vendor package.

## Architecture check

Read `architecture-v4.md` before designing, per `mission.md`. This moves toward the target or is neutral:

- **Rule 1, operations not CRUD.** `ISupportResponder.AnswerAsync(caseId)` is an operation, not data access.
- **Rule 2, one enforcement point.** The responder writes through `ISupportCaseService`, so authorization and
  invariants stay where they already are. It gets no privileged path of its own.
- **Rule 3, contracts serialize.** The port takes and returns records; no generic methods, no interface
  returns, no `IAsyncEnumerable`.
- **Rule 4, ports speak the domain's language.** `ISupportResponder` names cases and answers, never chat
  completions, tokens or messages-with-roles. `IChatClient` is the adapter's business and appears nowhere in
  `Tharga.Team`.
- **Not building on spec.** The port has one implementation, which the rule warns about — but it is a
  genuine dependency quarantine in the sense that matters: it keeps `Microsoft.Extensions.AI` out of
  `Tharga.Team`, which is the package every consumer takes.

## Acceptance criteria

- [ ] With no `IChatClient` registered, nothing changes: no responder resolves, and the full suite passes.
- [ ] With one registered, a case raised on the site gets an AI answer recorded as `Assistant`.
- [ ] The responder is authorized as any other caller — asserted by a test that it cannot read a case the
      caller could not.
- [ ] The AI's tools cannot reach data the requesting member could not see.
- [ ] Escalation to a human keeps the same case and the whole transcript.
- [ ] The transcript renders an AI answer distinguishably from a human one.
- [ ] The sample demonstrates the responder, presence, the unread chip and the awaiting count.
- [ ] Full suite green.

## Done condition

All acceptance criteria met, the user has confirmed, and #142 is updated with what shipped and what remains.
