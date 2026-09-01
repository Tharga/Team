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

## Model clarified 2026-09-01 (user) — steps 4 and 5 reopen

**Email is a customer channel; Slack is a support channel.** A customer types on the site or sends mail, and
is answered the same way they made contact. Support works in the backoffice or in a Slack thread, and never
touches mail in either direction. Both audiences read the whole history — the customer on the site, support
in the backoffice.

Steps 1–3 are unaffected. What changes:

- **Step 4 is built backwards** and reopens. `EmailSupportChannel.OpenAsync` mails the author whenever a case
  is raised *on the site*, which is exactly the case that must be answered on the site. The email projection
  belongs to a case that *arrived* by mail.
- **A case records its origin**, and replies route by it rather than by configuration.
- **Step 5 grows the create path.** Unmatched mail raises a case; see the team table in the spec. The
  sender-match rule survives, but it governs *replies* only.
- **Step 7's single-channel question is answered:** `IEnumerable<ISupportChannel>` with a role, because one
  case can hold an email binding facing the customer and a Slack binding facing support at once.
- **A new internal read:** resolve an email address to a user. Nothing in the toolkit does this, and inbound
  mail has no caller, so it is framework code reading on nobody's behalf.
- **A fallback team becomes required configuration** once the mailbox is read, enforced at startup.

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

- [x] **2. Options.** Done 2026-09-01. `MailOptions` + `MailServerOptions` in `Email/`, reached as
      `SupportCaseOptions.Email` and projected onto their own DI options type exactly as the Slack section is.
      `SlackChannel` and `SigningSecret` stay flat: moving them for symmetry would break the configuration of
      every host that has set them, and the reshape belongs in plan 05, the breaking batch.
      **Where mail is configured, and why it differs from Slack.** On `AddThargaSupportCases`, not
      `AddThargaSupport`. The Slack *transport* sits on the latter because notifications use it too; nothing
      but cases sends or reads mail, so putting it there would force a host wanting email cases to register
      the notification sink and its hosted service for nothing.
      **Two things pulled forward, because an option nobody can interpret is not worth shipping alone:**
      - **`RecipientFilter`** — the matcher that gives `Recipients` its meaning: bare domain matches any local
        part, full address matches only itself, case-insensitive, display-name headers unwrapped, and
        plus-addressing stripped so the per-case reply-to is not rejected. 13 tests.
      - **The startup check from step 7**, which refuses a configuration whose own `FromAddress` its filter
        would reject. Left to step 7 it would have been written after the code that needs it.
      Suite **2224 passed, 0 failed** (+20).
      **Corrected within the step, prompted by a question about invitation mail.** The projection was first
      written as twelve hand assignments — the exact pattern `OptionsForwarder` exists to prevent and that has
      shipped as a bug twice here (Tharga/Team#177): a property added later is accepted from the host and
      silently discarded. Now copied by reflection, with `EverySettableMailOption_IsForwarded` asserting the
      *result* — nothing left at its default — so the guard survives someone reverting to assignments.
      `OptionsForwarder` itself is internal to `Tharga.Team.Blazor`; if a third caller appears, promote it
      rather than writing a third copy.
      **Invitation mail stays separate, deliberately.** `ITeamEmailSender` / `EmailOptions` on
      `AddThargaTeam` is a different contract, options type and SMTP stack. Not unified, and not given a
      fallback: an invitation is usually sent from `noreply@`, and support mail must come from an address
      replies return to — inheriting one for the other would send support mail from a no-reply address and
      lose every reply, while the from-address/filter check validated an address nobody chose. A host wanting
      one mailbox binds both from the same configuration section, explicitly.
      **Also folded in:** `SlackNamespaceIsolationTests` became `TransportNamespaceIsolationTests`, a theory
      over both transport namespaces. A copied guard is the one that gets extended for one namespace and not
      the other. Its detector self-check now also asserts a transport type is *not* exempt outside its own
      namespace, so Slack and email cannot reach into each other by the long route.

- [x] **3. Mail transport.** Done 2026-09-01. MailKit 4.17.0 added to `Tharga.Team.Support` — the first
      third-party dependency, and the one the package was created in advance to quarantine. The csproj comment
      claiming "no third-party package" was corrected rather than left contradicting the line beneath it.
      `ISupportMailClient` / `SupportMailClient` over SMTP and IMAP, never throwing: a failed send is a failed
      `MailSendResult`, a failed read is an empty fetch at the unchanged position.
      **The testable logic was factored out of the I/O deliberately**, because a transport tested only against
      a live server is tested by nobody: `MailMessageFactory` (outgoing headers), `InboundMailReader`
      (received message → `InboundMail`) and `HtmlText` are pure and carry all 37 new tests. The client itself
      is thin enough to read.
      **Three decisions worth keeping:**
      - **The mailbox is opened `ReadOnly`.** Not a precaution — read-write lets a fetch set `\Seen` as a side
        effect, which is exactly what hides a message from the other site's instance.
      - **A connection per operation, not a pooled one.** A long-lived IMAP connection is the process-local
        state this design rejected when it chose polling over a socket, and it buys nothing at a poll interval
        in minutes.
      - **`References` carries the whole chain with the parent appended**, not just `In-Reply-To`. Clients
        thread on `References`; a reply naming only its parent starts a new conversation in some of them, and
        the mail is delivered perfectly while doing it.
      **Two bugs the tests caught before they shipped:** paragraphs were flattening to a single newline so a
      mail arrived as one run-on block, and a bounce was not recognised because an empty return path is
      written literally as `<>` rather than as an empty string — the loop-protection case that matters most.
      Suite **2261 passed, 0 failed** (+37).
      **Not registered yet** — that is step 7. Nothing resolves `ISupportMailClient` at this point.

- [~] **4. `EmailSupportChannel : ISupportChannel`.** REOPENED 2026-09-01 by the clarified model — it mails the author when a case is raised on the site, which is the one case that must be answered on the site. The projection belongs to a case that arrived by mail, and the case must carry its origin. Everything below was true of the first cut and mostly survives the rework. Done 2026-09-01. Opening mails the case author and
      keeps the `Message-ID` as `ExternalId`; replying threads on `In-Reply-To` and `References`. Every
      failure path returns a quiet null or false — unconfigured, no address, refused — because a channel
      being down must never stop a case being raised. Suite **2274 passed, 0 failed** (+13).
      **`SupportChannelBinding` gained an optional `Address`, and that is the substantive decision here.**
      A case has no email on it and none should be added: the correspondent is a property of the
      *projection*, not of the case. A Slack thread is posted into a room anyone can answer, so there is
      nobody to name; an email thread has exactly one correspondent, and a later reply must go back to them
      rather than to whoever is signed in. It is additive and nullable, so nothing existing changes.
      **The address is read from the signed-in user at open time, not looked up.** `GetUserByKeyAsync` carries
      `[RequireScope(SystemUserScopes.Manage)]`, which somebody raising a case has no reason to hold — routing
      around that would have been the authorization bypass `shared-instructions.md` warns about. Asking later
      would answer the wrong question anyway: by the time support replies, the signed-in user is the agent.
      **`PostAsync` reads the case back by binding** for its subject and id, which neither the binding nor the
      message carries. Same lookup the inbound path already uses, and a customer reads the subject line even
      though their client threads on headers.
      **`PerCaseReplyTo` defaults to off.** A server that rejects a plus-addressed local part bounces the
      reply back at the customer; with it off a reply is matched on headers, and the rare client that strips
      those leaves a reply unmatched and logged. A visible miss beats a bounce in front of a customer.
      **Two echo guards:** a message whose `Source` is `Email` is never mailed back — the provenance field
      added in step 1 paying for itself — while one from Slack still is, because that is not this channel's
      echo.
      **The step-2 guard fired for real:** adding `PerCaseReplyTo` failed
      `EverySettableMailOption_IsForwarded` until the test set it. Exactly the silent-drop class it was
      written for, caught one step later.

- [~] **5. Inbound handling.** REOPENED 2026-09-01: unmatched mail must now raise a case rather than be dropped, and the sender-match rule narrows to replies only. First cut done 2026-09-01, as `EmailEventHandler` — the per-message pipeline, fully
      tested. Suite **2315 passed, 0 failed** (+41).
      **Split from the hosted service deliberately:** the pipeline is where every decision that can lose or
      leak mail lives, and it is testable as a pure sequence; the poller is a loop around it and belongs with
      the watermark it advances. Step 6 now carries both.
      **The check order is asserted, not just documented** — `AMailAddressedElsewhere_NeverReachesTheLedger`
      fails if the recipient filter is ever moved after the ledger, which is the shared-mailbox defect that
      would otherwise be invisible until the other site started losing mail.
      **Two things this step needed that were not in the plan:**
      - **`PerCaseAddress`**, building *and* parsing the `support+{caseId}@…` convention in one place. They
        are one convention; a build that disagrees with its parse produces replies that arrive and match
        nothing. `EmailSupportChannel` was rewritten to use it rather than keep its own copy.
      - **`ISupportCaseStore.GetCaseByIdAsync`**, because the address fallback resolves a case id with no
        team and the store had no such read. **Added as a default interface member returning null** — a host
        implementing the port keeps compiling, which is the same reasoning that kept `ITeamEmailSender`
        un-widened in decision 1. A store that cannot answer is answering honestly; the header path is
        primary regardless.
      **`QuotedText` is deliberately conservative:** it cuts only on unambiguous markers and keeps the whole
      body when it recognises nothing, because keeping too much reads badly while cutting too much silently
      loses what somebody wrote. A reply that is entirely quote is kept whole rather than stored as an empty
      entry.
      **A test caught a locale bug:** the attribution pattern required the verb against the colon, which fits
      English (`… Support wrote:`) and fails Swedish (`… skrev Support:`) — so Swedish replies would have
      kept their whole quoted thread.

- [ ] **6. Inbound poller and watermark.** A hosted service on the configured interval, fetching from the
      instance's own
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

      Watermark store: a port in `Tharga.Team` with a Mongo implementation, one record per deployment,
      holding the last considered UID and its `UIDVALIDITY`. **No new endpoint** — there is no webhook to map,
      which is the one place this transport is cheaper than the rejected one.

- [ ] **7. Registration.** Wire the email channel in `SupportRegistration` with `TryAdd` semantics, active
      only when configured. The library registers the complete set — a consumer never enumerates interfaces
      by hand.
      ~~**Plus a startup check** naming the from-address/filter mismatch.~~ **Done in step 2** —
      `RequireSendingAddressIsAccepted`, with tests. What remains here is registering the channel, the poller
      and the watermark store when mail is configured.
      **Blocker found in step 4, decide before writing this:** `SupportCaseService` takes a **single**
      `ISupportChannel`, and registration uses `TryAdd` — so configuring Slack and email together silently
      gives whichever registered first, with no warning. A case is modelled to carry several bindings, so the
      model already disagrees with the wiring. Options: take `IEnumerable<ISupportChannel>` and open a
      projection per configured channel (the constructor is `internal`, so no public contract changes), or
      refuse both at startup and say so. **The first is the honest one** — two bindings is what
      `SupportCase.Bindings` being an array has always meant — but it changes what `RaiseCase` does when one
      channel accepts and another refuses, which needs stating rather than discovering.

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
