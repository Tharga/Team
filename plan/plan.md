# Plan: Anthropic as a support-assistant provider (#381 step 4)

- [x] 1. Package updates: `dotnet outdated` (2026-10-25). Only AngleSharp 1.8.6, held for bUnit (#390). No deps commit.
- [x] 2. Tests first. `AnthropicChatClientFactoryTests`: both providers offered at their addresses, the host can change both, `OfferAnthropic = false` leaves Anthropic out, Anthropic's catalogue (with `created_at`) listed from its address with the key as a bearer token, both registration names give the factory. `SupportAiConnectionServiceTests`: every offered provider is a choice; connecting Anthropic stores its address. `SupportAssistantConnectionViewTests`: choosing Anthropic starts from Anthropic's address.
  - On the old factory only the two "which providers are offered" tests failed: listing Anthropic's models already worked, since the client is the same.
- [x] 3. `AnthropicSupportOptions.OfferAnthropic` (default true) and `AnthropicBaseAddress` (default `https://api.anthropic.com`); the factory offers Anthropic beside Neurolito.
  - Decided with the user: the registration is renamed `AddThargaSupportAiProviders`, the partner of `AddThargaSupportAiConnections`; `AddThargaSupportAnthropic` stays as an `[Obsolete]` alias that forwards to it. `AddThargaSupport` was not available: it registers the Slack notifications.
  - Decided with the user: Anthropic is offered by default, with `OfferAnthropic = false` to keep teams on Neurolito.
  - Checked, not assumed: the Anthropic SDK 13.3.0 documents `ApiKey` as "sent as `Authorization: Bearer`", so the same client serves Anthropic's API and Neurolito.
- [x] 4. Full build at the warning baseline (14). Full suite green: Service 1532, Blazor 2338, Email 985, Support 816, MongoDB 468, Mcp 121, Entra 58, Support.Anthropic 15, Images 11.
- [x] 5. Docs: `support-cases.md` (the choice, the registration, the switch and the rename), the Support.Anthropic README and package description, the Support README (its "Planned" line was stale: usage per answer shipped in 4.4), the root README row, and the registration's XML docs. The sample uses the new name and its menu text names Anthropic.
  - Found on the way: the scripted edits of this session and #392 had given 17 files a byte-order mark they never had. Removed in its own `chore:` commit.
- [~] 6. Try it in the sample: Anthropic offered on the AI assistant page; with a real Anthropic key, load models, test, save.

## Last session

2026-10-25: steps 1-5 done and committed. Next: the user tries Anthropic in the sample, then close-out and PR.
