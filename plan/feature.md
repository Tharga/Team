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

- [~] **REVERSED 2026-09-01 by the clarified model, not met and not to be met.** *Raising a case with the
      email channel configured sends a mail and stores its `Message-ID` as a `SupportChannelBinding`.*
      This was built and then removed: it mailed the author whenever a case was raised **on the site**, which
      is exactly the case that must be answered on the site — the person who raised it is looking at the
      page. An email projection is opened by mail *arriving*. Asserted the other way round now, by
      `RaisingACaseOnTheSite_CreatesNoEmailProjection`.
- [x] A reply through `ISupportCaseService` is mailed into the same thread, carrying `In-Reply-To` and
      `References` — not as a fresh mail.
- [x] A mailed reply appears on the case, marked as email-sourced and **named** by the sending address —
      `AuthorName`, never `AuthorIdentity`. Writing an unverified address into the identity field would be
      wrong twice: it is what authorization compares a caller's subject against, and the inactivity sweep
      reads "support wrote last" as *the newest entry is not the author's*, so the customer's own reply would
      have armed the clock that closes their case.
- [x] The host is notified for both directions by the existing `SupportCaseUpdatedEvent` — including
      `Raised` with a null team when mail opens a case.
- [x] The same inbound `Message-ID` polled twice appends **one** message.
- [x] Two concurrent pollers handed the same message append **one** message between them. The decision is
      the ledger's insert against a unique index, which is atomic across instances where a read-then-write
      would not be; the poller is asserted to respect its answer rather than to make one.
- [x] The poller never sets `\Seen` and never moves a message; progress is its own stored position.
      **Guarded by a source scan** (`MailboxIsNeverMutatedTests`), because observing it needs an IMAP server
      and a second deployment, while the failure is silent permanent mail loss: read-write access lets a
      fetch set `\Seen` as a side effect that nobody wrote a line for.
- [x] A change in `UIDVALIDITY` resets the position and re-scans without appending duplicates. A generation
      going *backwards* discards it too — it should not happen, and that is exactly when a stored UID is
      least worth trusting.
- [x] A mail the toolkit itself sent does not come back as a reply; an auto-response does not either.
- [x] A mail from an address the case does not correspond with is dropped and recorded, not appended — and
      now gets a case of its own instead, which is the only thing an unknown sender can do.
- [x] A reply quoting the full thread is trimmed to its new content, and stays inside `SupportCaseLimits`.
      Over-long mail is **trimmed rather than refused**, unlike a reply typed into a form: there is nobody to
      tell, and the message id is already recorded, so refusing would discard what a customer sent and never
      ask again.
- [x] A case with **no** email binding still works exactly as before — email stays optional, as Slack is.
      Both are live together now, so a case can hold one of each.
- [x] **With no recipient filter configured, every addressed mail is accepted** — the filter is optional.
- [x] A domain filter of `fortdocs.se` accepts `support@fortdocs.se` and rejects `support@eplicta.se`.
- [x] A domain filter accepts the plus-addressed per-case reply-to (`support+{caseId}@fortdocs.se`).
- [x] Matching is case-insensitive on both the local part and the domain.
- [x] **A rejected mail is not recorded in `ISupportEventLedger`**, so the other instance can still record
      it. Asserted as an ordering, so moving the filter after the ledger fails the test rather than only
      contradicting a comment.
- [ ] A rejected mail leaves the mailbox untouched — no flag, no move — so the other site's instance still
      finds it.
- [x] A startup check names the mismatch when the configured from/reply-to address is not matched by this
      instance's recipient filter.
- [x] Contracts round-trip through `System.Text.Json` (target rule 3).
- [x] `SupportChannelType` persists by name — asserted on the stored BSON type.
- [x] The stale XML remarks are corrected — **four, not three**: `ITeamEmailSender` had a second copy of
      its claim in `ThargaBlazorOptions.Email`.
- [x] Docs updated: `docs/articles/support-cases.md` and `Tharga.Team.Support/README.md`. Three claims in
      the existing text had become false and were corrected rather than left standing beside the new
      sections.
- [x] Full test suite green: **2462 passed, 0 failed** (baseline 2196), warnings 32 against the ratchet
      of 35.

## Added by spec 10, folded in here because fewer PRs is better

- [x] A case may belong to **no team**, durably — raised, answered, closed and reopened without one.
- [x] The unassigned queue is governed by **system** scopes, because `support:read` is held against a team
      and an unassigned case has none. A caller fully privileged in their own team gets nothing.
- [x] `AssignCaseAsync` is conditional on the case still having no team, and **tells** the operator who lost
      the race rather than silently doing nothing.
- [x] Assignment is recorded in the transcript, naming the team and the operator.
- [x] An assigned case leaves the queue and becomes readable by that team's members.
- [x] Assigning to no team at all is refused.
- [x] `GetMyCasesAsync` matches nothing for a caller with no subject, rather than every unattributed case in
      the team.
- [x] A back-office component for the queue, offering only teams that exist — nothing validates a team key
      on the way through, so a free-text field could assign a case to a team that does not exist.

## Done condition

All acceptance criteria met, `MAJOR_MINOR` moved to 3.17 in the same PR, docs landed as their own `docs:`
commit, and the user has confirmed the feature is done.

## Out of scope

- Raising a case by email from an unknown sender — its own decision, with its own abuse surface.
- Bounce and complaint handling beyond not looping on them.
- Attachment storage — needs a blob store that does not exist. Dropped and recorded instead.
- Any UI beyond the sample page.
- Jira (phase 5), presence (3c), reminders (3d), the AI bot (4), the 3b components.
