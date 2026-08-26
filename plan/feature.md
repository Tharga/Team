# Feature: two-way Slack threads for support cases (#142 phase 3)

**Type:** feat (additive)
**Branch:** `feature/support-slack-channel`
**Issue:** [Tharga/Team#142](https://github.com/Tharga/Team/issues/142) — phase 3
**Target release:** 3.16 (new public API)

## Goal

A support case is projected onto a Slack thread. A reply raised through the toolkit appears in that thread; a
reply typed in that thread appears on the case. The host is **notified** when either happens, so its own UI
can react.

## What the host is building, and what that means for this design

The consuming UI is a bot-style panel in a Blazor hybrid app: it sends a query to a bot or to a human, and
renders the answer when a callback arrives. **The toolkit supplies the case, the channel and the notification
— not the UI.** Phase 2's widget is dropped rather than left looking outstanding; a ready-made panel is not
wanted.

Three consequences:

- **A callback seam is required, and it is new.** `ISupportCaseService` is request/response only today, so a
  Slack reply arriving has no way to reach a rendered page. This adds an event, following the existing
  `TeamsListChangedEvent` / `SelectTeamEvent` / `UserCreatedEvent` precedent.
- **No `Tharga.Communication` dependency.** A Blazor host already holds a live circuit per user, so the
  callback is an event plus `InvokeAsync(StateHasChanged)` in host code. The spec reached the same
  conclusion.
- **The bot half stays out.** The AI responder is a Neurolito client call in phase 4, not AI inside this
  toolkit. Routing a query to a bot *or* a human is the host's decision; the toolkit provides the human path.

## Transport: public webhook, not Socket Mode

**Multi-instance is the deciding argument.** Socket Mode holds a long-lived WebSocket per process. Consumers
run multiple instances — that is why `ITeamCache` exists, and it shipped as a **security** fix because
process-local state across instances let a suspended member keep their scopes. Socket Mode reintroduces that
shape: N instances, N sockets, Slack choosing one arbitrarily. A webhook is stateless and load-balances like
any other request.

The usual reason to choose Socket Mode does not apply: every consumer is already a web application serving
public HTTPS, so there is no endpoint to acquire, only a route to map. It also keeps reconnection, backoff,
duplicate delivery and ordering out of the toolkit.

**Cost, stated fairly:** consumers configure a Slack app with event subscriptions and hold a signing secret,
and the endpoint is publicly reachable so it must be correct.

## The five things that make inbound non-trivial

Each is easy to miss and each fails in production rather than in a demo.

1. **Signature verification over the raw body.** HMAC-SHA256 of `v0:{timestamp}:{raw body}` against the
   signing secret. The body must be read **before** model binding — hashing a re-serialized object silently
   never matches. Plus a timestamp freshness window, or the endpoint is replayable.
2. **Slack requires a 200 within 3 seconds.** Verify, enqueue, return; process afterwards. A case write done
   inline turns a slow database into Slack retrying and the case gaining duplicates.
3. **Retries are guaranteed, so idempotency is mandatory.** Slack retries with `X-Slack-Retry-Num`, and every
   event carries a unique `event_id`. Without deduplication a retry appends the same reply twice.
4. **Ignore the bot's own messages** (`bot_id` / `bot_message`), or the toolkit answers itself in a loop.
5. **The `url_verification` challenge** must be echoed, or subscriptions cannot be enabled.

## Design

- **`ISupportChannel`** — a port in `Tharga.Team`. Project a case onto a channel, post a message to it. The
  issue reversed its earlier advice here and was right to: an abstraction stops being speculative once cases
  can bypass channels entirely and Jira is named as a second one.
- **`SlackSupportChannel`** in `Tharga.Team.Support`, over the existing `SlackClient`.
- **`ISlackClient` gains thread support.** `SlackPostResult` carries only `Success` and `Error` today; it must
  return the message timestamp, and posting must accept a `thread_ts`. **Prerequisite for either direction.**
- **An inbound endpoint** mapped by the host, doing verification, ack, dedup and dispatch.
- **`SupportCaseUpdatedEvent`** on `ISupportCaseService`, raised for both directions, so the host's UI reacts
  the same way whichever side replied.

## Added 2026-08-24 (user), and what was already true

**Persistence is already done.** Slice 1 stores cases and their transcripts in MongoDB
(`SupportCaseEntity`, collection `SupportCase`, transcript embedded). Nothing is needed for the "keep the
messages somewhere" requirement; what is new is *state about* each message.

**Per-message delivery state.** Whether a message reached Slack, so an undelivered one is visible and
retryable rather than only logged. This **replaces the weaker decision in step 3** — a failed post no longer
just logs and moves on; it records the failure on the message.

**"Needs attention" queries**, for a dashboard component and a count chip in the top panel, for support and
for users. Two audiences, and they do not cost the same:

| Audience | Question | Needs new state? |
|---|---|---|
| Support | which cases are awaiting an answer? | **No** — the last message came from the case author |
| User | has a response arrived? | **No** to light it; **yes** to clear it on viewing |

The user-side chip is the fork: derived-only lights when the last message is not theirs, but stays lit until
they reply. Clearing on *viewing* requires a per-user read state. **Open — see the decision below.**

**Components may ship, but must never be the only way (user, 2026-08-24).** A dashboard component and a count
chip are welcome; a host must always be able to build its own instead.

**That gives the design its sharpest test: the toolkit's own component may use only the public surface a host
could use.** If a shipped chip needs something a consumer cannot reach, the surface is incomplete and the
component is hiding it. So the counts are a public query returning a record — not a calculation that lives
inside a component — and any component added later is a *demonstration* that the surface suffices, not a
privileged path.

This is also the v4 UI constraint arriving early: a component reaches for contracts and gates rendering only.
Building the first support component that way costs nothing now and keeps it off the audit list the
architecture document already warns about.

**Reminders are deliberately not in this feature.** They need their own decisions — what triggers one, the
cadence, which channel, and how two instances avoid nagging twice — and they are a background job rather than
part of the two-way path. They become straightforward once delivery state exists, so they follow as 3b.

## Acceptance criteria

- [x] Raising a case posts to Slack and stores the returned thread id as a `SupportChannelBinding`.
- [x] A reply through `ISupportCaseService` appears in the same thread, not as a new message.
- [x] A reply typed in the thread appears on the case, attributed to the Slack author.
- [x] The host is notified for both directions by one event.
- [x] A request with a bad, missing or stale signature is refused — asserted, including the stale case.
- [x] The same Slack `event_id` delivered twice appends **one** message.
- [x] A message the toolkit itself posted does not come back as a reply.
- [x] The endpoint answers `url_verification`.
- [x] ~~The endpoint returns 200 without waiting for the case write.~~ **WITHDRAWN 2026-08-26, deliberately.**
      The reason to ack first was that slowness makes Slack retry - and `ISupportEventLedger` records the
      delivery before any write, so a retry is now idempotent. Appending a message is one document update,
      comfortably inside the three-second budget. A queue and a pump would have been machinery for a problem
      the ledger already solved. Recorded rather than ticked, because the criterion became wrong rather than
      met.
- [x] A case with **no** channel binding still works exactly as before — Slack stays optional.
- [x] Full test suite green - 2187 passed, 0 failed.

## Out of scope

- **The AI bot / query routing** — phase 4, and a Neurolito client call rather than AI in this toolkit.
- **Jira** — phase 5, and **do not assume this port already fits it** (raised by the user 2026-08-24).
  `SupportChannelType.Jira` exists in the enum and stays unused, but Slack and Jira are different *kinds* of
  channel: Slack is a conversation, where "open a projection, post a message" is the whole of it; Jira is a
  ticket, with status, assignee and workflow transitions. The ask was to *follow* tickets, which means reading
  Jira state rather than only writing comments into it.
  **So `ISupportChannel` is shaped for messaging, deliberately, and claims nothing more.** If phase 5 needs
  status synchronisation it probably wants a second port beside this one rather than being forced through
  `PostMessage` — forcing a ticket workflow into a message-shaped abstraction is how ports go wrong. Taking
  the change that is correct today and not designing for an imagined second implementation is also what the
  architecture document asks for.
  **Already settled and not reopened by Jira:** the toolkit's `SupportCase` is authoritative and channel
  bindings are projections. Slice 1 built it that way, which closes the spec's open question about which
  system owns status across an AI → human → Jira escalation.
- **Presence** — the issue's "is support online" ask. Separate, and it drives a UI decision the host owns.
- **Any UI.**
- **Outbound retry/queueing beyond what Slack's own API needs.** If posting to Slack fails, the case still
  exists; that is the model working as intended, not a gap to paper over.

## Package updates — held, standing decision

Only the xunit 4.0 / Microsoft.Testing.Platform pair. `shared-instructions.md` now documents why it keeps
failing — `dotnet test` finds zero tests on Windows with xunit.v3 4.x and exits 5. Its own PR.
