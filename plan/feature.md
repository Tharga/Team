# Feature: Personal API keys bound to their owner

Source: [Tharga/Team#323](https://github.com/Tharga/Team/issues/323). Priority tier 1 (security): a credential
can currently outlive, or outrank, the person it belongs to.

## Goal

A private (owner-scoped) team API key behaves as the owner's personal credential:
1. It stops working as soon as the owner can no longer act in the team.
2. It never grants more than the owner currently holds there.
3. Only the owner sees and manages it, plus whoever the host grants oversight to.

## Today

- `IApiKey.OwnerMemberKey` marks a key private (PR #96). The member key is the user key.
- `ApiKeyAuthenticationHandler` never reads the owner: the key keeps working after the owner is removed,
  suspended, disabled or deleted, and its scopes come only from its own level, roles and overrides.
- `ApiKeyManagementService` hard-codes the `"Developer"` role as the oversight exception.

## Scope

- **Owner check at authentication.** For a key with an owner, resolve the owner's membership in the key's team
  through `TeamGrantResolver.ResolveFromMemberAsync`, the single copy of the grant rule. The key is refused,
  with an auth-failure audit entry naming the reason, when:
  - the member is gone or not in state `Member`;
  - the member is suspended;
  - the user is missing or disabled (`IUser.DisabledAt`).
- **Never more than the owner.** Effective scopes are the key's scopes ∩ the owner's current scopes. The
  `AccessLevel` claim is the less privileged of the key's level and the owner's.
- **Owner on the principal.** A new `TeamClaimTypes.ApiKeyOwner` claim carries the owner's user key, so writes
  made with the key can be attributed to a person (what #314's host needs).
- **Oversight as a system scope.** A new `apikeys:oversee` system scope (`SystemApiKeyScopes.Oversee`) lets a
  holder see and manage other members' private keys. Hosts map it to their Developer or Administrator roles.
- **Clean up with the owner.** When the owner is removed from the team (removal, leaving, or user deletion),
  their private keys in that team are deleted, or disabled when `ApiKeyOptions.OwnerRemovedAction = Disable`.
  Refusal at authentication is the security guarantee; this step is hygiene, and it stops a re-invited user
  inheriting an old key.

## Decisions to confirm

- **D1: fail closed.** If the owner cannot be looked up (store fault), a private key is refused. Unlike the
  team-exists check, which fails open: a private key is the security case.
- **D2: keep the `Developer` role working in 3.x.** It still grants oversight alongside the new scope, marked
  for removal in 4.0 (added to plan 05). Dropping it now would silently take oversight away from hosts that
  rely on it.
- **D3: delete by default** on owner removal; `Disable` is the opt-in alternative.

## Out of scope

- #314 (require every new key to be private): related, separate issue.
- System keys: they have no owner.

## Acceptance criteria

- A private key is refused once its owner is removed, has left, is suspended, disabled or deleted, and works
  again when a suspension or disable is lifted.
- A private key's scopes never exceed its owner's current scopes; demoting the owner narrows the key on the
  next request.
- Team-wide keys behave exactly as before.
- Oversight works through `apikeys:oversee`, and through the `Developer` role in 3.x.
- Removing the owner deletes (or disables) their private keys in that team.
- The owner's user key is on the authenticated principal.
- Tests cover each refusal reason, the intersection, the level claim, fail-closed, oversight both ways, and
  the cleanup on every removal path.

## Done condition

All acceptance criteria are met, the full suite passes, the docs are updated (the implementation guide's
*Private (owner-scoped) keys*, the README API-key section), the version line is bumped, #323 is closed by the PR,
and a v4 removal note for the `Developer` fallback is added to plan 05.
