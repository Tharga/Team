# Plan: support assistant allowance

Legend: [x] done, [~] in progress, [ ] to do.

- [x] 0. NuGet update (commit c106ce6). Applied Microsoft.Extensions.AI and .OpenAI 11.0.1 on net11.0; full suite 16150 passed. Held, with recorded reasons: MailKit 4.18.2 (existing hold in the csproj), Swashbuckle 10.3.1 on net10.0 (existing, Microsoft.OpenApi), Serilog.AspNetCore 10.0.0 in the sample (11.0.0 pulls .NET 11 packages into a net10.0 app; comment added), Microsoft.CodeAnalysis.CSharp 4.8.0 (stays until CI builds on the .NET 11 SDK), and every net10.0 package that would jump to 11.x.
- [x] 1. Plan confirmed 2026-11-22: units not money on screen (configurable), default in options plus per-team override, ask-administrators last (step 6b).
- [x] 2. Cache-write pricing done. `PriceCacheWrite` (catalogue model, usage record, Mongo, admin view and text), `CacheWriteTokens` (usage, record, totals, per-message usage), new `CostOf` overload with writes (old overload kept; a write with no write price costs as input, as before), `ChatSupportResponder.UsageOf` reads `CacheCreationInputTokens`. The key was checked as a string literal in the Anthropic 13.12.0 assembly, not by running the SDK's conversion. Reasoning tokens are not added (output already includes them). 26 new test runs, suite 16176 passed on both targets.
- [x] 3. Allowance contract done. Contracts in `Tharga.Team/SupportAssistantAllowance.cs`: `SupportAssistantAllowance` (share, state, optional amounts, per-model cost and questions left), `ISupportAssistantAllowanceSource` (host-implementable, `EditorBrowsable(Never)`, default `DefaultSupportAssistantAllowanceSource` sums this month's cost from the usage store), `ISupportAssistantAllowanceService` (members read the share, `assistant:manage` also reads amounts), `ISupportAssistantLimitStore` + `ISupportAssistantLimitService` (per-team override, system scope `support:allowance:manage`, audited, purged with the team), Mongo collection `SupportAssistantLimit`. Options: `SupportAssistantOptions.Allowance` (`DefaultLimit`, `Display` Share/Units/Tokens/Money default Units, `UnitValue`, `UnitLabel`, `WarnAt`, `AverageOverDays`). Registered by `AddThargaSupportCases`, TryAdd so a host's own source wins. Suite 16250 passed on both targets. NOT done in this step: nothing yet refuses an answer (step 4), no UI (step 5), no REST/MCP surface for the limit service (decide whether to add; the catalogue and connection both have one).
- [~] 3b. (open question) REST endpoints and MCP tools for the limit and allowance services, as the connection and catalogue have.
- [ ] 4. Readiness: `AllowanceUsed`, `ProviderRefused`; `ChatSupportResponder` classifies account-level provider refusals; cases are not handed over in those states.
- [ ] 5. Panel: progress bar, administrator amounts and questions left, hidden on own provider, refresh after an answer and through the live relay.
- [ ] 6. Refusal actions by role: other platform model, own subscription, host actions.
- [ ] 6b. Ask-administrators request (built last).
- [ ] 7. Host slot and refusal hook on `SupportPanel`.
- [ ] 8. Texts, README, docs articles.
- [ ] 9. Full suite on both targets, push the branch for the user to test.

## Last session
Branch created, dependencies updated and committed, plan written. Next: confirm the plan and decisions below, then start step 2.

## README changes needed when complete
Support assistant section: allowance, progress bar, refusal actions, host slot and hook; pricing of cache writes.
