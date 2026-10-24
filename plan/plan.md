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
- [~] 6. Docs, full build (baseline 14) and the full suite; run the sample.
  - Docs: the streaming section of the support article describes rounds and tools; the `Tharga.Team.Blazor` README.

## Decisions

- The user asked for a simple loop counter (2026-10-24), so the round and the tool's own name are shown as they are.
  Friendlier wording per tool can come with the steps recorded with the answer.

## Last session

2026-10-24: steps 2 to 5 done; step 6 (verification and the user trying it) in progress.
