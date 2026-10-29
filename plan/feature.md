# Feature: Support AI providers — OpenAI-compatible kinds, and endpoints over the connection

Branch: `feature/support-ai-providers` (public repo, so no issue number in the name)

Closes Tharga/Team#421 and #422. #421 adds kinds; #422 exposes the same service over HTTP, so doing #421 first means
the endpoints are written once, for every kind.

## Goal

A team can connect the support assistant to OpenAI, Ollama or llama.cpp as it can to Anthropic and Neurolito, and a
host can offer every step of choosing that connection over its REST API without writing its own controller.

## Scope

### #421 — OpenAI, Ollama and llama.cpp

- **Three new kinds** on `SupportAiConnectionKind`, added at the end: `OpenAI`, `Ollama`, `LlamaCpp`. The store keeps
  the kind as a string, so nothing stored changes. An enum, not an open provider key: an open key would change the
  type of `Kind` everywhere it is read, which breaks every host; three members break nobody.
- **A new package, `Tharga.Team.Support.OpenAI`**, beside `Tharga.Team.Support.Anthropic`: one
  `ISupportChatClientFactory` for the OpenAI-compatible API (`GET {address}/models`, `POST {address}/chat/completions`),
  on `Microsoft.Extensions.AI.OpenAI` (same version line as the `Microsoft.Extensions.AI` the toolkit uses) and the
  `OpenAI` SDK. Registration `AddThargaSupportOpenAIProviders(o => …)`, with `OfferOpenAI`, `OfferOllama`,
  `OfferLlamaCpp` (all default `true`), the addresses each starts from, and `MaxOutputTokens`.
  - **The address includes the API's version path**, as each provider documents it: `https://api.openai.com/v1`,
    and for self-hosted servers whatever the organisation gives (Ollama's `…:11434/v1`, llama.cpp's `…:8080/v1`).
  - The model list is `/models`, with the same plain-words problems the Anthropic factory returns (refused key, no
    model list at this address, unreachable, too slow, not an AI provider, no models), never with the key.
- **Several factories at once.** Today one `ISupportChatClientFactory` is resolved and the Anthropic registration
  removes any other, so a host could not install both packages. The service and the resolver take every registered
  factory and pick, per kind, the **last registered** factory that offers it (the DI convention for overriding). Each
  package's registration replaces only its own factory. A host's own factory now sits beside the toolkit's instead
  of being removed by `AddThargaSupportAiProviders`.
- **Keyless self-hosted kinds.** `SupportAiProviderDefault` gains `RequiresApiKey` (`init`, default `true`, so the
  positional constructor and every existing factory are unchanged). Ollama and llama.cpp set it `false`:
  - The service accepts a connect, test or model list without a key for such a kind, and stores no key.
  - **The address is required for them**: they start from no address (unless the host sets one), because the
    server's own localhost is not the organisation's server, and the service refuses an empty address.
  - A stored key is still sent only where it was entered for: changing kind or address with no new key sends none.
- **Addresses keep today's rule**: https anywhere, http only on the same machine, never credentials in the address.
  A self-hosted server reached over the network needs https (a reverse proxy in front of Ollama or llama.cpp).
- `SupportAssistantConnectionView`: the three kinds get labels (new text keys), the key field says "optional" for a
  keyless kind, and a provider is anything but Platform and Off rather than a fixed pair.

### #422 — endpoints over the connection

- `MapThargaSupportAiConnection(path = "/api/support/assistant/connection")` in `Tharga.Team.Support`, mapped by the
  host like `MapThargaSupportSlack`, behind the Tharga API policy (`ApiKeyConstants.ThargaApiPolicyName`), so API keys
  reach it as they reach the audit API.

  | Method | Path | Does |
  |---|---|---|
  | GET | `{path}` | The connection, without the key (`SupportAiConnection`) |
  | GET | `{path}/choices` | What the team may choose (`SupportAiConnectionChoices`) |
  | POST | `{path}/models` | The models a provider offers for a key or the stored one |
  | POST | `{path}/test` | Asks the model one short question |
  | PUT | `{path}` | Connects to a provider: the key is accepted here and never returned |
  | PUT | `{path}/platform` | Uses the platform's model |
  | PUT | `{path}/off` | Turns the assistant off for the team |

- **The team comes from the credential, never a parameter**, as on the audit API: a team key's own team, or the team
  a system key names in `X-Team-Key`. No team, 400.
- **No authorization in the endpoints.** The service's decorator already requires `assistant:manage` on the team and
  the audit decorator records each change, so the endpoints inherit both. A refusal is 403.
- A failed connect or test is 422 with the `SupportAiConnectionResult`, so a client reads why from the body.

## Acceptance criteria

- [ ] A team can choose OpenAI, Ollama or llama.cpp where the host registered the new package, list the models, test,
      and save; the assistant then answers through it.
- [ ] Ollama and llama.cpp connect without a key and refuse an empty address; OpenAI requires a key.
- [ ] Installing both provider packages offers all five provider kinds; each kind is reached through its own factory.
- [ ] A stored key is never sent to a different kind or address.
- [ ] Each endpoint does what its service method does, for a caller with `assistant:manage` on the team, and refuses
      (403) one without. No response carries a key, not even in an error.
- [ ] Docs (`docs/articles/support-cases.md`, the new package's README, the root README) describe the new kinds, the
      package and the endpoints.
- [ ] Full test suite passes.

## Done condition

All acceptance criteria met, the user has tested from the pushed branch, and the PR closes #421 and #422.

## Out of scope

- **MCP tools over the connection** (asked for as "ideally" in #422). `Tharga.Team.Mcp` does not reference
  `Tharga.Team.Support`, so either package would take on a new dependency; proposed as its own issue.
- Http to a private network address. Allowing it would let anyone with `assistant:manage` make the server call
  internal hosts; that needs its own decision, not a side effect of adding kinds.
- #378 (token usage per team) follows on its own branch.
