# Plan: support-send-as

Legend: [x] done, [~] in progress, [ ] to do

- [x] 1. `chore(deps): nuget update` — everything applied, majors included. Held: MailKit 4.18.2 (#469, already recorded), Microsoft.CodeAnalysis.CSharp 4.8.0 (analyzer compiler floor, recorded), Swashbuckle.AspNetCore 10.3.1 (new: v11 needs Microsoft.OpenApi 3.x, AspNetCore.OpenApi 10.0.15 needs 2.x, NU1107). 6835 tests pass, as before.
- [~] 2. Plan confirmed with the user
- [ ] 3. Options: `MailOptions.SendAs` and `SendAsDomains`, `SupportAnswerOptions.StaffSenderRole`, `NameTheAnswerer`, closing template. Registration validation (addresses parse; send-as addresses unique per mailbox).
- [ ] 4. Sender resolution as one pure unit (default chain, allowed set, refusal) with tests: default from delivered-to, explicit choice, unknown refused, staff address allowed and not allowed, no role, automatic mail.
- [ ] 5. Sender on the message: `SupportMessage` records the `From` it went out with; store, Mongo entity and in-memory store. Posting takes the chosen sender; `ISupportCaseService` overloads keep existing callers compiling; authorization and audit decorators pass it through.
- [ ] 6. `EmailSupportChannel`: From and display name per message, Reply-To always the support address with tracker, SMTP sender-rejection fallback, resend uses the stored sender.
- [ ] 7. Route and preview: `SupportAnswerRoute` lists allowed senders and the default; preview shows From and Reply-To.
- [ ] 8. Blazor: picker in `SupportCaseConversation` and `SupportUnassignedConversation`, text keys, render tests.
- [ ] 9. Signature: closing template, `NameTheAnswerer`, per-answer opt-out, quoted-entry attribution, text and HTML, preview parity.
- [ ] 10. Docs: README and docs/ articles. Full test run. Push the branch for the user to test.
- [ ] 11. On the user's word: final `feat: support-send-as complete` commit without `plan/`, close issues with evidence, PR.

## Notes
- SDK 10.0.301 runs the suite fine locally via `dotnet test` on the solution (6835 tests); the zero-tests problem in the shared notes did not occur.
