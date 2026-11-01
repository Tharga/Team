# Plan: external support case (#430)

Branch `feature/external-support-case`, from `master` at 29ff551.

## Steps
- [x] 1. Open questions settled with the user (see `feature.md`: separate interface, contact record on the case, receipt mail off by default).
- [x] 2. Tests first: the entry point opens an unassigned case with an Email binding to the visitor, never in a team, with suggestions (`ExternalSupportCaseTests`).
- [x] 3. Tests first: limits (required address, plain-address check, field and body lengths, inclusive bounds), consent stored, host fields kept.
- [x] 4. Implemented: `ExternalSupportContact`, `SupportCase.ExternalContact`, `IExternalSupportCaseService` (core), `ExternalSupportCaseService` (Support).
- [x] 5. Audit entry `support:raise` with `support.case.source = web-form` (`AuditingExternalSupportCaseServiceDecorator`); the host notification is `Raised`, from a channel, no team.
- [x] 6. Persistence: `SupportExternalContactEntity` on `SupportCaseEntity`, host fields stored as name/value pairs (a field name may hold a dot or start with a dollar sign); mapping tests; JSON round trip test.
- [x] 7. Optional receipt (`SupportCaseOptions.External.SendReceipt`, `ReceiptText`), off by default; its message id joins the thread; failure never loses the case.
- [x] 8. Answer path: a staff answer on the case is mailed to the visitor's address (test through `EmailSupportChannel`).
- [x] 9. Docs: article section "A public contact form" with the whose-job-is-what table, package README pointer.
- [x] 10. Full suite green (Service 1,543; Blazor 2,379; Email 985; Support 912; MongoDB 486; Mcp 121; Entra 58; Anthropic 17; OpenAI 16; Images 11).
- [ ] 11. Close-out: re-run `dotnet outdated`; PR description (release-note style); archive this plan to `$DOC_ROOT/Tharga/plans/Toolkit/Platform/done/external-support-case.md`; `git rm -r plan`; final commit `feat: external-support-case complete`; push and PR only with the user's yes, after the user has tested.

## Notes
- The opening message is marked `Source = Email` and `Delivery = Sent`, as a mailed case's is, so the reply path never mails the visitor their own message back. What shows it came from a web form is the audit entry and `SupportCase.ExternalContact`.
- A web-form case has no inbound Message-Id: the binding's thread id is generated as `{caseId}@{domain of the mailbox address}`. With a receipt, the receipt's own id joins the thread too, and that is the one a reply names.
- The audit action is `raise`, not a new one, so a notification route on `support:raise` also fires for a web-form case; `support.case.source` tells them apart.
- Not done, possible follow-ups: show the contact (phone, organisation, consent) in the staff case view; a sample contact form page in the sample site; a non-primary mailbox for web-form cases (the primary is used).
- Not exercised against a real mail server or MongoDB; the mapping is tested against the entity, the sending against a substituted client.

## Last session
2026-11-01: steps 1-10 done and committed on the branch, not pushed. Next: the user decides on pushing and testing (a sample form would help), then the close-out sequence in step 11.
