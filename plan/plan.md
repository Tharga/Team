# Plan: support-answer-by-mail

- [x] 1. NuGet check: nothing new beyond the recorded holds.
- [x] 2. Contracts: `ISupportCaseContactResolver`, `SupportAnswerRoute.MailOffer`, `SupportAnswerChoice.ByMail`, `SupportRaiseOptions` and the `RaiseCaseAsync` overload (default member).
- [x] 3. Service: the offer on the route, the first mail answer binds the case, preview of it, raise bound to mail; authorization and audit (`switch-to-mail`, address and mailbox on raise).
- [x] 4. Blazor: Answer by mail in `SupportCaseConversation`.
- [x] 5. Tests and docs.
- [~] 6. Push for testing; on the users word close-out, PR, close #461.

## Decisions
- The address comes only from the host's resolver (or from host code raising the case), never from the one answering, so staff cannot redirect a case's answers.
- The binding records the chosen support address as the address the case "came in on", so later answers default to it, as for a case that arrived by mail. A staff member's personal address is never recorded as it.
- A sender the answerer may not use is refused before the case is switched, so a refusal never leaves a case bound to mail with nothing sent.
- No default resolver is registered: no resolver means no offer, which is "default returns null" without an extra type.
- The offer is a nested `SupportAnswerRoute` rather than new route fields, so the route line and the preview work on it unchanged.

## Release notes
Additive. New `ISupportCaseContactResolver` (register one to be offered *Answer by mail* on site-only cases), `SupportAnswerRoute.MailOffer`, `SupportAnswerChoice.ByMail`, `SupportRaiseOptions` and `ISupportCaseService.RaiseCaseAsync(..., SupportRaiseOptions)` (default member). Decorators of `ISupportCaseService` must forward the new overload. Texts: `team.support.answerRoute.answerByMail`.
