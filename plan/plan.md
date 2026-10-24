# Plan: the support assistant's chat client, chosen per team (#377)

- [x] 1. Package updates: `dotnet outdated` over the solution. Nothing outdated, so there is no `chore(deps)` commit.
- [x] 2. `SupportAssistantReadiness` in `Tharga.Team`, and `ISupportResponder.GetReadinessAsync` as a default member answering ready. Tests: the default, and a host responder unchanged.
  - Done: `NotRegistered`, `NotConfigured`, `Ready`. The zero value is `NotRegistered`, so an unset readiness never reads as ready. `SupportAssistantReadinessTests`: 2 tests, and the support suite is green at 659.
- [x] 3. `ISupportChatClientResolver` and its default over the registered `IChatClient`. `ChatSupportResponder` resolves per answer and reports readiness, declining when a team has no client. Tests: per-team selection, null meaning `NotConfigured`, and the default resolver.
  - Done: `RegisteredChatClientResolver` (internal) is the default. A resolver that throws reads as `NotConfigured` for readiness and declines an answer, because raising a case must not fail on unreadable settings. The registration passes the default resolver for now; step 4 makes it a service. `ChatSupportResponderTests`: 6 tests, the first the responder has had. Support suite 665.
- [x] 4. Registration: the responder exists when an `IChatClient` or a host resolver is registered, and not otherwise. Tests in the registration suite, covering both orders of registration.
  - Done: the default resolver is a `TryAddScoped` factory over the registered `IChatClient`. The responder is null only when the default is in place and there is no `IChatClient`. `SupportAssistantRegistrationTests`: 4 tests, each resolving the responder (container validation cannot see into the factory). Support suite 669.
- [~] 5. `ISupportCaseService.GetAssistantReadinessAsync`: in the service; behind membership in the authorization decorator; passed through by the auditing decorator. `RaiseCaseAsync` uses it. Tests: the three readiness states, a refusal for a non-member, and a raise for a team that is not ready going to people.
- [ ] 6. `SupportCasesView` offers the assistant from the team's readiness, and shows a short notice offering a person when the assistant does not answer (FortDocs' panel does the same at `AssistantPanel.razor:471`). Extend `SupportCasesViewOfferedTests`, and add a render test for the notice.
- [ ] 7. Docs: the assistant section of `docs/articles/support-cases.md`, the `Tharga.Team.Support` README, and XML docs on the new public members.
- [ ] 8. Full build (warning baseline 14) and the full test suite, then push the branch for the user to test.

## Decisions

- Readiness is a value with a reason rather than a yes or no, as FortDocs' `AssistantReadiness` is, so a component can say why the assistant is missing.
- The resolver lives in `Tharga.Team.Support`, beside `ChatSupportResponder`, because it names `IChatClient`. `Tharga.Team` stays free of AI types.
- A decline, whether the client is missing or the provider is down, shows a notice on screen offering a person, and nothing is stored on the case. FortDocs had to add that canned reply in its own panel because the toolkit was silent.
- FortDocs checked (2026-10-24): one `IChatClient` for the whole environment (`AddFortDocsAssistant`, an API key per environment), so nothing per team to copy. Its panel uses the toolkit's support assistant (`RaiseCaseAsync` with `SupportAssistance.Assistant`, `RunAssistantAsync`, `RequestHumanAsync`), so it will consult this readiness. For #376: it offers human support per team through its own feature check plus a change event (`AssistantPanel.razor:566-586`).

## Last session

2026-10-24: plan confirmed. #381 filed for the configurable connection (platform model, Neurolito, Anthropic) and #377 commented. Step 2 done; step 3 (the resolver) next.
