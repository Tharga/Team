# Plan: mail-log

- [x] 1. NuGet check: nothing new beyond the recorded holds.
- [x] 2. Sender fields: `UserMail.SentByUserKey/FromAddress/CaseId`, route and sender on the outbox item, delivery and history item; `IMailClient.DefaultFrom`; the dispatcher records the effective From and Reply-To; staff and contact messages fill the sender.
- [x] 3. Support mail in the log: `SupportAnswer` and `SupportReceipt` kinds, written done by the mail channel and the contact-form receipt, one row per answer, kept in step on resend and bounce, never in a user's own history.
- [x] 4. `IMailOutcomeSink` (given up, refused, bounced), once on the transition, a throwing sink never stops the others.
- [x] 5. `IUserMailHistoryService.GetMailAsync`: the sender or `users:manage`; everyone else refused.
- [x] 6. `UserMailLogView`: From, Reply-To, Sent by, case, the two new kinds. Tests and docs.
- [~] 7. Push for testing; on the users word close-out, PR, close #485.

## Decisions
- The sink contract and notifier live in `Tharga.Team`, not `Tharga.Team.Email`: the transport namespace must not expose team types.
- The outbox gets default members (`MarkSentAsync` with a route, `AddCompletedAsync`, `SetOutcomeAsync`) so an outbox written earlier keeps compiling; the new ones throw until it implements them.
- One support answer is one row, keyed `support:{case}:{sequence}`: sending it again updates the row, so the log says what the case says.
- A mail log that cannot be written never costs an answer, a receipt or a case.
- The sender of a support answer is the signed-in user at the time the channel posts, which covers answering and sending again.
- The outcome carries the mail's id and no address; the sender reads the rest through `GetMailAsync`.

## Release notes
Additive. New on `UserMail`: `SentByUserKey`, `FromAddress`, `CaseId`; on `UserMailBatch`: `SentByUserKey`; `UserMailKind.SupportAnswer` and `SupportReceipt`; `IMailClient.DefaultFrom`; `IMailOutcomeSink`/`MailOutcome`; `IUserMailHistoryService.GetMailAsync`; default members on `IUserMailOutbox`. `UserMailLogView` shows From, Reply-To, Sent by and the case.
