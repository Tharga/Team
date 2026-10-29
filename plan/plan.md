# Plan: Support AI providers (#421, #422)

## Steps

- [x] 1. NuGet update: `dotnet outdated` reports nothing outdated (checked 2026-10-29, right after #424 merged).
- [x] 2. Confirmed 2026-10-29: ships as a **patch** (4.6.3), so `MAJOR_MINOR` stays `4.6`. MCP tools become their
      own issue (draft the text, show it before filing). The address rule stays: https, or http on this machine.
- [~] 3. Several factories: tests first (two factories offering different kinds are both offered and each reaches its
      own kind; the last registered wins a kind both offer; registering a package twice adds one factory), then the
      service, the resolver and the Anthropic registration.
- [ ] 4. Keyless kinds: tests first (a kind with `RequiresApiKey = false` connects, tests and lists without a key and
      stores none; it refuses an empty address; a stored key is not sent after the kind or address changes), then
      `RequiresApiKey` and the service.
- [ ] 5. The three kinds and `Tharga.Team.Support.OpenAI`: tests first against a fake server (models listed, each
      problem in words without the key, chat answered), then the enum members, the package and its registration.
- [ ] 6. `SupportAssistantConnectionView`: tests first (labels for the new kinds, the optional key, providers beyond
      the fixed pair), then the view and the text keys.
- [ ] 7. #422 endpoints: tests first (each route calls its method with the credential's team; 403 without the scope;
      400 without a team; 422 on a failed connect; no key in any response), then `MapThargaSupportAiConnection`.
- [ ] 8. Sample: register the new package and map the endpoints; check in a browser and over HTTP.
- [ ] 9. Docs: `docs/articles/support-cases.md`, the new package's README, the root README, `Tharga.Team.Support`'s
      README if it lists the registrations.
- [ ] 10. Full test suite, push the branch for the user to test.

## Notes

- Versioning: `MAJOR_MINOR` in `.github/workflows/build.yml` stays `4.6`; the user chose a patch.
- `Tharga.Team.Support.OpenAI` is a new package: it needs adding to the solution and the build's pack list.

## Last session

2026-10-29: #424 (#418–#420) merged as 4.6.2. Branch created from `master`, nothing outdated, plan written. Next: the
user confirms the plan.
