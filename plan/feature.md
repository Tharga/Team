# Feature: text catalogue foundation (#204, phase 1)

## Goal

Give a consumer a way to override **and discover** every string the toolkit renders, and the mechanism to
migrate the remaining components onto it.

## What #204 turned out to be

The resolver Eplicta asked for as their preferred option already existed: `IThargaTextProvider.GetAsync(TextKey)`,
`TextKey(Key, Default)`, `o.Blazor.AddTextProvider<T>()`. "Team" is already the default and a consumer already
overrides by registering a provider.

**Two things were missing**, and the second was not in the issue — it came from the user:

1. **Coverage.** Only `LoginDisplay` and `TeamSelector` routed through it.
2. **Discoverability.** No way to enumerate the full key set, so a host could not know what to translate.
   They would find the gaps by seeing English in production.

## Scope of this phase

- **`TextSet` + `ResolveAsync(params TextKey[])`** — a component declares its keys, resolves them in one pass
  in `OnInitializedAsync`, reads them synchronously in markup. The provider is async and per-key; without
  this, a component with forty labels needs forty awaits and forty fields.
- **`ThargaTextKeys.All`** — every key, discovered by reflection over the catalogues so one added later is
  included without anyone remembering. This is the list a consumer translates from.
- **`TeamSelectorText` and `AccessLevelText`** — the first catalogues. Access levels are shared rather than
  per-component, because a level named differently in the selector than in the member grid reads as two
  different things.
- **`TextCoverageTests`** — the ratchet that drives the rest.
- **`TeamSelector` fully migrated** as the proof.

## Design rules written into the code

**Keys are whole strings, never a substitutable noun.** `Text["Team"] = "Organisation"` is the tempting
shortcut and produces broken Swedish: the definite article is a suffix, so *"medlem i ett team"* → *"teamet"*
is not reachable by substitution, and word order moves besides. Stated in `ThargaTextKeys` and the guide so it
is not "simplified" later.

**One failing key never fails the set.** A provider reaching an external source can throw on any lookup; that
key falls back to English and the rest still resolve. One English label among translated ones beats a
component that does not render.

## Why a ratchet rather than a gate

Migrating everything is a large sweep; blocking the build until it finished would mean landing it as one
unreviewable change. Each component is either **migrated** (zero literals, enforced) or **pending** with a
recorded count that may only go down. A new literal in a migrated component fails immediately.

**The guard earned itself during this phase** — it caught a literal tooltip in `TeamSelector` that the manual
pass had missed.

## Acceptance criteria

- [x] A component resolves many keys in one pass and reads them synchronously.
- [x] A throwing provider degrades that key to English rather than failing the set.
- [x] `ThargaTextKeys.All` is non-empty, deduplicated, ordered, and every entry has a default.
- [x] `TeamSelector` renders no literal attribute text.
- [x] The ratchet fails on a new literal in a migrated component, and on growth in a pending one.
- [x] The scan proves it still matches real markup, so it cannot pass while checking nothing.
- [x] Full suite green, no new warnings.

## Honest limits, stated in the test class

The scan covers **attribute** strings only. Inline prose between tags is user-facing too, but separating it
from markup, bound expressions and scope names like `team:manage` needs judgement a regex does not have. So a
zero means "no literal attribute text", not "fully translated".

## Remaining for #204

`TeamComponent` (24), `UsersView` (3), `AuditLogView` (47), plus inline prose across all of them. Each is its
own increment against a ratchet that is already recording the number.
