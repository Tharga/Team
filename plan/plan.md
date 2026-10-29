# Plan: Support assistant usage per team (#378)

## Steps

- [x] 1. NuGet update: `dotnet outdated` reports nothing outdated (2026-10-29, after #427 merged).
- [x] 2. Confirmed 2026-10-29: usage records are deleted when the team is purged; ships as a patch (4.6.4).
- [x] 3. `SupportAssistantUsage` gained `CachedInputTokens`, `Provider` and `Model`. `ChatSupportResponder` reads the
      provider and default model from the client's `ChatClientMetadata`, the model from the response's (or last
      update's) `ModelId`, and the cached tokens from `UsageDetails.CachedInputTokenCount`. The per-message MongoDB
      entity and its mapping carry the three too.
- [~] 4. Recording: tests first (an answer and a decline each give one record with everything but text; a host
      responder's usage is recorded; none gives none; a throwing sink is logged and the answer still appended), then
      the record, the sink interface and `RunAssistantAsync`.
- [ ] 5. Read side: tests first (totals per team, provider and model; one team or all; refused without the scope),
      then the store interface, the service, its authorization, the scope and registration.
- [ ] 6. MongoDB store: tests first (write, total, purge), then the collection, index and registration.
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
