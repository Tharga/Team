# Plan: each team's AI connection (#381, first PR)

- [x] 1. Package updates: `dotnet outdated` over the solution. Nothing outdated, so there is no `chore(deps)` commit.
- [x] 2. Contracts in `Tharga.Team`: `SupportAiConnectionKind`, `SupportAiConnection`, the request and model records, `ISupportAiConnectionService`, `ISupportAiConnectionStore` and its stored record, plus the `assistant:manage` scope constant. Tests: the zero value is `Platform`, and the read model has no member that can carry a key.
  - Done: results are values rather than exceptions for anything a person setting it up can get wrong (`SupportAiModelList.Problem`, `SupportAiConnectionResult`); authorization stays an exception. `SupportAiProviderRequest.ToString` hides the key, because a record prints every member. `SupportScopes.AssistantManage` sits beside the support scopes. `SupportAiConnectionContractTests`: 5 tests.
- [~] 3. `ISupportChatClientFactory` and the endpoint it is given (`Tharga.Team.Support`).
- [ ] 4. `SupportAiConnectionService`: key encrypted with its own Data Protection purpose, address validation, verification by listing models before saving, and keeping the stored key when only the model changes. Authorization decorator (`assistant:manage`), auditing decorator (no key), purge participant. Tests for each, including a search of the stored record, audit entries and exceptions for the key's text.
- [ ] 5. `StoredChatClientResolver`: Platform or no record gives the registered client; Off gives null; a provider gets a client from the factory, reused until the connection changes. Tests, including readiness through the service.
- [ ] 6. Registration: `AddThargaSupportAiConnections()` in Support, which swaps in the stored resolver and registers the scope, the service and the purge participant. Registration tests that resolve everything.
- [ ] 7. MongoDB: `MongoSupportAiConnectionStore`, the Data Protection key ring (`IXmlRepository` over a collection) with its opt-in registration, and a `Microsoft.AspNetCore.DataProtection` package reference in `Tharga.Team.MongoDB`. Tests.
- [ ] 8. `Tharga.Team.Support.Anthropic`: project, test project and solution entries. `AnthropicChatClientFactory` (`AnthropicClient` with `BaseUrl`, `AsIChatClient`, model listing) and `AddThargaSupportAnthropic()`. Tests against a fake HTTP handler: base address, key header, model list. CI pack line.
- [ ] 9. `SupportAssistantConnectionView` (`Tharga.Team.Blazor`) and its texts. Render tests: the current choice, only what the host offers, the key never echoed, load and pick a model, test, save, and a refusal shown.
- [ ] 10. Sample: the Anthropic package, the key ring, and an "AI assistant" page under Support in `SampleNavigation`.
- [ ] 11. Docs: a new support-cases section on choosing each team's AI connection, the new package's README, the root README (table and tree), and the `Tharga.Team.Support` README.
- [ ] 12. Full build (baseline 14) and the full suite. Push for testing on approval, then test against the user's Neurolito key.

## Decisions

- One SDK for both providers: Neurolito exposes the Anthropic Messages API (`GET /v1/models`, `POST /v1/messages`) and accepts the key as `X-API-KEY` or a bearer token (checked in `neurolito-meta/Server`, `AnthropicController`).
- The package is `Tharga.Team.Support.Anthropic`, named after the dependency it quarantines, as Entra and Images are (user's choice, 2026-10-24).
- A team may turn the assistant off (user's choice). A team that never chose gets the platform's model.
- The API key is the toolkit's first secret stored at rest. Team mail keys are addresses, not secrets. Its Data Protection purpose is its own, so a key protected for another purpose cannot be read as this one.
- The key ring sits in `Tharga.Team.MongoDB` because Mongo implementations belong there (architecture v4). The DataProtection package reference it needs is pruned for every ASP.NET Core consumer.
- Verification before saving is listing the provider's models: it proves the address and the key without spending tokens. Test is a separate, explicit action that sends one short message.

## Last session

2026-10-24: plan written, waiting for confirmation before any code.
