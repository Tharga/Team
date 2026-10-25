# Plan: Anthropic as a support-assistant provider (#381 step 4)

- [x] 1. Package updates: `dotnet outdated` (2026-10-25). Only AngleSharp 1.8.6, held for bUnit (#390). No deps commit.
- [ ] 2. Tests first: both providers offered, Anthropic's default address, the host can change it, models listed from an Anthropic-shaped catalogue with the key as a bearer token, choices include Anthropic.
- [ ] 3. `AnthropicSupportOptions.AnthropicBaseAddress` and the factory offering Anthropic.
- [ ] 4. Full build (baseline 14) and the full suite.
- [ ] 5. Docs: support-cases.md, Support.Anthropic README and package description, Support README, root README.
- [ ] 6. Try it in the sample: Anthropic offered on the AI assistant page.

## Last session

2026-10-25: branch created, plan written, waiting on the user's decision about offering Anthropic by default.
