# Support assistant: spend allowance per team

Source: GitHub issue #552 on Tharga/Team. Branch: `feature/support-assistant-allowance` (public repo, so no issue number in the name).

## Goal
A host that answers teams with a platform model can cap what each team spends, show a team how much is left, and, when the allowance or the provider's account is used up, refuse with a way out instead of a generic failure.

## In scope (this PR)
1. **Cost fix.** Price cache writes: nullable `PriceCacheWrite` on `PlatformAiModel` and `SupportAssistantUsageRecord`, a `CacheWriteTokens` count on the record and totals, read from `AdditionalCounts["CacheCreationInputTokens"]`. `CostOf` takes the write count and price. Reasoning tokens are not added twice. Stored enums and fields follow the persistence rules, with a test of the stored shape.
2. **Allowance contract.** `ISupportAssistantAllowance` the host can implement: per team the limit, the amount used, the currency, when the period ends, and per offered platform model the team's recent average cost per answer. Default implementation sums `ISupportAssistantUsageStore` over a calendar month; the limit comes from options, and a host replaces it for its own limits and its own model calls. Gated by scope like the other team-bound services (see shared instructions, service layering).
3. **Readiness.** New `SupportAssistantReadiness.AllowanceUsed` and `ProviderRefused`, added after `Ready` so the zero value stays `NotRegistered`. A case raised in either state is not handed to the assistant, and the reason is surfaced. `ProviderRefused` is set from an account-level refusal by the provider (usage or spend limit, billing, revoked key), not from one bad request.
4. **Progress bar** at the top of the assistant tab: share used, warning colour from 80%, full at 100%. Administrators also see amounts and "about N questions left on <model>". Hidden for a team on its own provider. Refreshed after each answer and through the existing live relay for other open panels.
5. **Refusal with actions**, by role: choose another platform model (with estimated cost per answer and questions left beside each), connect the team's own subscription via `SupportAssistantConnectionView`, and for a non-administrator a request to the team's administrators carrying a link to where the choice is made.
6. **Host extension points.** A `RenderFragment` slot at the top of the assistant tab and a hook that lets the host refuse a question with its own message and actions (the issue's stated minimum, and what the host-specific "buy more" builds on).
7. Texts through the existing text provider, tests for every new function, README and docs updates.

## Deferred, with the trigger that unparks it
- **Exchange rate / host currency.** The allowance is kept in the catalogue currency. Unparks when a host needs to bill in another currency; records would then store the rate used.
- **Counting abandoned (timed-out) answers.** Unparks if the allowance proves to undercount in practice; needs the usage known at the timeout.

## Acceptance
- An answer's cost includes cache writes at their own price; a record keeps the prices it was given at.
- A team over its allowance, or refused by the provider, is not answered, sees why, and is offered the actions for its role.
- A team on its own provider shows no bar and is never limited.
- Full test suite passes on net10.0 and net11.0.

## Done condition
User confirms; PR merged to master; issue #552 closed with evidence (types, members, tests, version).
