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
- [x] 9. `search_my_cases(text)`: the member's own cases by subject and by what was written in them, so the assistant can find a case and then read it with `get_case_messages`.
  - Done: words of three letters or more, cut to a rough stem, matched in the subject and then in every message; the newest 100 of the caller's cases, at most 10 matches with the words and where they were found, through `GetMyCasesAsync` and `GetMessagesAsync`, so only cases the caller may read. `get_case_messages` now points at it. `SupportAssistantToolsTests`: 9 tests.
- [x] 10. What the assistant could not do: a `report_missing_skill` tool the responder always adds, `SupportAnswer.Gaps`, `SupportCase.AssistantGaps` with a store method (MongoDB and in-memory), recorded by the service.
  - Done: `report_missing_skill(what)` is added by `ChatSupportResponder` beside the host's tools, whatever they are; what it is told comes back as `SupportAnswer.Gaps`. The service keeps them, trimmed to 200 characters, once each, whether the assistant answered or declined, through `ISupportCaseStore.AddAssistantGapsAsync` (a default that keeps nothing). MongoDB pushes them keeping the newest 20 (`SupportCaseLimits.MaxAssistantGaps`). The default instructions tell the model to call it. Tests: 6 in Support (one through the real tool loop), 2 in MongoDB.
- [x] 11. `SupportAssistantGapsView`: staff list, across teams, of what the assistant could not do, opening the case; a badge on the desk's case. On the sample's support desk.
  - Done: `SupportAssistantGapsView` reads every page across teams, one entry per thing asked (case-insensitive), most asked first, each case opening as `SupportCaseConversation` for staff; a note without `support:all:read`. `SupportAcrossTeamsView` shows **The assistant could not help** with the gaps on hover. On the sample's desk. The sample's scripted client reports every question off its script. `SupportAssistantGapsViewTests`: 5 tests.
- [x] 12. Docs, full build (baseline 14) and the full suite; run the sample.
  - Docs: the support article (who the assistant answers, the built-in tools, what it could not do), the notifications article (`WithAssistant`, the built-in routes skip assistant conversations), the `Tharga.Team.Blazor` README and the sample README. Build at 14 warnings; full suite green: Email 985, MongoDB 460, Blazor 2160, Service 1470, Support 768, Support.Anthropic 12, Mcp 121, Entra 58, Images 11.

Asked for while testing, second round (2026-10-24):

- [x] 13. The side panel could not be resized: the drag sized the panel inside Radzen's sidebar, which kept its own width, and the saved width was drawn only on the next render. The script now sizes the sidebar, and `OnResized` redraws. Test: a dragged width is drawn and remembered.
- [x] 14. Answers with links: `SupportAssistantOptions.CaseLinkTemplate` (null by default; the sample sets `/support/{caseId}`) gives every case `get_my_cases` and `search_my_cases` return a `Link`, and their descriptions tell the model to link in Markdown. The panel and, now, the support page render the assistant's Markdown; `SupportAnswerLinks.SiteOnly` keeps a link only when it is a path on the site, and writes any other out as text. Tests: 8 in Blazor (bare addresses are not linked by RadzenMarkdown), 2 in Support.
- [x] 15. Rating answers up or down: on the assistant's answers in the panel and on the support page, by the person answered; kept on the message, counted on the case, audited; marked on the desk and listed in the feedback view with what it could not do.
  - Done: `SupportAnswerRating` (None, Up, Down) on `SupportMessage.Rating`, counted as `SupportCase.AnswersRatedUp`/`AnswersRatedDown`; `RateAnswerAsync` lets only the author rate, only the assistant's answers, audited as `rate-answer`, announced as `SupportCaseChange.Rated`; `ISupportCaseStore.SetAnswerRatingAsync` (default throws), MongoDB by the entry's position, counts read from the entries. Thumbs in the panel and the support page, the same again taking it back; a mark for support. `SupportAssistantGapsView` became `SupportAssistantFeedbackView`, adding the rated cases; the desk marks them. Tests: 5 Support, 3 MongoDB, 4 + 1 + 3 Blazor.
- [x] 16. Docs, full build (baseline 14) and the full suite; run the sample.
  - Docs: the support article (answers with links; what it could not do and how its answers were rated) and the `Tharga.Team.Blazor` README. Build at 14 warnings; full suite green: Email 985, MongoDB 463, Blazor 2177, Service 1470, Support 775, Support.Anthropic 12, Mcp 121, Entra 58, Images 11.

## Decisions

- The user asked for a simple loop counter (2026-10-24), so the round and the tool's own name are shown as they are.
  Friendlier wording per tool can come with the steps recorded with the answer.

## Last session

2026-10-24: steps 2 to 12 done and committed. Next: the user tries the search, the gap list and Slack staying quiet in the sample; then the close-out and the PR, after #384 merges.
