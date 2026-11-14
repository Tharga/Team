# Feature: every outgoing mail in the mail log, and an outcome hook

Issue: #485

## Goal

The global mail log lists every mail the host sends — user mail, support answers and contact-form receipts — with the From and Reply-To it was actually sent with and who sent it. When a mail ends as given up, refused or bounced, the host is told once, so it can notify the sender.

## Scope

- From, Reply-To and sender (`SentByUserKey`, `SentByName`) recorded on every logged mail and carried through `UserMail`, `UserMailEntity`, `UserMailItem`, `UserMailDelivery` and `UserMailHistoryItem`. The effective From and Reply-To are recorded when the dispatcher sends.
- Staff messages and contact messages set the sender they already know.
- Support answers and contact-form receipts recorded as their own `UserMailKind` values, with a reference to the case (team key, case id, message sequence). Rows are written completed (Sent or Failed), so the dispatcher never claims them. A support-answer bounce and a resend update the same row.
- `IMailOutcomeSink`: every registered sink told once on the transition to given up, refused or bounced; a throwing sink is logged and the others still run.
- `IUserMailHistoryService.GetSentAsync(id)`: a filtered read of one mail for its sender or a `users:manage` holder.
- `UserMailLogView`: From, Reply-To and Sent-by columns, the new kinds in the kind filter, an optional link from a support answer to its case.

## Out of scope

- Retrying support answers automatically (resend stays manual).
- Any cross-instance delivery of the outcome: the sink is called on the instance that observed it, and relaying is the host's.

## Acceptance criteria

- A support answer appears in the log with From, Reply-To, sender and delivery status, and its status follows a bounce and a resend.
- Every log row shows From and Reply-To; rows queued before this change show them empty rather than failing.
- A mail given up after its retries calls every sink exactly once; a bounce read twice, or on two instances, calls them once.
- `GetSentAsync` returns the mail to its sender and to a `users:manage` holder, and nothing to anyone else.
- The change is additive: no existing public signature changes.

## Done when

All acceptance criteria hold, the full suite is green, `docs/articles/user-mail.md`, `support-cases.md` and the README describe the new fields, kinds and hook, and the PR is open.
