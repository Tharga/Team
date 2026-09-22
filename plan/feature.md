# Feature: icon settings as a shipped component, with a Gravatar style preview

Branched from `origin/master` at `d9fda2f`. Asked for 2026-09-22 (user), starting from *"I want a preview when
I select the Gravatar style"* and the follow-up question of whether the page is reusable. It is not — today
`/icon-settings` is demo markup in `Tharga.Team.Sample`, with the style list hardcoded in its `@code` block.

## Goal

Ship `<IconSettingsView />` in `Tharga.Team.Blazor` so a client site gets the page with one tag, with a live
preview of what each Gravatar style produces, and with the chosen settings surviving a restart.

## Decisions (user, 2026-09-22)

| Question | Decision |
|---|---|
| Gating scope | **`users:manage`** (system) — the scope that already authorizes setting a user's icon on their behalf. No new scope |
| Persistence | **The toolkit's own store** — settings survive a restart without the host writing anything |
| Global or per-team | **Global.** `IconSettings` is a process singleton and `IconSubject` carries no team; per-team would mean making icon resolution team-aware throughout, which nobody has asked for |

## Design

### The valid styles stop being prose

`GravatarStyles.All` in `Tharga.Team` — the single source. The set is currently written out in three places
(`IconSettingsPage.razor`, the XML docs on `IconSettings.GravatarStyle`, `docs/articles/icons.md`) and exposed
as code in none, so a consumer building their own settings UI retypes it from documentation.

### Persistence mirrors the icon-store seam

`IIconSettingsStore` in `Tharga.Team`, `MongoIconSettingsStore` in `Tharga.Team.MongoDB`, registered as the
default and replaceable by the host — exactly the shape `IIconStore` / `MongoIconStore` already has, so this
is an established pattern here rather than a port invented for one implementation.

Stored settings are loaded into the `IconSettings` singleton at startup. **A host that never opens the view
is unaffected**: nothing is stored, so `o.IconSettings` configured at startup stays authoritative. Once saved,
the stored value wins on the next start — that is the point of persisting, and it is what the docs must say.

### The check is in a service, not in the component

`IIconSettingsService` carrying `[RequireScope(SystemUserScopes.Manage)]`, and `<IconSettingsView />` injects
*that*. A component that wrote to the store directly would be the only thing standing between any signed-in
user and app-wide icon behaviour — which is precisely how `team:read` came to be registered, documented,
granted and checked by nothing. The component hides what the caller cannot do; the service refuses it anyway.

Changing app-wide icon behaviour is an administrative act, so it is audited with the actor and the new values,
like the other administrative operations.

### The preview

Each style renders `https://www.gravatar.com/avatar/{md5}?d={style}&f=y` for the signed-in user's own email.

**`f=y` is load-bearing.** Without it, an email that has a real Gravatar returns the photo whatever `d` says,
so all seven tiles come back identical and the control reads as broken. `blank` renders nothing by design — an
empty tile is the correct preview, and it gets a label so it does not read as a failure to load.

### Text

A new `IconSettingsViewText` catalogue through `IThargaTextProvider`. Not optional: the component ships in
`Tharga.Team.Blazor`, and `TextCoverageTests` fails any component carrying literal display text that is in
neither the migrated nor the pending table.

## Multi-instance — settled 2026-09-22 (user)

**The cached value expires (~60s) so every instance converges on its own.** Consuming sites run more than one
instance.

**Why not cache indefinitely and clear it on save**, which is the obvious and otherwise better design: clearing
on change clears the cache *in the process that handled the save*. Nothing tells the others, so their cache
stays wrong until they restart — permanently stale rather than briefly stale, and it never self-corrects. An
infinite-until-cleared cache is correct only when the cache itself is shared, and `Tharga.Team` has no
distributed-cache dependency; it is deliberately plain .NET with none. `ITeamCache` was considered and
rejected: it is a host-implemented cache for the three claims-path lookups, and widening that published
contract to carry site settings would make it something it does not claim to be.

So the expiry is not an alternative to clearing on change — it is how clearing on change reaches another
process when there is nothing shared to clear. The saving instance still clears its own copy immediately, so
the person who made the change sees it at once.

Cost: about one store read per instance per interval, behind the cache rather than in the avatar path.

## Not in scope

- Per-team icon settings.
- Changing the icon *sources* or their order — this is the settings surface, not the pipeline.
- A preview for `DefaultUserIconUrl` (it is whatever URL the host typed; there is nothing to preview but the
  image itself, which the host can already see).

## Acceptance criteria

- [ ] `<IconSettingsView />` renders the current settings and saves changes, in a client site, with one tag.
- [ ] A caller without `users:manage` cannot change anything — refused at the service, not only hidden.
- [ ] Settings survive a restart; a host that never opens the view still gets its startup configuration.
- [ ] Selecting a style previews it, and the preview is the style rather than the caller's own Gravatar photo.
- [ ] `GravatarStyles.All` is the only place the set is written; the sample and docs refer to it.
- [ ] Changes are audited with the actor and what changed.
- [ ] Every string resolves through `IThargaTextProvider`; the component is in the migrated table.
- [ ] The sample page is `<IconSettingsView />` and nothing else.
- [ ] Full suite green.

## Done condition

Criteria met, docs on both surfaces (`README.md` icons section and `docs/articles/icons.md`), `plan/` removed
in the close-out commit, PR open.

## Version

Additive and opt-in — a new component, service, store and constant — so **no `MAJOR_MINOR` bump**; the patch
increments from git tags. The one behaviour change is that a stored setting outranks startup configuration,
which only affects a host that has used the view. Say so in the release notes.
