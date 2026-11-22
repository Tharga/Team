# Plan: support assistant allowance

Legend: [x] done, [~] in progress, [ ] to do.

- [x] 0. NuGet update (commit c106ce6). Applied Microsoft.Extensions.AI and .OpenAI 11.0.1 on net11.0; full suite 16150 passed. Held, with recorded reasons: MailKit 4.18.2 (existing hold in the csproj), Swashbuckle 10.3.1 on net10.0 (existing, Microsoft.OpenApi), Serilog.AspNetCore 10.0.0 in the sample (11.0.0 pulls .NET 11 packages into a net10.0 app; comment added), Microsoft.CodeAnalysis.CSharp 4.8.0 (stays until CI builds on the .NET 11 SDK), and every net10.0 package that would jump to 11.x.
- [~] 1. Confirm the plan and the open decisions with the user.
- [ ] 2. Cache-write pricing: `PriceCacheWrite`, `CacheWriteTokens`, `CostOf`, store mapping (BSON by name where enums, test of stored shape), `ChatSupportResponder.UsageOf` reads `CacheCreationInputTokens`. Tests first.
- [ ] 3. Allowance contract and default implementation over the usage store; options for the limit; scope-gated service plus architecture-test compliance; registration by the library with TryAdd.
- [ ] 4. Readiness: `AllowanceUsed`, `ProviderRefused`; `ChatSupportResponder` classifies account-level provider refusals; cases are not handed over in those states.
- [ ] 5. Panel: progress bar, administrator amounts and questions left, hidden on own provider, refresh after an answer and through the live relay.
- [ ] 6. Refusal actions by role, including the ask-administrators request.
- [ ] 7. Host slot and refusal hook on `SupportPanel`.
- [ ] 8. Texts, README, docs articles.
- [ ] 9. Full suite on both targets, push the branch for the user to test.

## Last session
Branch created, dependencies updated and committed, plan written. Next: confirm the plan and decisions below, then start step 2.

## README changes needed when complete
Support assistant section: allowance, progress bar, refusal actions, host slot and hook; pricing of cache writes.
