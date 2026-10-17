# Feature: Require every new team API key to be private

Source: [Tharga/Team#314](https://github.com/Tharga/Team/issues/314).

## Goal

A host can require that every **new** team API key is owner-scoped ("private"), so each key has exactly one
responsible person. Use case: a bookkeeping app records the person behind a key as the actor on what it writes;
a team-wide key has no single owner.

## Today

- `ApiKeyView` shows a "Private (only me)" switch only when `ShowPrivateKeys` is not `None`, and it defaults off.
- `ApiKeyOptions` has nothing that requires private keys.
- `IApiKeyManagementService.CreateKeyAsync(..., ownerScoped)` can mint a private key, and the owner is always
  the caller (anti-spoof). `IApiKeyAdministrationService.CreateKeyAsync(..., ownerMemberKey)` is the single
  path every team key goes through. There's no REST create endpoint, MCP doesn't create keys, and system keys
  use a separate service.
- In simple mode (`AdvancedMode = false`), `GetKeysAsync` auto-creates `AutoKeyCount` team-wide keys with no
  owner.

## Scope

- **`ApiKeyOptions.RequirePrivateKeys`** (default `false`). This one switch covers both suggestions in the issue.
- **Enforced at the single creation path.** `ApiKeyAdministrationService.CreateKeyAsync` refuses a team key
  without an owner, throwing `InvalidOperationException` that names the option. The management service
  inherits it, so a host calling either service can't get around the UI.
- **`ApiKeyView` follows the option**, with no new parameter:
  - The create card always creates a private key owned by the caller. The switch shows on and disabled, with
    a caption explaining why.
  - Private keys are always listed. When `ShowPrivateKeys` is `None`, the view behaves as `Mine`; otherwise
    the newly created key would disappear from the list.
- **Existing team-wide keys keep working and stay listed.** Recycle, lock, disable, delete and edit still work
  on them. The option governs new keys only.
- Docs: the private-keys section of the implementation guide, and the `ApiKeyOptions` row in the Service
  README.

## Out of scope

- System keys.
- Converting existing team-wide keys into private ones.

## Decisions (to confirm)

- **D1. Refuse, don't coerce.** If `ownerScoped: false` were silently coerced to `true`, a caller asking for a
  team-wide key would get a key bound to whoever happened to call. Refusing names the problem. The UI never
  hits the refusal because it always asks for a private key. *Recommended: refuse.*
- **D2. Enforce at the administration layer, not only the management layer.** Host code calling
  `IApiKeyAdministrationService` directly (seeding, integrations) is refused too, so the option means "every
  new team key has an owner" with no exceptions. The cost: a host that wants one deliberate exception can't
  have it while the option is on. *Recommended: administration layer.*
- **D3. Simple mode conflicts with the option.** Auto-created keys have no owner and no caller to own them.
  Combining `RequirePrivateKeys = true` with `AdvancedMode = false` fails at startup with an error naming
  both options. Simple mode isn't silently left with no keys. *Recommended: fail at startup.*

## Acceptance criteria

- With the option on, `CreateKeyAsync` without an owner throws on both services; with an owner it succeeds.
- With the option off, behaviour is unchanged.
- The view's create card always produces a private key, and the new key is listed even with
  `ShowPrivateKeys = None`.
- Existing team-wide keys are still listed and manageable.
- Option on with simple mode fails at startup.
- Version 4.1 (a feature, not breaking).
