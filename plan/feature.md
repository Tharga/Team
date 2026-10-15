# Feature: team mailbox

**Consumer:** FortDocs e-postinkorg (FD-675 epic: FD-676 addresses, FD-677 catch-all routing and postmaster). Tier 2 — a consumer cannot build this outside the toolkit without re-implementing the support mail transport.

## Goal

Every team can receive real mail at an address nobody can guess:

- A team holds an opaque, rotatable **key**: one *primary* for everything new, and at most one *secondary* still accepted until its retirement date.
- The team address is `{key}{suffix}@{domain}`, e.g. `8fjgthitwne3-case@fortdocs.app`; an item inside the team (a case, in FortDocs) is `{key}{suffix}+{itemKey}@{domain}`. Suffix, domain and the item key are the host's.
- **One catch-all IMAP mailbox** receives everything. Each message is routed by its *envelope recipient* (`Delivered-To`, `X-Original-To`, `Envelope-To`), not the `To` header.
- Mail to a current key reaches that team's mailbox — marked when it came to a retiring key. Mail to an unknown or retired key reaches the **postmaster**, a system-wide list, never the void.
- Read idempotently on several instances: the same message is stored once (unique on Message-Id), and a crash can never lose a message.

## Decisions (2026-10-15)

- **Package:** inside `Tharga.Team.Support`, next to the mail transport it reuses (MailKit stays quarantined there). Contracts in `Tharga.Team`, Mongo adapters in `Tharga.Team.MongoDB`, a postmaster view in `Tharga.Team.Blazor`.
- **Content:** Tharga keeps the parsed envelope (sender, recipients, subject, Message-Id, received time, route) in its store and hands the **raw MIME** to a content store the host implements (`IInboundMailContentStore`); FortDocs puts it in Azure Blob. Nothing large in Mongo.
- Nothing is registered automatically: the toolkit delivers and lists mail; what a person does with it is the host's.

## Scopes

- Team: `mailbox:read` (list and open the team's mail, see its addresses), `mailbox:manage` (rotate the key, set a message's outcome). Registered at a host-chosen level, like support.
- System: `mailbox:postmaster:read` (list and open unrouted mail; every open audited), `mailbox:postmaster:manage` (route a message to a team). Never team-level.

## Out of scope here

- Match hints (tracking reference, diarienummer) — FortDocs FD-678.
- Sending from an item with a tracking reference — FortDocs FD-769; may later add an outbound operation here.

## Done when

Rotation, routing, postmaster, retention and content handover work and are tested; README and a new `docs/articles/team-mailbox.md` describe them; `MAJOR_MINOR` bumped (public API added); FortDocs runs against it end to end.
