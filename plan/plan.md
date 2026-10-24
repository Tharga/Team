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

Asked for while testing, third round (2026-10-24): whether support is online, and a word when nobody answers.

- [x] 17. Who is on the desk: `SupportStaffStatus` (Online, Away, Hidden) and `SupportDeskPresence`, an in-process record of each support person's choice and when they last acted.
  - Done: Online while one chose Online and acted within `SupportCaseOptions.StaffIdleAfter` (10 minutes); Away while somebody is known and nobody is online, Hidden included; Unknown while nobody is known. One timer, for whoever goes idle first, raises `Changed`. `SupportPresence` combines the desk with Slack (asked only when the desk has nobody online) and is always registered; `ISupportPresence.Changed` is a default member that never raises. Tests: 15 in `SupportDeskPresenceTests`.
- [x] 18. The service: `SetMyStaffStatusAsync`, `GetMyStaffStatusAsync` and `ReportStaffActivityAsync`.
  - Done: for `support:all:manage` or `support:unassigned:manage`, anybody else refused; not audited. Answering as support and writing as support count as acting, only for somebody who has set a status. Tests: 8 in `SupportStaffStatusTests`, one through the real registration.
- [x] 19. `SupportDeskStatus`: the switch (Online, Away, Hidden) with what each means, remembered in the browser; it starts at what another page of theirs set, else the browser's, else Online. `support-desk.js` reports use of the page while it is in front, at most once a minute. Renders nothing for anybody not on the desk, and follows a choice made on another page.
  - Decided: no "shown as away while idle" line. Using the page is what brings them back, so it would show only for the moment before they move the mouse.
- [x] 20. Members see it: `SupportPresenceIndicator` ("Support is online", "Support will reply later", reusing the support page's texts) under the panel's header on the Support tab, and above a conversation with a person on the support page; nothing while unknown or with the assistant. `SupportPresenceWatch` follows `Changed`.
- [x] 21. The notice: `SupportNoAnswerWatch` takes the wait from the member's first message since anybody else wrote, notes left out, and calls back when it runs out. `NoAnswerYet` after `NoAnswerAfter` (60 seconds; on `SupportPanel`, `SupportCasesView` and `SupportCaseConversation`), `NoAnswerAway` at once while away; not while support is writing, not on a closed case or with the assistant, gone when support answers. `SupportTranscript` takes it as `Notice`, on support's side. Tests: 7 watch, 4 conversation, 5 panel.
- [x] 22. Sample: `SupportDeskStatus` at the top of the support desk page; the sample README.
- [x] 23. Docs, full build and the full suite; run the sample.
- [x] 24. Hidden is not counted at all (the user, 2026-10-24): with only Hidden people on the desk, members see no indicator, not "Support will reply later". Away beside Hidden is still away.
  - Done: `SupportDeskPresence` counts only Online and Away; the hint, the docs and the READMEs say so. Tests: 3 more in `SupportDeskPresenceTests`. Build at 14 warnings; full suite green (Support 804, Blazor 2209).
  - Docs: the support article's "Is anybody on support" (the desk and Slack, what members see), the `Tharga.Team.Blazor` README, the sample README. Build at 14 warnings; full suite green: Email 985, MongoDB 463, Blazor 2209, Service 1470, Support 802, Support.Anthropic 12, Mcp 121, Entra 58, Images 11. The sample runs.

Asked for while testing, fourth round (2026-10-24): the round counter looked strange; show the service calls made.

- [x] 25. Beside the dots, in words: "First lookup…", "Second lookup…", from the eleventh "Lookup 11…", counting the service calls while one runs; "Working…" otherwise. No round numbers on screen. A lookup part way through the text is said under it.
  - Done: `SupportAssistantSteps.Describe` from `SupportAssistantProgress.Calls`; `Lookup`, `LookupOrdinals` (comma-separated words) and `LookupNumbered` replace `Round` and `RoundTools`, which were not released.
- [x] 26. The service calls behind an answer: `SupportAssistantCall` (name, what it was asked for written out as `text: "export"`) on `SupportAnswer.Calls`, `SupportAssistantProgress.Calls` and `SupportMessage.AssistantCalls`; kept bounded (`MaxAssistantCalls` 20, `MaxAssistantCallArgumentsLength` 200); MongoDB `SupportAssistantCallEntity`. Shown under the answer in the panel and the transcript, folded as "2 service calls", a row per call such as `search_my_cases(text: "export")`.
  - Done: read from the tool loop in one piece and streamed; `report_missing_skill` is not a service call. The sample's script also searches (`search_my_cases(text: …)`) for a question about cases that names something. Tests: 4 Support, 1 contract, 3 MongoDB, 6 + 2 Blazor. Build at 14 warnings; full suite green: Email 985, MongoDB 466, Blazor 2216, Service 1470, Support 809, Support.Anthropic 12, Mcp 121, Entra 58, Images 11.

- [x] 27. The lookup words are shown beside the dots, not only read out: `SupportTypingDots.ShowLabel`, set while a lookup runs. "Working…" and somebody typing stay screen-reader text.
- [x] 28. Configurable: `o.Blazor.Support` (`SupportDisplayOptions`): `RateAnswers` (default true) takes the thumbs and the marks away; `ServiceCallsShownTo` (`Everyone` default, `Support`, `Nobody`) decides who sees the calls. Display only; what is kept stays. Read through `SupportDisplay.Of`, defaults where the Blazor options are not registered.
  - Done: tests 6 + 1 Blazor, and the panel's label checks. `get_my_cases` takes no arguments by design (the team and the person are the caller's), so `get_my_cases()` is right. Build at 14 warnings; full suite green (Blazor 2223).

- [x] 29. Text beside the dots, always (the user still saw dots alone: "First lookup…" showed only while a call ran, under a second in the sample, and never for a model that calls nothing): "Working…", then "Working n…" where n counts the tool loops, kept while the model reads what came back.
  - Done: `SupportAssistantSteps.Describe` takes the idle text; `Running` for the line under text already written; `WorkingLoop` ("Working {0}…") replaces the lookup texts, which were not released. The support page's `AssistantThinking` (released) now defaults to "Working…" so both places read alike. Build at 14 warnings; full suite green (Blazor 2224).

- [x] 30. The text first, the animated dots last: "Working 2 •••", the dots standing for the "…" the texts no longer end with. The line under text already written reads the same, dots included.
- [x] 31. The tokens with the service calls (#381 step 3, usage per answer): `SupportAssistantUsage` (input, output, total) on `SupportAnswer.Usage` and `SupportMessage.AssistantUsage`, MongoDB `SupportAssistantUsageEntity`. In one piece from the tool loop's response, which adds its rounds up (checked through the real `FunctionInvokingChatClient`); streamed, each round's `UsageContent` added up here; none reported is null, not zero. `SupportAnswerDetails` shows "2 service calls · 830 tokens", opening to the calls and "Tokens: 610 in, 220 out", in the panel and the transcript. `ServiceCallsShownTo` became `AnswerDetailsShownTo` (`SupportAnswerDetailsAudience`), unreleased. The sample's script estimates its tokens at four characters each.
  - Not yet: a usage row per answer for billing and per-team totals behind a system scope (#378, and the rest of #381 step 3).
  - Tests: 4 Support, 2 MongoDB, 3 Blazor. Build at 14 warnings; full suite green: Email 985, MongoDB 468, Blazor 2231, Service 1470, Support 814, Support.Anthropic 12, Mcp 121, Entra 58, Images 11.

- [x] 32. A stored key shows as a mask in the key field, so it does not look empty: `StoredKeyMask` (U+2022, which no key holds) while the key would be kept; the mask is taken out of whatever is typed, and nothing left keeps the stored key. The hint says "A key is stored. Type a new one to replace it." (unreleased text).
- [x] 33. Rating is off by default (`RateAnswers = false`); a host turns it on. The sample keeps the default.
  - Tests: 4 connection view, 2 display, the rating tests now turn it on. Build at 14 warnings; full suite green (Blazor 2236).

## Decisions

- The user asked for a simple loop counter (2026-10-24), so the round and the tool's own name are shown as they are.
  Friendlier wording per tool can come with the steps recorded with the answer.
- Online (2026-10-24, the user's choices): each support person picks Online, Away or Hidden, and Online counts as
  away after ten minutes of doing nothing in support. Hidden works unseen and is not counted at all (changed
  the same day: at first it counted as away). With nobody known and no Slack, members see no indicator. The no-answer notice is shown, not stored.
- The desk record is in-process, like typing: on several instances, each knows only the support people connected to
  it. Presence is advisory, so a wrong "away" costs a notice, never a refused question.

## Last session

2026-10-24: steps 2 to 23 done and committed. Next: the user tries the desk switch, the indicator and the one-minute notice in the sample; then the close-out and the PR.
