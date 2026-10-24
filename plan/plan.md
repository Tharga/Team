# Plan: the assistant shows its steps while it works

- [x] 1. Package updates: `dotnet outdated` over the solution (2026-10-24, at #376's close-out). Nothing outdated.
- [x] 2. `SupportAssistantProgress`, and the progress overloads of `RunAssistantAsync` and `ISupportResponder.AnswerAsync` take it.
  - Done: a record with `AnswerSoFar`, `Round` (from 1) and `Tools`. It replaces `IProgress<string>`, unreleased, in the service, both decorators and the responder port.
- [x] 3. `ChatSupportResponder`: rounds and tools from the stream. Tests through the real tool loop.
  - Done: reports round 1 at the start, the tools on each `FunctionCallContent`, and the next round on the first `FunctionResultContent` after them. Checked through the real `FunctionInvokingChatClient`: both come through its stream.
- [x] 4. Blazor: the panel and the support page show the round and the tools. Texts in `SupportTranscriptText`.
  - Done: `Round` and `RoundTools` in `SupportTranscriptText`, also in `SupportPanelText.All`. `SupportAssistantSteps.Describe` writes the line; `SupportAssistantRelay` replaces `SupportAnswerSoFar`. `SupportTranscript` and `SupportCaseConversation` take `AssistantProgress` in place of `AnswerSoFar`.
- [x] 5. Sample: the scripted client calls `get_my_cases` for a question about cases.
  - Done: it asks for the tool when the question mentions a case and the tool is offered, then answers with the count; the lookup pauses 0.8 s each side so the round shows.
- [x] 6. Docs, full build (baseline 14) and the full suite; run the sample.
  - Docs: the streaming section of the support article describes rounds and tools; the `Tharga.Team.Blazor` README.
  - Build at 14 warnings, full suite green; the sample shows Round 1 and Round 2 for a question about cases.

Asked for while testing (2026-10-24):

- [x] 7. Only the person who raised a case has the assistant answer it. Anybody else gets nothing done and false, so the tools always read with the rights of the person being answered.
  - Done in the authorization decorator, both overloads: after the write check, the case's author must be the caller; anybody else gets false and the responder is not asked. Test: support across teams cannot run it, the author still can.
- [x] 8. No Slack while the assistant has the conversation: `support.case.withAssistant` on the raise and reply audit entries, a `WithAssistant` filter on `NotificationRoute`, and the default support routes set to false.
  - Done: the audit decorator writes `support.case.withAssistant` on raise (from the raised case) and on reply (the case read after the reply; left out when it cannot be read, which then posts). Case threads already skipped assistant conversations, so the posts came from the default routes alone.
- [~] 9. `search_my_cases(text)`: the member's own cases by subject and by what was written in them, so the assistant can find a case and then read it with `get_case_messages`.
- [ ] 10. What the assistant could not do: a `report_missing_skill` tool the responder always adds, `SupportAnswer.Gaps`, `SupportCase.AssistantGaps` with a store method (MongoDB and in-memory), recorded by the service.
- [ ] 11. `SupportAssistantGapsView`: staff list, across teams, of what the assistant could not do, opening the case; a badge on the desk's case. On the sample's support desk.
- [ ] 12. Docs, full build (baseline 14) and the full suite; run the sample.

## Decisions

- The user asked for a simple loop counter (2026-10-24), so the round and the tool's own name are shown as they are.
  Friendlier wording per tool can come with the steps recorded with the answer.

## Last session

2026-10-24: steps 2 to 6 done and committed (the round counter). The user asked for steps 7 to 11 after trying it; step 7 in progress.
