# Feature: route UsersView through the text catalogue (#204, increment 2)

## Goal

Migrate `UsersView` onto the text catalogue, and confirm the phase-1 mechanism works on a component it was
not designed against.

## Why this component first

Three literal strings — small enough that a failure would be the mechanism's fault rather than the sweep's.
It is also the first component migrated by someone applying the pattern rather than inventing it, which is
what makes it evidence.

**One of the three is the tenant noun in plural** — the `Teams` tab. That is Eplicta's actual case: a host
calling the tenant an Organisation sees this tab say "Teams" beside their own renamed vocabulary.

## Scope

- `UsersViewText` — `UsersTab`, `TeamsTab`, `DirectoryOnlyTab`, plus an `All` array for the resolve.
- `UsersView` injects `IThargaTextProvider`, resolves once in `OnInitializedAsync`, reads synchronously.
- Ratchet updated: `UsersView` moves from **pending (3)** to **migrated (enforced zero)**.

## What this confirmed about the mechanism

- **The pattern transferred without change.** Catalogue, `All` array, one `ResolveAsync`, indexer in markup.
  No new abstraction was needed for a component with a different shape.
- **Reflection picks up a catalogue added after `ThargaTextKeys` was written.** `UsersViewText` is the first
  such case and it appeared in `All` with nothing registering it — now asserted by a test, because that is
  the property a consumer depends on when they upgrade and expect new keys to surface.
- **A name collision is a non-issue.** `UsersView` already had `const int UsersTab/TeamsTab` for tab indices;
  keys are always referenced as `UsersViewText.UsersTab`, so the two coexist.

## Acceptance criteria

- [x] `UsersView` renders no literal attribute text; the ratchet enforces it as migrated.
- [x] Keys resolve through the provider, with English defaults when none is registered.
- [x] `UsersViewText.TeamsTab` is discoverable through `ThargaTextKeys.All`, asserted.
- [x] Guide's migration note corrected — it still listed `UsersView` as unmigrated.
- [x] Full suite green, no new warnings.

## Remaining for #204

`TeamComponent` (24 attribute strings) and `AuditLogView` (47), plus inline prose in both. The ratchet records
each count.
