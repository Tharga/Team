# Plan: every outgoing mail in the mail log (#485)

- [x] Branch `feature/mail-log-every-outgoing-mail` from master (51080d77); issue #485 filed.
- [x] NuGet update across the solution (`chore(deps): nuget update`), full suite green. AngleSharp 1.10.3 taken. Held: the .NET 11 line (Microsoft.* / System.* 11.x, Microsoft.Extensions.AI 11.x) until spring 2027, now commented beside each reference; MailKit 5 (existing hold, #469) and Swashbuckle 11 (existing hold, NU1107 against Microsoft.AspNetCore.OpenApi 10) unchanged; Microsoft.CodeAnalysis stays 4.8 (oldest compiler the supported SDKs ship). 6 950 tests green across 11 test projects.
- [x] From, Reply-To and sender on every logged mail. `UserMailHeaders` record; `UserMail`/`UserMailBatch` gain `SentByUserKey`/`SentByName`; `IUserMailOutbox` gains default-implemented overloads of MarkSent/MarkSkipped/MarkFailed/Postpone taking the headers, so a host outbox keeps working; the dispatcher resolves the headers once per attempt and writes them with every outcome (a never-delivered mail still shows what it would have been sent with); entity field `EffectiveReplyTo` beside the existing per-mail `ReplyTo` override. Sender shown in the global log only, not in a user's own history. Email 1026, MongoDB 527, Blazor 2454, Service 1700, Support 1001 green; 16 warnings.
- [~] Support answers and receipts recorded in the log as their own kinds, with a case reference; bounce and resend update the row.
- [ ] `IMailOutcomeSink`, called once on the transition to given up, refused or bounced.
- [ ] `IUserMailHistoryService.GetSentAsync(id)`, filtered to the sender or `users:manage`.
- [ ] `UserMailLogView`: From, Reply-To and Sent-by columns, new kinds, case link.
- [ ] Docs: `user-mail.md`, `support-cases.md`, README.
- [ ] Close-out: re-check NuGet, archive `feature.md` to the Plan directory `done/`, remove `plan/`, push, PR.

## Last session

- 2026-11-14: branch and plan created.
