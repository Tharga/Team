# Feature: Tharga.Team.Email — a shared mail layer, and a team mailbox on it

**Consumers:** FortDocs e-postinkorg (FD-675 epic: FD-676 addresses, FD-677 catch-all routing and postmaster), and Tharga.Team.Support, whose support mail becomes one consumer of the same layer. Tier 2 — a consumer cannot build this outside the toolkit without re-implementing the support mail transport.

## Why a package of its own

Two products receive and send mail: the support inbox (simple cases, states, an assignee) and an organisation's case-management mailbox (a legal register). They are the same up to the moment a mail *belongs* to something, and different after it. So the mail layer is shared and the *case* is not:

- **Shared, in `Tharga.Team.Email`:** transport (IMAP read, SMTP send, MIME build/parse with HTML and attachments, the outbound policy), the **mailbox** (addresses, catch-all routing, postmaster, dedupe, raw content handover), and the **thread tracker** (Message-Id and a tracking reference on outgoing mail; incoming mail matched by In-Reply-To/References, plus-address and subject token to an *owner*: an owner type and an opaque id the consumer interprets).
- **Not shared:** the case. A support case and a diarium case keep their own models; each uses the mail layer through the owner reference.

The package quarantines MailKit: consumers who only want teams never take it, which is the reason a v4 package may exist.

## Goal of this branch

Every team can receive real mail at an address nobody can guess:

- A team holds an opaque, rotatable **key**: one *primary* for everything new, and at most one *secondary* still accepted until its retirement date.
- The team address is `{key}{suffix}@{domain}`, e.g. `8fjgthitwne3-case@fortdocs.app`; an item inside the team is `{key}{suffix}+{itemKey}@{domain}`. Suffix, domain and item key are the host's.
- **One catch-all IMAP mailbox** receives everything, routed by the *envelope recipient* (`Delivered-To`, `X-Original-To`, `Envelope-To`), never the `To` header.
- Mail to a current key reaches that team's mailbox — marked when it came to a retiring key. Mail to an unknown or retired key reaches the **postmaster**, a system-wide list, never the void.
- Read idempotently on several instances: one stored item per Message-Id, durably (not a 24-hour ledger), and a crash can only cause a re-read, never a loss.

## Decisions (2026-10-15)

- New package **`Tharga.Team.Email`**; `Tharga.Team.Support` depends on it. Contracts and scopes in `Tharga.Team`, Mongo adapters in `Tharga.Team.MongoDB`, views in `Tharga.Team.Blazor` (contracts only, no MailKit).
- **Content:** the envelope (sender, recipients, subject, Message-Id, received, route) in the toolkit's store; the **raw MIME** handed to `IInboundMailContentStore`, which the host implements (FortDocs: Azure Blob).
- Nothing is registered automatically: the toolkit delivers and lists mail; what a person does with it is the consumer's.
- Support's own matching (channel binding + event ledger) moves onto the shared tracker in a **follow-up PR**, so it cannot destabilise this one. That follow-up also closes the two ledger gaps (24-hour retention; ledger written before the case).

## Scopes

- Team: `mailbox:read` (list and open the team's mail, see its addresses), `mailbox:manage` (rotate the key, set a message's outcome). Registered at a host-chosen level, like support.
- System: `mailbox:postmaster:read` (list and open unrouted mail; every open audited), `mailbox:postmaster:manage` (route a message to a team). Never team-level.

## Out of scope here

- Match hints from a diarienummer — FortDocs FD-678 (the tracker gives it the thread signals).
- Sending from an item with a tracking reference, and the rich-text composer — with FortDocs FD-769.
- Moving support onto the tracker — follow-up PR.

## Done when

The package exists with the moved transport (support unchanged), rotation, routing, postmaster, retention, tracker and content handover work and are tested; README and `docs/articles/team-email.md` describe them; `MAJOR_MINOR` bumped (new package and public API); FortDocs runs against it end to end.
