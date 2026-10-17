# Feature: Personal API keys, for team and system keys

Source: [Tharga/Team#314](https://github.com/Tharga/Team/issues/314), re-scoped by the user on 2026-10-17.
The issue asked for an option that makes *every* new team key private. The user doesn't want that as the
behaviour. They want each person creating a key to choose, with a host option deciding which kinds exist.

## Goal

- A host decides, separately for team keys and system keys, whether keys can be **shared**, **personal**,
  or **both**. The default is both.
- With both allowed, the create card has a **Personal** checkbox. A personal key belongs to the person who
  created it: only they see and manage it.
- Everything the two API key views show is translatable through text catalogues, like the rest of the library.

## Today

- Team keys can be personal ("private", owner-scoped) through `IApiKeyManagementService.CreateKeyAsync(...,
  ownerScoped: true)`. The view's checkbox appears only when the page sets `ShowPrivateKeys` (default `None`
  before this branch). No host option exists.
- System keys can't be personal at all. Every system key is shared by everyone who can manage system keys.
- `ApiKeyView` (44 literal strings), `SystemApiKeyView` (35) and `ApiKeyRevealDialog` (2) are still on the
  #204 Pending list: English only.

## Scope

### Options (`ApiKeyOptions`)

- New enum `ApiKeyOwnership`: `Both` (default), `Shared`, `Personal`.
- `ApiKeyOptions.TeamKeys` and `ApiKeyOptions.SystemKeys`, both default `Both`.
- Enforced in the services, not only the UI:
  - `Shared`: a request for a personal key is refused (`InvalidOperationException`). Failing closed: silently
    making it shared would expose a key the caller meant to keep to themselves.
  - `Personal`: every key created through the management service is personal, whatever the caller passed.
  - Keys that already exist are untouched by a change of option, and stay listed and manageable.
  - Simple mode's auto-created team keys have no creator, so they stay shared.

### Personal system keys

- Stored like personal team keys: `IApiKey.OwnerMemberKey` holds the owner's user key (a member key is the
  user key).
- `IApiKeyAdministrationService.CreatePersonalSystemKeyAsync(name, scopes, ownerUserKey, expiryDate, createdBy)`,
  a new member with a default implementation that throws `NotSupportedException`, so a host's own key store
  keeps compiling. The built-in store and both decorators (lifecycle, audit) implement it.
- Refreshing a personal system key keeps its owner.
- `ISystemApiKeyManagementService`:
  - `GetSystemKeysAsync(PrivateKeyScope personalKeys = None)`: shared keys always; personal keys as for team
    keys (`Mine` = own, `All` = own plus everything for an `apikeys:oversee` holder).
  - `CreateSystemKeyAsync(..., bool personal = false)`: the owner is always the caller (anti-spoof).
  - Refresh, lock, disable and delete of a personal system key: only its owner or an `apikeys:oversee` holder.
- Authentication refuses a personal system key whose owner no longer exists or is disabled, and fails closed
  on a store fault, like personal team keys (#323). Losing the Developer role can't be checked there, because
  roles come from the sign-in token.
- Deleting a user deletes (or disables, per `ApiKeyOptions.OwnerRemovedAction`) their personal system keys.

### UI

- Create card: a checkbox labelled **Personal**, with no "Private" heading. Shown when the option is `Both`
  and the view lists the caller's personal keys (`ShowPrivateKeys` isn't `None`).
- `Personal` option: no checkbox, every new key is personal, and the view lists at least the caller's own.
- The list badge reads **Personal** instead of "Private".
- `SystemApiKeyView` gains `ShowPrivateKeys` (default `Mine`), the same as `ApiKeyView`.

### Translations

- New catalogues `ApiKeyViewText`, `SystemApiKeyViewText` and `ApiKeyRevealDialogText`.
- All three components move from Pending to Migrated in `TextCoverageTests`.

## Upgrade notes (release notes)

- A page that didn't set `ShowPrivateKeys` on `ApiKeyView` now shows the Personal checkbox and lists the
  caller's own personal keys. Set `ShowPrivateKeys="PrivateKeyScope.None"` to keep the old behaviour, or set
  `ApiKeyOptions.TeamKeys = ApiKeyOwnership.Shared` to turn personal team keys off for the whole site.
- `SystemApiKeyView` shows the Personal checkbox too. Set `ApiKeyOptions.SystemKeys = ApiKeyOwnership.Shared`
  to keep every system key shared.
- A class implementing `ISystemApiKeyManagementService` must add the new parameters. A class implementing
  `IApiKeyAdministrationService` keeps compiling, and only needs `CreatePersonalSystemKeyAsync` to support
  personal system keys.
- The badge and checkbox say "Personal". The code keeps the existing "private" names (`PrivateKeyScope`,
  `ownerScoped`, `OwnerMemberKey`).

## Acceptance criteria

- With default options, both views show a Personal checkbox. A key created with it ticked is personal to the
  creator, listed with a Personal badge, and hidden from others.
- `Shared` hides the checkbox and refuses personal keys in the service. `Personal` hides the checkbox and
  makes every new key personal.
- A personal system key stops authenticating when its owner is deleted or disabled.
- The three API key components render no literal text.
- Version 4.1.
