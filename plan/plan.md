# Plan: Support assistant usage per team (#378)

## Steps

- [x] 1. NuGet update: `dotnet outdated` reports nothing outdated (2026-10-29, after #427 merged).
- [x] 2. Confirmed 2026-10-29: usage records are deleted when the team is purged; ships as a patch (4.6.4).
- [x] 3. `SupportAssistantUsage` gained `CachedInputTokens`, `Provider` and `Model`. `ChatSupportResponder` reads the
      provider and default model from the client's `ChatClientMetadata`, the model from the response's (or last
      update's) `ModelId`, and the cached tokens from `UsageDetails.CachedInputTokenCount`. The per-message MongoDB
      entity and its mapping carry the three too.
- [x] 4. `SupportAssistantUsageRecord` and `ISupportAssistantUsageSink` in `Tharga.Team`. `SupportCaseService` takes every
      sink (and a logger), times the responder call, and after it returns tells every sink, answered or declined,
      when the answer has usage. The asker is the authorizer's subject; tool calls are the answer's service calls.
      A throwing sink is logged and the rest are still told. Four tests, one checking no written text is in the record.
- [x] 5. `ISupportAssistantUsageStore : ISupportAssistantUsageSink` (`GetTotalsAsync(from, to, teamKey)`,
      `DeleteForTeamAsync`), `ISupportAssistantUsageService` and `SupportAssistantUsageTotal` in `Tharga.Team`;
      `SystemSupportScopes.UsageRead` (`support:usage:read`), registered with a description. The service reads the
      store (empty without one) behind a decorator requiring the system scope. Purge participant when a store is
      registered, a no-op one otherwise. The existing guard counting `support:` system scopes went from 4 to 5.
- [~] 6. MongoDB store: tests first (write, total, purge), then the collection, index and registration.
- [ ] 7. `SupportAssistantUsageView`: tests first (rows and total for the period; nothing without the scope), then
      the view and text keys.
- [ ] 8. Sample: a staff page with the view, the scope on a system role; check in a browser.
- [ ] 9. Docs.
- [ ] 10. Full test suite, push the branch for the user to test.

## Notes

- Versioning: proposed as a patch (4.6.4), as the last two additive releases were; `MAJOR_MINOR` stays `4.6`.

## Last session

2026-10-29: #427 (#421, #422) merged. Branch created from `master`, nothing outdated, plan written. Next: the user
confirms the plan.
