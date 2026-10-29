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
- [x] 6. MongoDB: `SupportAssistantUsageRecordEntity` (the per-message `SupportAssistantUsageEntity` already had the
      name) in `SupportAssistantUsage` (`ThargaTeamOptions.SupportAssistantUsageCollectionName`), indexes `At_TeamKey`
      and `TeamKey`. Totals by a server-side `$match`/`$group` on the collection, as the mail statistics are; tests
      render the pipeline. `MongoSupportAssistantUsageStore` registered with `TryAddScoped`, and
      `SupportAssistantUsageStoreSink` with `TryAddEnumerable`, handing each record to whichever store is registered.
- [x] 7. `SupportAssistantUsageView`: From / To date pickers (this month to today by default, whole days, the last
      included), a table row per team, provider and model (unnamed ones as "Unknown") with a total row; team names from
      `ITeamDirectoryService` where the reader may read it, keys otherwise; "not permitted" without the scope.
      `SupportAssistantUsageViewText`. Added to the guard's list of services from optional packages.
- [x] 8. Sample: `SystemSupportScopes.UsageRead` on the Developer role, and the view on `/support-desk` ("What the
      assistant used"). In the browser the card rendered with this month's period and "The assistant was not used in
      this period", nothing being recorded before this change. The sample's scripted client reports tokens and names
      itself, so asking the assistant in the panel gives a row; left for the user's testing, which also exercises
      the server-side totals on a real MongoDB.
- [~] 9. Docs.
- [ ] 10. Full test suite, push the branch for the user to test.

## Notes

- Versioning: proposed as a patch (4.6.4), as the last two additive releases were; `MAJOR_MINOR` stays `4.6`.

## Last session

2026-10-29: #427 (#421, #422) merged. Branch created from `master`, nothing outdated, plan written. Next: the user
confirms the plan.
