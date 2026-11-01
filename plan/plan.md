# Plan: MCP tools for the support AI connection (#425)

Branch `feature/mcp-support-ai-connection`.

## Steps
- [x] 1. Read the issue, the REST endpoints, the service and the MCP provider conventions; the open question answered (see `feature.md`).
- [x] 2. Tests first: listing (opt-in, no team, no service), each tool calls the service for the caller's team, results and refusals, the key never returned, kind parsing, DI resolution both ways (`SupportAiConnectionToolProviderTests`, 31 tests).
- [x] 3. Implemented `SupportAiConnectionToolProvider` and `McpTeamOptions.ExposeSupportAiConnection`; registered in `AddTeam` only when asked.
- [x] 4. The architecture tests still pass (no `ITeamService` injected); a test pins the constructor to the checked service alone.
- [x] 5. Docs: the `Tharga.Team.Mcp` README section and a paragraph in the support-cases article beside the REST one.
- [x] 6. Full suite green (Mcp 152; Service 1,543; Blazor 2,383; Email 985; Support 912; MongoDB 486; Entra 58; Anthropic 17; OpenAI 16; Images 11); `dotnet outdated` clean.
- [ ] 7. Close-out, only when the user says to push: archive this plan to `$DOC_ROOT/Tharga/plans/Toolkit/Platform/done/mcp-support-ai-connection.md`; `git rm -r plan`; final commit `feat: mcp-support-ai-connection complete`; push; PR with release-note text; then close #425.

## Notes
- Not exercised over a real MCP connection with a real API key; the service is substituted in the tests. The refusal for a caller without `assistant:manage` is the service's and is tested there (`SupportAiConnectionServiceTests`).
- Kinds are read by name only, where REST also reads numbers.
- Possible follow-up: a scope-aware listing (hide the tools from a caller without `assistant:manage`). Not done because the scope's name lives in the Support package, which this package does not reference.

## Last session
2026-11-01: steps 1-6 done and committed on the branch, not pushed. Next: the user decides on pushing, then step 7.
