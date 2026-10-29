# Feature: Support assistant usage per team

Branch: `feature/support-assistant-usage` (public repo, so no issue number in the name)

Closes Tharga/Team#378.

## Goal

Every time the support assistant is asked, one usage record is kept: which team, which case, who asked, which provider
and model, the tokens, the tool calls and how long it took. Never the prompt or the answer text. Staff can read the
usage per team for a period, so a host lending its own AI account to teams can see what each one used.

## Today

Each answer keeps its tokens on the message (`SupportMessage.AssistantUsage`: input, output, total). Missing for
billing: the provider and the model, cached input tokens, the tool-call count, the duration, the asker, and **any
answer the assistant declined**, whose tokens were spent but are not kept anywhere. There is no rollup per team.

## Scope

- **`SupportAssistantUsage` gains** `CachedInputTokens`, `Provider` and `Model` (all optional, so a host's own
  responder that sets none still works). `ChatSupportResponder` fills them: cached tokens from
  `UsageDetails.CachedInputTokenCount`, the provider from the chat client's `ChatClientMetadata`, the model from the
  response's `ModelId`, falling back to the metadata's default model. The per-message copy gains them too.
- **A usage record** (`SupportAssistantUsageRecord`): team, case, the asker's identity, provider, model, input / cached /
  output / total tokens, tool calls, whether it answered, duration, time. No text.
- **Written by `SupportCaseService.RunAssistantAsync`** after the responder returns, answered or declined, whenever
  the answer carries usage. Written there rather than in `ChatSupportResponder` so a host's own responder is
  recorded too, by returning `Usage` and nothing more. The duration is measured around the responder call.
  **A failing sink never fails the answer**: it is logged and skipped, as a throwing audit sink is.
- **`ISupportAssistantUsageSink`** (in `Tharga.Team`): `RecordAsync(record)`. Every registered sink gets every record,
  so a host can also forward usage to its own billing.
- **`ISupportAssistantUsageStore : ISupportAssistantUsageSink`** adds the read: totals per team, provider and model
  for a period, optionally one team. MongoDB implementation in `Tharga.Team.MongoDB`, registered by
  `AddThargaTeamRepository` like the other support stores, with an index on time and team.
- **`ISupportAssistantUsageService`** (read side): `GetUsageAsync(from, to, teamKey = null)`, gated by a new system
  scope `SystemSupportScopes.UsageRead` (`support:usage:read`), granted to nobody by default; a host maps it to a
  system role as it does `support:all:read`.
- **`SupportAssistantUsageView`** (Blazor, for staff): a period (this month by default), and a row per team, provider
  and model with answers, declines, tool calls and tokens, and a total. Shown only to a caller with the scope.
- **Purge**: a team's usage records are removed when the team is purged, like its AI connection (decided 2026-10-29).

## Acceptance criteria

- [ ] An answer and a decline each write one record with the team, case, asker, provider, model, tokens, tool calls,
      outcome and duration, and nothing written by the person or the model.
- [ ] A host's own responder that returns `Usage` is recorded the same way; one that returns none writes nothing.
- [ ] A sink that throws does not stop the answer being appended.
- [ ] `GetUsageAsync` totals per team, provider and model for the period, for one team or all, and refuses a caller
      without `support:usage:read`.
- [ ] The view shows the totals for the chosen period, and nothing to a caller without the scope.
- [ ] The MongoDB store writes, totals and purges; the sample shows the view on a staff page.
- [ ] Docs (`docs/articles/support-cases.md`, `Tharga.Team.MongoDB` and root README where they list stores and scopes).
- [ ] Full test suite passes.

## Done condition

All acceptance criteria met, the user has tested from the pushed branch, and the PR closes #378.

## Out of scope

- A price per provider and model, and charging (the issue's "later").
- A team administrator's own view of the team's usage (a team scope); staff only for now.
- Retention: records are small, one per answer; no expiry yet.
