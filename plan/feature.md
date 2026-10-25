# Feature: Anthropic as a support-assistant provider (#381, step 4 of 4)

GitHub: #381 (steps 1–3 shipped in #382, #383 and #385). Branch: `feature/support-anthropic-preset`, from master
after 4.5.0.

## Goal

A team can connect the support assistant to its own Anthropic API key, the same way it connects to Neurolito: enter
the key, load the models, pick one, test and save.

## What is already there (verified 2026-10-25)

- `SupportAiConnectionKind.Anthropic` and its label (`SupportAssistantConnectionViewText.KindAnthropic`).
- The choices (`SupportAiConnectionService.GetChoicesAsync`), the stored-connection resolver and the view all follow
  `ISupportChatClientFactory.Providers`, and the view already treats `Anthropic` as a provider.
- `AnthropicChatClientFactory` speaks the Anthropic Messages API with the official `Anthropic` SDK 13.3.0, which sends
  the key as `Authorization: Bearer` (documented on `ClientOptions.ApiKey`), as Anthropic's API accepts. Only the
  address differs between the two providers.
- What is missing: the factory offers only Neurolito.

## Scope

- `AnthropicSupportOptions.AnthropicBaseAddress`, default `https://api.anthropic.com`.
- The factory offers Anthropic beside Neurolito, at that address.
- Tests: both providers offered; the address is the host's to change; listing models from an Anthropic-shaped
  catalogue (with `created_at`) at that address with the key as a bearer token; choices include Anthropic.
- Docs: `support-cases.md`, the `Tharga.Team.Support.Anthropic` README and package description, the
  `Tharga.Team.Support` README (its "Planned" line is stale: usage per answer shipped in 4.4), the root README row.

## Done when

- With `AddThargaSupportAnthropic()`, a team administrator can choose Anthropic, load the models for a key, test and save.
- Full suite green at the warning baseline (14).
- #381 is closed with what shipped.
