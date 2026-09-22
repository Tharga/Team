# Plan: icon settings view (Gravatar style preview)

Spec: `plan/feature.md`. Branched from `origin/master` at `d9fda2f`; `dotnet outdated` clean at branch time,
so there is no leading `chore(deps)` commit.

- [x] 1. **Multi-instance settled 2026-09-22 (user): cached with a ~60s expiry**, because consuming sites run
      several instances and clearing on save reaches only the saving process. Reasoning and the rejected
      alternatives are in `feature.md`. The saving instance still clears its own copy at once.
- [ ] 2. `GravatarStyles.All` in `Tharga.Team`, with the styles and their display names. Point
      `IconSettings.GravatarStyle`'s XML docs at it instead of listing them, and add a test that the default
      (`identicon`) is a member — a default outside the set is the failure this constant exists to prevent.
- [ ] 3. `IIconSettingsStore` in `Tharga.Team` + `MongoIconSettingsStore` in `Tharga.Team.MongoDB`, mirroring
      `IIconStore` / `MongoIconStore` including the host-replacement path. One site-level document; enums and
      booleans round-trip. Tests on the store shape, and on the representation per the persistence rule.
- [ ] 3b. The ~60s expiry in front of the store, with the saving instance clearing its own copy immediately.
      Tests with a controlled clock: a value is served from cache inside the window, re-read after it, and a
      save is visible on the saving instance without waiting.
- [ ] 4. Load stored settings into the `IconSettings` singleton at startup, after the host's
      `o.IconSettings` configuration so a stored value wins. Test: nothing stored leaves startup
      configuration untouched; something stored overrides it.
- [ ] 5. `IIconSettingsService` with `[RequireScope(SystemUserScopes.Manage)]` — read and save — registered by
      the library with `TryAdd`. Tests: a caller without the scope is refused at the service.
- [ ] 6. Audit the save with the actor and what changed, through the existing decorator pattern. Tests.
- [ ] 7. `IconSettingsViewText` catalogue, then `<IconSettingsView />` in `Tharga.Team.Blazor`: the switches,
      the style dropdown bound to `GravatarStyles.All`, the default-URL box, and the preview strip
      (`?d={style}&f=y`, `blank` labelled). Hide the controls from a caller without the scope.
- [ ] 8. Add the component to the migrated table in `TextCoverageTests`; confirm it renders no literal text.
- [ ] 9. Replace the sample page body with `<IconSettingsView />`, and drop its hardcoded `_styles`.
- [ ] 10. Docs: `README.md` icons section and `docs/articles/icons.md` — the component, the gating scope, that
      a stored setting outranks startup configuration, and the `f=y` reason so the next person does not
      rediscover it. Own `docs:` commit.
- [ ] 11. Manual verification in the sample: preview changes with the selection, a non-`users:manage` caller
      is refused, and a restart keeps the setting.
- [ ] 12. Full suite, then push for user testing.

## Notes

- **The component must not write to the store directly.** The gate lives in `IIconSettingsService`; the
  component only decides what to offer. A first-level surface reaching past its gated service is how
  `team:read` ended up registered, documented, granted and enforced by nothing.
- `f=y` on the preview URL is not cosmetic — without it every tile renders the caller's own Gravatar photo and
  the control looks dead. Worth a comment at the call site.
- No `MAJOR_MINOR` bump: additive and opt-in.
