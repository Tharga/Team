# Plan: Support AI providers (#421, #422)

## Steps

- [x] 1. NuGet update: `dotnet outdated` reports nothing outdated (checked 2026-10-29, right after #424 merged).
- [x] 2. Confirmed 2026-10-29: ships as a **patch** (4.6.3), so `MAJOR_MINOR` stays `4.6`. MCP tools become their
      own issue (draft the text, show it before filing). The address rule stays: https, or http on this machine.
- [x] 3. Several factories. `CompositeSupportChatClientFactory` (internal) wraps every registered factory; the service
      and the resolver take it, so neither changed. A kind is reached through the last registered factory offering
      it, and offered once. `AddThargaSupportAiProviders` now registers its concrete factory and forwards to it with
      `TryAddEnumerable`, so a second call changes the options and adds nothing, and a host's own factory stays.
- [x] 4. Keyless kinds. `SupportAiProviderDefault.RequiresApiKey` (default true). The enum got `OpenAI`, `Ollama` and
      `LlamaCpp` here, as the tests needed a self-hosted kind. With no key given and none stored for the same kind
      and address, a keyless kind is reached with none, and connecting stores none; the resolver answers a keyless
      connection with no key. An empty address says "Enter the address." before the https check.
- [x] 5. `Tharga.Team.Support.OpenAI`: `OpenAIChatClientFactory` on `Microsoft.Extensions.AI.OpenAI` 10.11.0 and `OpenAI`
      2.14.0, `OpenAISupportOptions`, `AddThargaSupportOpenAIProviders` (registered like the Anthropic one, so the
      two sit side by side). No key sends no `Authorization` header (a pipeline policy takes out the SDK's stand-in).
      The model list has no display names, so the id is shown. 16 tests against a fake server. Added to the solution,
      the pack step in `build.yml`, and `Tharga.Team.Blazor.Tests` (its guard searches every shipped package).
- [x] 6. View: a provider is any kind but Platform and Off, so the form shows for the new kinds. New text keys
      `KindOpenAI`, `KindOllama`, `KindLlamaCpp`, and `ApiKeyOptional` ("Only if the server asks for one") under the
      key field of a kind that needs none. A self-hosted kind starts with an empty address.
- [x] 7. `MapThargaSupportAiConnection(path)` (`SupportAiConnectionEndpointRegistration`, default
      `/api/support/assistant/connection`), returning the route group, behind `ThargaApiPolicyName`. Team from the
      `TeamKey` claim (400 without one), 403 from the service's own refusal, 422 with why for a failed model list, test
      or connect, 204 for platform and off, 400 for a body that is not a request (without the parser's text, which can
      quote the key). Kinds by name in JSON. 16 tests through a test server and the real service with its
      authorization. Gotcha noted in the code: a handler taking only `HttpContext` binds as a request delegate and
      drops its result.
- [~] 8. Sample: register the new package and map the endpoints; check in a browser and over HTTP.
- [ ] 9. Docs: `docs/articles/support-cases.md`, the new package's README, the root README, `Tharga.Team.Support`'s
      README if it lists the registrations.
- [ ] 10. Full test suite, push the branch for the user to test.

## Notes

- Versioning: `MAJOR_MINOR` in `.github/workflows/build.yml` stays `4.6`; the user chose a patch.
- `Tharga.Team.Support.OpenAI` is a new package: it needs adding to the solution and the build's pack list.

## Last session

2026-10-29: #424 (#418–#420) merged as 4.6.2. Branch created from `master`, nothing outdated, plan written. Next: the
user confirms the plan.
