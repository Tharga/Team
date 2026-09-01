# Plan: email as a support channel

Feature scope in `plan/feature.md`. Spec:
`$DOC_ROOT/Tharga/plans/Toolkit/Platform/planned/08-support-email-channel.md`.

## Transport — settled 2026-09-01 (user), and it reverses decision 2

**IMAP in, SMTP out, against the host's own mailbox, with MailKit.** No provider, no public endpoint, no
signature verification. The spec records the reversal and why the multi-instance objection that rejected
polling does not survive contact with this design: the ledger already makes a double poll idempotent, and the
recipient filter sorts a shared mailbox into disjoint sets before anything else runs.

**Verify before step 5:** dump the headers of a real message sent to each domain and confirm which of
`Delivered-To` / `X-Original-To` / `Envelope-To` the mailbox's MTA actually adds. The filter's reliability
depends on it, and it is minutes to check against days to debug.

## Steps

- [x] **0. Package updates and baseline.** `dotnet outdated` across the solution: the only available update
      is the xunit 4.0 / Microsoft.Testing.Platform pair, which is the standing hold with its own documented
      reason and its own PR. **Nothing to apply, so nothing bundled.** Baseline verified on this branch:
      `dotnet build -c Release` 0 errors / 17 pre-existing warnings, `dotnet test -c Release` **2196 passed,
      0 failed** across seven projects. SDK 10.0.302 does not hit the zero-tests-ran problem.
      Also deleted the stale merged local branch `feature/support-slack-notifications` (phase 1).

- [x] **1. Contracts in `Tharga.Team`.** Done 2026-09-01 — `SupportChannelType.Email` added; both stale
      remarks corrected; `SupportMessage.Source` added (nullable, `[BsonIgnoreIfNull]` +
      `[BsonRepresentation(BsonType.String)]` on the entity) and mapped both directions in
      `MongoSupportCaseStore`; `SlackEventHandler` now stamps `Source = Slack`.
      **Decision worth keeping:** provenance is its own field rather than a third `SupportMessageKind` value.
      *Who wrote it* and *which door it came through* vary independently — an email reply is
      `Kind = User` and untrusted at the same time — and collapsing them would have made the trust signal
      unrepresentable.
      Tests: 8 added, suite **2204 passed, 0 failed** (baseline 2196). The existing
      `PersistedEnumRepresentationTests` sweep already unwraps nullable enums, so `Source` was covered on
      arrival; it is named in the sweep's self-check so a regression in the unwrap is caught.
      - `SupportChannelType.Email`, and correct that type's stale remark.
      - Correct `SupportChannelBinding`'s stale remark ("nothing reads or writes a binding").
      - Add provenance to a transcript entry: `SupportChannelType? Source` on `SupportMessage` — null means
        raised on the site. **Set it for Slack too**: today a reader cannot distinguish a Slack reply from a
        site reply except by the author id looking like an id, which is correct on its own terms and is what
        the trust decision needs for email.
      - Tests: JSON round-trip; `SupportChannelType` stored by name (assert the BSON type).

- [~] **2. Options.** `SupportCaseOptions` gains a nested `Email` section, including the optional
      **`Recipients`** list — domains or full addresses; unset accepts everything.
      **Decision to record in code:** `SlackChannel` and `SigningSecret` stay flat where they are rather than
      moving into a `Slack` section for symmetry. Reshaping them is a breaking configuration change for any
      host that has configured them, and symmetry is not worth an outage. The inconsistency with
      `SupportOptions` (which nests `Slack` and `Notifications`) is visible and harmless; the reshape belongs
      in plan 05, the breaking batch.

- [ ] **3. Mail transport** — `Tharga.Team.Support/Email/`, mirroring `Support/Slack/`. `ISupportMailClient`
      over **MailKit**: SMTP send setting `Message-ID`, `In-Reply-To` and `References`; IMAP fetch; MIME
      parsing. One dependency, in the package that exists to quarantine exactly this.
      **No `Tharga.Team.*` types cross into the folder** — the #142 code-organisation rule, guarded by the
      same reflection-test pattern the Slack folder already has, so lifting it out stays a move.

- [ ] **4. `EmailSupportChannel : ISupportChannel`.** `OpenAsync` sends the opening mail and returns a binding
      carrying its `Message-ID` as `ExternalId`; `PostAsync` replies into that thread. Returns null rather
      than throwing when unconfigured or refused — a channel being down must never stop a case being raised.

- [ ] **5. Inbound poller.** A hosted service on a configurable interval, fetching from the instance's own
      stored watermark (UID + `UIDVALIDITY`, reset and re-scan on a validity change). **It never sets `\Seen`
      and never moves a message** — the flag is shared state in a mailbox two applications read, so using it
      as "handled" would hide the other site's mail from it. Then, per message, in this order:

      **5a. The recipient filter, before the ledger insert.** This deviates from `SlackEventHandler`, which
      records the event *before* its filters — correct there, wrong here, and **not to be "fixed" to match**.
      `MongoSupportEventLedger` dedups on a unique index over `(Source, EventId)` in the Team database, so if
      both sites share one database: instance A records the id for a mail addressed to the other site, drops
      it, and instance B then sees a duplicate and concludes somebody handled it. Nobody did, and the mail is
      gone with a debug line saying it was already dealt with. Filtering first is correct in both topologies.
      Resolve the recipient from headers in order — `Delivered-To`, `X-Original-To`, `Envelope-To`, then
      `To`/`Cc` — because IMAP exposes no envelope and which of the first three exists is the MTA's choice.
      Bare domain matches any local part; strip plus-addressing before comparing a local part, or the per-case
      reply-to is rejected; case-insensitive. **A drop touches nothing in the mailbox** — only this instance's
      watermark advances, so the other site still finds the message.

      **5b. Dedup** through `ISupportEventLedger`, source `"email"`, the message's `Message-ID`. Then:
      - **Trust:** match the case by threading headers, falling back to the per-case reply-to address; accept
        only from the address that case already corresponds with. Anything else is dropped and recorded.
      - **Loop protection:** `Auto-Submitted`, `X-Auto-Response-Suppress`, `Precedence: bulk`, and never act
        on a `Message-ID` we sent.
      - **Body:** trim quoted history and signature; flatten HTML to text; record that attachments were
        dropped. Enforce `SupportCaseLimits` — a few quoted round-trips can hit the per-entry cap alone.

- [ ] **6. Watermark store.** A port in `Tharga.Team` with a Mongo implementation, one record per deployment,
      holding the last considered UID and its `UIDVALIDITY`. **No new endpoint** — there is no webhook to map,
      which is the one place this transport is cheaper than the rejected one.

- [ ] **7. Registration.** Wire the email channel in `SupportRegistration` with `TryAdd` semantics, active
      only when configured. The library registers the complete set — a consumer never enumerates interfaces
      by hand.
      **Plus a startup check** naming the mismatch when the configured from/reply-to address is not matched by
      this instance's own recipient filter. Without it the instance drops every reply to its own mail and it
      looks identical to inbound being broken.

- [ ] **8. Correct the `ITeamEmailSender` remark** so it scopes "the only mail the toolkit sends" to the core
      and points at the support channel.

- [ ] **9. Tests for every acceptance criterion** in `plan/feature.md`, including the negative ones: stale
      signature, replayed id, own message returning, auto-responder, wrong sender, oversized quoted reply.
      Run the full suite before each commit.

- [ ] **10. Docs** (own `docs:` commit): an email section in `docs/articles/support-cases.md` covering
      provider setup, the reply-to scheme and the trust rule, plus `Tharga.Team.Support/README.md`. State
      plainly that a public URL is needed and that localhost cannot receive.

- [ ] **11. Sample.** Wire email in `Tharga.Team.Sample/Program.cs` on the Slack pattern — registered
      unconditionally, dormant without secrets, so the wiring resolves in the real application graph. Show
      `Source` on `SupportPage`.

- [ ] **12. Version line.** `MAJOR_MINOR` → `3.17` in `.github/workflows/build.yml`, **in this PR**. Nothing
      in CI does it, and a version-line-only PR queues a content-free release.

- [ ] **13. Close-out** (only when the user says the feature is done): re-run `dotnet outdated`; comment on
      #142 and close what this satisfies; move spec 08 to `../done/`; update `planned/README.md`; archive
      `plan/feature.md`; `git rm -r plan`; final commit `feat: support email channel complete`.

## Last session

**2026-09-01.** Branch created from a current `master` (fetched, neither ahead nor behind). Decisions 1–3
settled with the user and recorded in spec 08. Step 0 done — no package updates to apply, baseline green at
2196 tests.

**Decision 5 added the same day (user): an optional recipient filter**, so one mailbox can serve
`fortdocs.se` and `eplicta.se` with each instance taking only its own. Writing it up surfaced two things that
were not obvious: the filter must run **before** the ledger insert or a shared database silently loses the
other site's mail, and the filter must accept the plus-addressed per-case reply-to or the instance rejects
replies to its own mail. Both are now acceptance criteria.

**Transport settled later the same day (user), reversing decision 2: IMAP in, SMTP out, MailKit, against the
host's own mailbox.** No provider, no public endpoint, no signature verification. Two further consequences
came out of writing it up, both of which lose mail if missed: `\Seen` cannot mean "handled" in a mailbox two
applications read, so the poller keeps its own UID watermark and never mutates shared flags; and IMAP exposes
no envelope, so the recipient filter resolves through a header chain whose availability is the MTA's choice
and must be verified against the real mailbox.

Plan **awaiting confirmation before any code**. Nothing is open except that verification, which is step 5's
prerequisite rather than a decision.
