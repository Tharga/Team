# Feature: A "Private" checkbox on the API key create card, shown by default

Source: [Tharga/Team#314](https://github.com/Tharga/Team/issues/314), re-scoped by the user on 2026-10-17.
The issue asked for an option that makes *every* new team key private. The user doesn't want that. They want
to be able to choose, per key, to make it private when it's created.

## Goal

Any page showing `ApiKeyView` lets the person creating a key tick **Private**, which makes the key owner-scoped
to them. No host configuration is needed.

## Today

- The create card (advanced mode only) has a "Private / only me" **switch**, but only when the page sets
  `ShowPrivateKeys` to `Mine` or `All`. The default is `None`, which shows no switch and lists no private keys.
- The sample's API keys page doesn't set it, so the option is invisible there.
- The server side is complete: `IApiKeyManagementService.CreateKeyAsync(..., ownerScoped: true)` makes a key
  owned by the caller, and only the owner (or `apikeys:oversee`) sees and manages it.

## Scope

- **`ApiKeyView.ShowPrivateKeys` defaults to `Mine`** (was `None`), as decided by the user. Every page shows
  the checkbox and lists the caller's own private keys, so a new private key doesn't vanish from the list. A
  page that sets `None` explicitly still hides both.
- **The switch becomes a checkbox**, with the same "Private" label and "only me" caption and tooltip.
- No service or option changes.
- Docs: the `ShowPrivateKeys` default in the implementation guide and the Blazor README.
- The earlier "require every key to be private" work is reverted (commit 481db00).

## Upgrade note (release notes)

On 4.1 a page that didn't set `ShowPrivateKeys` gains the Private checkbox and lists the caller's own private
keys. To keep the old behaviour, set `ShowPrivateKeys="PrivateKeyScope.None"`.

## Acceptance criteria

- A default `ApiKeyView` shows a Private checkbox on the create card. A key created with it ticked is private
  to the creator and appears in the list.
- `ShowPrivateKeys="PrivateKeyScope.None"` hides the checkbox and private keys, as before.
- Version 4.1.
