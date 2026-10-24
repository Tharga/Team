# Feature: each team's AI connection, chosen in a component (#381, first PR)

GitHub: #381 (for SpartaBok). Builds on #377 (`ISupportChatClientResolver`, readiness). Branch:
`feature/support-ai-connection`. This PR is the issue's step 2. Usage per answer (step 3) and the Anthropic preset
(step 4) follow as their own PRs.

## Goal

A team administrator chooses, in a component, what the support assistant answers their team with:

1. **The platform's model:** the `IChatClient` the host registers. This is also the default for a team that
   never chose.
2. **Off:** no assistant for this team.
3. **Neurolito:** the team's own Neurolito key, with the model picked from what Neurolito offers that key.

The Anthropic kind is part of the contract from the start, but it is offered only from step 4.

## Scope

- **Contracts in `Tharga.Team`:**
  - `SupportAiConnectionKind` (`Platform`, `Off`, `Neurolito`, `Anthropic`). The zero value is `Platform`, so a
    team with no record gets the platform's model.
  - `SupportAiConnection`, the read model: the kind, base address, model, whether a key is set, and who set it
    and when. **Never the key.**
  - `ISupportAiConnectionService`, with operations rather than CRUD: read the connection and the choices,
    use the platform's model, turn the assistant off, connect to a provider, list a provider's models, and test
    a connection.
  - `ISupportAiConnectionStore`, the persistence port in the domain's language.
- **Service in `Tharga.Team.Support`:**
  - The API key is encrypted with Data Protection under a purpose of its own, and is never returned, logged, or
    audited by value.
  - A base address must be https, or http on loopback for a local server.
  - A provider connection is verified by listing its models before it is saved.
  - Authorization by a new team scope, `assistant:manage`, at Administrator level and re-levelable like the
    support scopes. Changes are audited under `assistant`, without the key.
  - Purging a team removes its connection.
- **`StoredChatClientResolver`:**
  - Platform, or no record: the registered `IChatClient`, or none.
  - Off: null, so the team is `NotConfigured`.
  - A provider: the factory builds the client from the decrypted key.
  - Clients are reused per team until its connection changes.
- **`ISupportChatClientFactory`**, the port that turns a provider connection into an `IChatClient` and lists the
  provider's models.
- **`Tharga.Team.Support.Anthropic`**, a new package quarantining the `Anthropic` SDK. It implements the factory
  for the Anthropic Messages API (`AnthropicClient` with `BaseUrl`, `AsIChatClient`), which Neurolito also
  speaks. Registered with `AddThargaSupportAnthropic()`.
- **MongoDB (`Tharga.Team.MongoDB`):**
  - The connection store.
  - A Data Protection key ring persisted in MongoDB, opt-in, modelled on FortDocs' `MongoXmlRepository`, so
    every instance can read the stored keys after a restart or scale-out.
- **Component in `Tharga.Team.Blazor`:** `SupportAssistantConnectionView`.
  - It shows the team's current choice, and offers only what the host can provide.
  - For Neurolito: the base address (prefilled), the key (write-only, with "replace"), load models, pick one,
    test, save.
  - Texts go through the text provider, and it depends on `ISupportAiConnectionService` alone.
- **The sample** wires it up and gets an "AI assistant" page.

## Out of scope (later PRs of #381)

- Usage per answer (step 3), and offering Anthropic (step 4).
- REST or MCP for the connection.
- The semi-agentic assistant, which is its own issue.

## Acceptance criteria

- A team with no record, or with Platform chosen, is answered by the registered `IChatClient`. Without one, it
  is `NotConfigured`.
- Off makes the team `NotConfigured`: the assistant is not offered and cases go to people.
- A Neurolito connection answers with the chosen model, through the team's own key.
- The key never leaves the service: not in the read model, logs, audit entries or exceptions. It is stored
  encrypted, and a stored record does not contain it in clear.
- Only a holder of `assistant:manage` on the team reads or changes the connection. Members still get readiness
  by membership.
- A connection that cannot list its models is refused, and nothing is saved.
- Purging a team removes its connection.
- With the MongoDB key ring registered, keys are written to and read from MongoDB.
- The component works end to end in the sample against a real Neurolito key (the user's), as well as in render
  tests.
- Docs: the support-cases article, the new package's README, the root README, and the CI pack list.

## Done when

All criteria hold, the full suite passes, the build stays at the warning baseline (14), and the user has tested
from the pushed branch and said it is done.
