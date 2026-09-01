# Feature: email as a support channel, in and out

**Spec:** `$DOC_ROOT/Tharga/plans/Toolkit/Platform/planned/08-support-email-channel.md`
**Issue:** [Tharga/Team#142](https://github.com/Tharga/Team/issues/142) — phase 6
**Branch:** `feature/support-email-channel` (from `master`)
**Target release:** 3.17 (new public API)
**New published packages:** none — lands in `Tharga.Team.Support`
**New dependency:** MailKit, in `Tharga.Team.Support` only — the package that exists to quarantine exactly this

## Goal

A support case can be projected onto an email thread. A reply raised through the toolkit is mailed to the
case's correspondent; a reply mailed back appears on the case. The host is notified either way, through the
`SupportCaseUpdatedEvent` the Slack channel already raises.

Read **and** send. Sending alone is a notification, not a channel.

## Scope

- `SupportChannelType.Email`, and provenance on a transcript entry so an email-sourced reply is visibly that.
- An outbound mail contract in `Tharga.Team.Support`, mirroring `Slack/ISlackClient` — **not** a new member on
  `ITeamEmailSender`.
- `EmailSupportChannel : ISupportChannel`, with RFC threading headers.
- An inbound poller: an optional recipient filter, deduplication through the existing `ISupportEventLedger`,
  loop protection, sender-match trust, quote trimming, and its own UID watermark rather than mailbox flags.
- Docs and sample wiring, dormant without configuration exactly as Slack is.

## Decisions, settled 2026-09-01 (user)

1. **A separate outbound port in `Tharga.Team.Support`**, not a new member on `ITeamEmailSender`. Adding a
   member is a compile-time break in every host that implemented it, and the mail dependency belongs in the
   package that exists to quarantine it.
2. **IMAP in, SMTP out, against the host's own mailbox, with MailKit** — standard protocols, no provider in
   the mail flow, no public endpoint, no signature verification. **This reverses the spec's original "provider
   webhook, IMAP rejected"**, and the spec records why: the objection was about duplicated work, and
   `ISupportEventLedger` already makes a double poll idempotent, while the recipient filter sorts a shared
   mailbox into disjoint sets before anything else runs. No leader election, no lease.
3. **A `From:` header is not an identity.** Inbound mail may only append to a case it matches, from the
   address that case already corresponds with. An unmatched or unexpected sender is dropped and recorded,
   never used to raise a case. Email-sourced entries are marked as such.
4. **An optional recipient filter**, so one mailbox can serve two sites and each instance takes only its own
   (`fortdocs.se` / `eplicta.se`). Matches the **envelope recipient**, not `To:`; a bare domain matches any
   local part; plus-addressing is stripped before comparing a local part, or the per-case reply-to would be
   rejected. Unset accepts everything. **It runs before the ledger insert** — see the plan's step 5 for why
   that is load-bearing rather than stylistic.

## Folded in, because the files are already being edited

- **Stale XML remarks on two shipped types.** `SupportChannelBinding` ("Modelled now, unused for now.
  Nothing reads or writes a binding until the channel work lands") and `SupportChannelType` ("Nothing reads
  or writes a binding yet") both became false when phase 3 shipped. `SupportChannelType` gains `Email` in
  this feature, so its remark is corrected in the same edit.
- **The `ITeamEmailSender` remark** ("Invitations are the only mail the toolkit sends … no other feature is
  planned to grow one") becomes wrong the moment this ships. Corrected to scope the claim to the core.

## Acceptance criteria

- [ ] Raising a case with the email channel configured sends a mail and stores its `Message-ID` as a
      `SupportChannelBinding`.
- [ ] A reply through `ISupportCaseService` is mailed into the same thread, carrying `In-Reply-To` and
      `References` — not as a fresh mail.
- [ ] A mailed reply appears on the case, attributed to the sending address and marked as email-sourced.
- [ ] The host is notified for both directions by the existing `SupportCaseUpdatedEvent`.
- [ ] The same inbound `Message-ID` polled twice appends **one** message.
- [ ] Two concurrent pollers handed the same message append **one** message between them.
- [ ] The poller never sets `\Seen` and never moves a message; progress is its own stored watermark.
- [ ] A change in `UIDVALIDITY` resets the watermark and re-scans without appending duplicates.
- [ ] A mail the toolkit itself sent does not come back as a reply; an auto-response does not either.
- [ ] A mail from an address the case does not correspond with is dropped and recorded, not appended.
- [ ] A reply quoting the full thread is trimmed to its new content, and stays inside `SupportCaseLimits`.
- [ ] A case with **no** email binding still works exactly as before — email stays optional, as Slack is.
- [ ] **With no recipient filter configured, every addressed mail is accepted** — the filter is optional.
- [ ] A domain filter of `fortdocs.se` accepts `support@fortdocs.se` and rejects `support@eplicta.se`.
- [ ] A domain filter accepts the plus-addressed per-case reply-to (`support+{caseId}@fortdocs.se`).
- [ ] Matching is case-insensitive on both the local part and the domain.
- [ ] **A rejected mail is not recorded in `ISupportEventLedger`**, so the other instance can still record it.
- [ ] A rejected mail leaves the mailbox untouched — no flag, no move — so the other site's instance still
      finds it.
- [ ] A startup check names the mismatch when the configured from/reply-to address is not matched by this
      instance's recipient filter.
- [ ] Contracts round-trip through `System.Text.Json` (target rule 3).
- [ ] `SupportChannelType` persists by name — asserted on the stored BSON type.
- [ ] The three stale XML remarks above are corrected.
- [ ] Docs updated: `docs/articles/support-cases.md` and `Tharga.Team.Support/README.md`.
- [ ] Full test suite green (baseline: 2196 passed, 0 failed).

## Done condition

All acceptance criteria met, `MAJOR_MINOR` moved to 3.17 in the same PR, docs landed as their own `docs:`
commit, and the user has confirmed the feature is done.

## Out of scope

- Raising a case by email from an unknown sender — its own decision, with its own abuse surface.
- Bounce and complaint handling beyond not looping on them.
- Attachment storage — needs a blob store that does not exist. Dropped and recorded instead.
- Any UI beyond the sample page.
- Jira (phase 5), presence (3c), reminders (3d), the AI bot (4), the 3b components.
