# Plan: support-send-as

Legend: [x] done, [~] in progress, [ ] to do

- [x] 1. `chore(deps): nuget update` — everything applied, majors included. Held: MailKit 4.18.2 (#469, already recorded), Microsoft.CodeAnalysis.CSharp 4.8.0 (analyzer compiler floor, recorded), Swashbuckle.AspNetCore 10.3.1 (new: v11 needs Microsoft.OpenApi 3.x, AspNetCore.OpenApi 10.0.15 needs 2.x, NU1107). 6835 tests pass, as before.
- [x] 2. Plan confirmed with the user (2026-11-12): host declares send-as addresses and domains; staff-address feature off until `StaffSenderRole` is set.
- [x] 3. Options: `MailOptions.SendAs` (`MailSendAs`) and `SendAsDomains`, `SupportAnswerOptions.StaffSenderRole`, `NameTheAnswerer`, `Closing`; `OutboundMail.From`; `MailSendResult.SenderRefused`.
- [x] 4. Sender resolution as one pure unit, `SupportAnswerSenders` (allowed set, default chain, choose/refuse, delivered-to address), with tests.
- [x] 5. Sender on the message: `SupportMessage.SentFrom` and `SignedWithName`, persisted in Mongo; `ISupportCaseService` overloads `AnswerCaseAsync` and `PreviewAnswerAsync` with a `SupportAnswerChoice`; decorators pass it through; the service refuses an address that is not one of the route's senders.
- [x] 6. `EmailSupportChannel`: From and name per message, Reply-To always a support address (with tracker), resend keeps the stored sender, tracker read back for any send-as address (`CaseReplyAddress.FindByRecipientAsync`).
- [x] 7. Route and preview: `SupportAnswerRoute.Senders`, `SignWithName`; preview shows From and Reply-To.
- [x] 8. Blazor: sender picker and "Sign with my name" in `SupportAnswerRouteLine`, bound in `SupportCaseConversation`, `SupportUnassignedConversation` and `SupportQueueView`; text keys; render tests.
- [x] 9. Signature: closing template (`team.support.mail.closing`), `NameTheAnswerer`, per-answer choice, quoted-entry attribution, text and HTML, preview parity.
- [x] 10. Docs: `docs/articles/support-cases.md` and `Tharga.Team.Support/README.md`. Full test run: 6882 pass.
- [~] 11. Push the branch for the user to test (needs the user's word). Then, on their word: final `feat: support-send-as complete` commit without `plan/`, close #462, #473, #463 with evidence, PR.

## Decisions
- No silent retry when the server refuses a sender: the entry fails with the address named, so `SentFrom` always matches what went out.
- `SupportAnswerRoute.ReplyTo` was dropped; reading a route must not write a tracking token. The preview carries Reply-To.
- Public `SupportSender` already existed (inbound sender directory), so the answer-side type is `SupportAnswerSender`.

## Last session
Steps 1 to 10 done on `feature/support-send-as`, 6882 tests green. Next: user tests from the pushed branch, then close-out, then #474 and #464 as their own PRs.

## README / docs notes for completion
Docs are already updated. Release notes (PR description) must say: new `ISupportCaseService` overloads (implementers and decorators update), new `MailOptions.SendAs` / `SendAsDomains`, new `SupportAnswerOptions.StaffSenderRole` / `NameTheAnswerer` / `Closing`, and that an answer now goes from the address the customer wrote to when it is a declared send-as address.
