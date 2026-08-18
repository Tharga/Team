# Feature: access-model configurability

**Issue:** [Tharga/Team#232](https://github.com/Tharga/Team/issues/232) — *Configurable access model*, filed by
FortDocs. One issue, two independent requests; this branch takes **part 1** and documents the answer to
**part 2**.

**Branch:** `feature/access-model-configurability` (from `master`)

## Part 2 — documentation only, because the capability already exists

The request was for a way to keep a scope (`case:read`, governing OSL/TF secrecy-classified records) out of
the automatic Owner/Administrator grant. **That works today with no toolkit change**: register the scope on a
code-defined tenant role and never in `ScopeRegistry`.

Verified against the code:

| Step | Behaviour |
|---|---|
| `TenantRoleRegistry.Register` | Stores scope **strings**; no validation against `ScopeRegistry` |
| `GetScopesForRoles` | Returns them as-is |
| `GetEffectiveScopes` | Unions role scopes into the effective set |
| `ScopeProxy.CheckScope` → `TeamScopePolicy.HasTeamScope` | Reads the **claim**, never the registry |
| `GetScopesForAccessLevel` | Cannot return it — it is not in `_scopes` |

`ValidateCustomRoles` additionally refuses any scope that is not app-registered, so a *team admin* cannot put
an unregistered scope into a tenant-defined custom role. For a scope of this kind that guard is exactly right
and comes free.

**So this branch documents it rather than building it.** What the toolkit could still add later is smaller
than the issue proposes — registration for *documentation and typo-safety* while excluding the level grant —
and that is filed rather than built here, because nothing is blocked without it.

**A correction the issue needs, and the reason to reply promptly:** #232 proposes
`scopes.Register("case:read", AccessLevel.Custom, …)`. That does the opposite of what it intends. The enum is
`Owner=0, Administrator=1, User=2, Viewer=3, Custom=4`; `GetScopesForAccessLevel` returns *all* scopes for
Owner/Administrator ignoring `DefaultMinimumLevel`, and the fall-through filter is
`s.DefaultMinimumLevel >= accessLevel` — so a Custom-registered scope reaches **Viewer and User too**. It
would leak `case:read` to every level.

## Part 1 — hiding an access level

**Goal:** let a host remove an access level from every selector, without making it invalid.

FortDocs' case: they are moving their last two level-registered scopes up to `Administrator`, after which
`Viewer` and `User` hold exactly the same thing (`team:read`). Offering both is then a choice with no meaning
that every team administrator has to reason about. They want `Viewer` gone from the pickers.

### Shape

```csharp
o.Blazor.HiddenAccessLevels = [AccessLevel.Viewer];
```

**A collection, not a bitflag.** `AccessLevel` is not `[Flags]` and `Owner = 0`, which in flag arithmetic
means "no bits" — `HasFlag(Owner)` is always true and `Owner` can never be OR'd in, so the filter cannot be
expressed at all. Building a parallel `[Flags]` enum would mean a second list of levels to keep in sync,
silently wrong when they drift, and would compound the `default(AccessLevel) == Owner` zero-value hazard the
backlog already carries as Critical.

**Subtractive, not an allow-list.** The four selectors deliberately differ — `ApiKeyView` keeps `Custom`
because least-privilege machine keys are its purpose, while the member dialogs exclude it. A single
allow-list applied everywhere would flatten that. Hiding is layered over each surface's own rule.

**Selectors only, never display.** A hidden level stays valid: FortDocs sync members from a system that can
still produce it, and those members must keep working and keep rendering their badge. `AccessLevelBadge` and
every read-only path are untouched.

### Surfaces

| Where | Offers today |
|---|---|
| `InviteUserDialog.razor:85` | Administrator, User, Viewer |
| `TeamComponent.razor:733` (member edit) | Administrator, User, Viewer |
| `ApiKeyView.razor:292` | Administrator, User, Viewer, **Custom** |
| `TeamComponent.razor:1000` (`_consentLevels`) | Viewer, User, Administrator |

The issue found three; the consent picker is the fourth and they asked for it too.

## Hiding Owner or Administrator — the question that shaped the guards

- **Owner is refused.** No selector offers it, so hiding it does nothing. Accepting a setting that silently
  does nothing teaches a host it worked — and someone setting this to mean *"nobody may become Owner"* has
  a security misunderstanding worth correcting at startup. Ownership is governed by
  `TransferOwnershipAsync` and `SetOwnerAsync`, and the message says so.
- **Administrator is allowed, with a consequence stated in the docs.** It is coherent — the Owner still
  manages the team — but it removes the only way to delegate management without handing over ownership.
  Note the domain still *produces* Administrators regardless: both `TransferOwnershipAsync` and
  `SetOwnerAsync` demote a displaced owner to Administrator. Those members keep working, which is the
  hidden-but-valid rule doing its job.
- **Emptying any selector is refused.** An invite dialog with no level to pick is broken, not configured.

## Acceptance criteria

- [ ] `o.Blazor.HiddenAccessLevels = [AccessLevel.Viewer]` removes Viewer from all four selectors.
- [ ] An existing Viewer member keeps working and still renders their badge.
- [ ] `ApiKeyView` still offers `Custom` when it is not hidden.
- [ ] Hiding `Owner` throws at registration, naming what actually governs ownership.
- [ ] A configuration that empties any selector throws at registration, naming that selector.
- [ ] Default (unset) behaviour is byte-for-byte what it is today.
- [ ] Docs explain grant-only scopes **and** the new option.
- [ ] Full suite green.

## Done condition

Criteria met, docs updated, `Requests.md` and backlog closed out with evidence, #232 answered with both the
`AccessLevel.Custom` correction and the shipped option, `plan/` removed in the close-out commit.
