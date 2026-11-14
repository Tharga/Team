# Plan: every outgoing mail in the mail log (#485)

- [x] Branch `feature/mail-log-every-outgoing-mail` from master (51080d77); issue #485 filed.
- [~] NuGet update across the solution (`chore(deps): nuget update`), full suite green.
- [ ] From, Reply-To and sender on every logged mail (contracts, entity, dispatcher, history item; staff and contact messages set the sender). BSON representation tests for any new enum.
- [ ] Support answers and receipts recorded in the log as their own kinds, with a case reference; bounce and resend update the row.
- [ ] `IMailOutcomeSink`, called once on the transition to given up, refused or bounced.
- [ ] `IUserMailHistoryService.GetSentAsync(id)`, filtered to the sender or `users:manage`.
- [ ] `UserMailLogView`: From, Reply-To and Sent-by columns, new kinds, case link.
- [ ] Docs: `user-mail.md`, `support-cases.md`, README.
- [ ] Close-out: re-check NuGet, archive `feature.md` to the Plan directory `done/`, remove `plan/`, push, PR.

## Last session

- 2026-11-14: branch and plan created.
