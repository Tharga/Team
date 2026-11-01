# Feature: MCP tools for a team's support AI connection (#425)

Branch `feature/mcp-support-ai-connection`, from `master` at 671d964.

## Need
`MapThargaSupportAiConnection` exposes a team's AI connection over REST, but `Tharga.Team.Mcp` has no tools for it, so an agent connected over MCP cannot read or change what the support assistant answers a team with.

## Answer to the open question in the issue
**None of the three options is needed.** `ISupportAiConnectionService` and every type it uses (`SupportAiConnection`, `SupportAiConnectionChoices`, `SupportAiProviderRequest`, `SupportAiModelList`, `SupportAiConnectionResult`) live in the core `Tharga.Team` project, which `Tharga.Team.Mcp` already references through `Tharga.Team.Service`. The tools can live in `Tharga.Team.Mcp` against that interface, and the implementation (registered by `AddThargaSupportAiConnections`) is resolved from DI per request. No new package, and neither package gains a dependency.

## Scope
- A tool provider in `Tharga.Team.Mcp`, team scope, with seven tools mirroring the REST endpoints: read the connection, read the choices, list models, test, connect, use the platform's model, turn the assistant off.
- Authorization is the service's, as for REST: it requires `assistant:manage` on the team and audits every change (a denial too). The provider adds no check of its own and never touches `ITeamService`.
- The key is accepted by `connect`, `test` and `list_models` and never returned, logged or audited.
- The tools appear only when the host opted in (`McpTeamOptions.ExposeSupportAiConnection`, off by default), the service is registered, and the caller has a team.

## Decisions
- Opt-in, off by default (my choice, not asked): REST needs an explicit `Map...` call, so an upgrade does not expose a new management surface to agents that hold `assistant:manage`; the MCP equivalent of that call is this option. Same shape as `ExposeSystemResources`.
- No scope check on listing: the service enforces `assistant:manage` on every call, and the scope's name lives in the Support package, which the MCP package does not reference.

## Not in scope
- A new package, or a dependency between Support and Mcp.
- Streaming, resources for the connection (tools only, as asked).

## Done when
- An agent with `assistant:manage` can read, test, connect, switch to the platform's model and turn the assistant off over MCP.
- One without it is refused, by the service.
- The key never comes back.
- README documents the opt-in and the tools.
