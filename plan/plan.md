# Plan: two-way Slack threads for support cases (#142 phase 3)

Feature scope: `plan/feature.md`. Conventional-commit prefix: **`feat:`**.

## Steps

- [ ] **0. Package updates — HELD.** xunit 4.0 pair only, per the standing decision.

- [x] **1. `ISlackClient` gains thread support — DONE 2026-08-24.**
      `SlackPostResult` gains `MessageId` (Slack's `ts`), and `PostAsync` takes an optional `threadId` sent as
      `thread_ts`. Slack has no separate thread object — a thread *is* the `ts` of its first message — so this
      one value is what makes a conversation possible at all.
      `thread_ts` is omitted when null rather than serialized as null, because Slack rejects an explicit null
      and a threadless post is exactly what a notification wants.
      **This is a source-breaking change on a public interface, and it broke the one existing caller.**
      `threadId` sits before `cancellationToken` (convention keeps the token last), so
      `PostAsync(channel, text, token)` positionally now binds the token to `threadId`.
      `SlackNotificationSink` hit it immediately and was fixed with a named argument. **Any consumer calling
      `ISlackClient` positionally hits the same thing** — a compile error, not a silent change. Call it out in
      the PR description; the 3.16 bump already covers it.

- [x] **2. DONE 2026-08-24. `ISupportChannel` in `Tharga.Team`, and the Slack implementation in `Tharga.Team.Support`.**
      Two operations: open a channel projection for a case, and post a message to it. Nothing Slack-shaped in
      the port — `thread_ts` is a Slack detail and belongs behind `SupportChannelBinding.ExternalId`.
      Guard it the way the case contracts are guarded: the port must name no Slack type.

- [x] **3. DONE 2026-08-24. Outbound: raising and replying reach the thread.**
      Raising a case opens the projection and stores the binding; replying posts into the same thread.
      **A case with no channel configured must behave exactly as it does today** — Slack is optional, and the
      zero-binding case is the requirement slice 1 exists to protect. Assert it rather than assume it.
      Decide and record: if posting to Slack fails, does the case still get raised? Preferred **yes** — the
      case is authoritative and the channel is a projection, so a Slack outage must not stop somebody
      reporting a problem. Log it and leave the case unbound; a later reply can bind it.

- [x] **4. DONE 2026-08-24. Inbound: the endpoint, in the order that matters.**
      Build it in this sequence, because each step is independently testable and the later ones are where
      naive implementations fail:
      - **4a. Raw-body capture and signature verification.** HMAC-SHA256 over `v0:{timestamp}:{body}`.
        The raw body must be read before model binding — hashing a re-serialized object never matches, and
        it fails in a way that looks like a wrong secret. Reject a timestamp outside a freshness window, or
        the endpoint is replayable. **Constant-time comparison**, not `==`.
      - **4b. `url_verification`.** Echo the challenge, or event subscriptions cannot be enabled at all.
      - **4c. Ack then process.** Return 200 within Slack's 3-second budget and do the case write afterwards.
        Doing it inline turns a slow database into retries and duplicate replies.
      - **4d. Idempotency on `event_id`.** Slack retries are guaranteed. Where the seen-set lives needs a
        deliberate answer: **it must be shared across instances**, for exactly the reason `ITeamCache` exists
        — a process-local set means two instances each accept the same retry. Prefer `ITeamCache`; if it does
        not fit, say what was used and why.
      - **4e. Ignore the toolkit's own messages** (`bot_id` / `bot_message`), or replying to a case makes the
        toolkit answer itself.
      - **4f. Map the thread to its case and append**, attributed to the Slack author. An event for a thread
        the toolkit does not know is ignored quietly — a shared channel carries traffic that is none of its
        business.

- [ ] **5. The callback the host needs.**
      `SupportCaseUpdatedEvent` on `ISupportCaseService`, raised for **both** directions so the host reacts
      the same way whichever side replied. Follow `TeamsListChangedEvent` / `UserCreatedEventArgs`.
      **The event must carry enough to render without a re-read** — case id, team, and what changed — but not
      the message body: an event handler is host code and the body is free-form user content.
      Note for the docs: raising it from the inbound path means it fires on a background continuation, not a
      request thread, so a Blazor host must marshal with `InvokeAsync`.

- [x] **6. Tests — DONE 2026-08-24. The outbound gap is closed.**
      Done: signature verification (10), the inbound handler (9) — challenge, dedup, bot-echo, unknown
      thread, unsigned refusal.
      Outbound added (`SupportCaseChannelTests`, 6): raising stores the returned thread id; a reply carries
      that id rather than starting a new message; delivery is recorded Sent, Pending and Failed on the three
      paths; and **a case with no channel configured behaves exactly as before**, which is the regression
      guard for everything the site-only release shipped.
      **Found by checking rather than assuming** — the outbound path had been written, wired and shipped
      through three commits with no test touching it, while the inbound half had nineteen.

- [ ] **7. Full test suite.** Green before any commit.

- [ ] **7b. The sample site (user, 2026-08-24) — a page that exercises the whole round trip.**
      `Tharga.Team.Sample` must be able to raise a case, reply, and show a reply that arrived from Slack, so
      the feature is testable by hand rather than only by unit test.
      **It is also the design test.** The sample is a host like any other, so it may use only the public
      surface — if the page needs something a consumer cannot reach, the surface is incomplete. That is the
      same rule the shipped components will be held to in 3b, applied a slice early and for free.
      Include the callback: subscribe to the update event and re-render, with `InvokeAsync`, because a Slack
      reply arrives on a background continuation rather than a request thread. A sample that silently fails
      to update is worse than none, since it teaches the wrong pattern.
      **The inbound endpoint needs a public URL to receive events**, which a local sample does not have — say
      plainly in the docs how to test it (a tunnel, or posting a signed request by hand) rather than leaving
      someone to discover that Slack cannot reach `localhost`.

- [ ] **8. Docs.**
      - Extend `docs/articles/support-cases.md` with the Slack channel: what the host configures, the Slack
        app setup (event subscriptions, signing secret, bot token), and the endpoint to map.
      - State plainly that the endpoint is **public and unauthenticated by design** — it is authenticated by
        Slack's signature, not by a scope — because that will look wrong to a reviewer otherwise.
      - Document the callback and the `InvokeAsync` requirement.
      - Separate `docs:` commit.

- [ ] **9. Close the records.**
      - **Plan 04** — phase 3 delivered; **phase 2's widget dropped**, with the reason, so it stops reading
        as outstanding work.
      - `Requests.md` — no row for this; do not invent one.
      - **Issue #142 stays open** — phases 4 (AI) and 5 (Jira) remain. Comment; no `Fixes` keyword.

- [ ] **10. Close out.** Archive `feature.md`, `git rm -r plan`, final commit
      `feat: two-way Slack threads for support cases complete`, push, PR. Do not merge locally.
      **Bump `MAJOR_MINOR` to 3.16** — this adds public API.

## Notes and decisions

- **2026-08-24** — Branch from `master` at `8646934`. Tree clean.
- **2026-08-24 (user)** — **Two-way, over a public webhook.** Reasoning in `feature.md`; the short version is
  that consumers run multiple instances and a socket per process is the shape `ITeamCache` had to fix.
- **2026-08-24 (user)** — **No toolkit UI.** The host is building a bot-style panel in a Blazor hybrid app
  that sends queries to a bot or a human and renders the answer on a callback. Phase 2's widget is dropped.
- **2026-08-24** — **A consumer's repository was not read**, and must not be: a Tharga project never
  references a consuming product's checkout. The design above comes from what the user described here.

## Last session

Branch created, `feature.md` and `plan.md` written, awaiting plan confirmation. No code changed yet.
