# Fix: ScopeView reads the team-blind role registry

[Tharga/Team#292](https://github.com/Tharga/Team/issues/292), reported from PlutusWave on
`Tharga.Team.Blazor` 3.21.2. Branched from `origin/master` at `2568af0`; `dotnet outdated` clean, so no
leading `chore(deps)` commit.

## The defect

`<ScopeView />` builds its rows and its role list from `ITenantRoleRegistry`, the **code-registered** roles
only, and never consults the team-aware `ITenantRoleService`. Three symptoms, in increasing order of harm:

1. A team's custom role is missing from the **Roles** select bar, so *"what would this role grant?"* cannot
   be asked about it.
2. It is missing from the **Roles** column, so no scope is credited to it.
3. **A member who holds that role is told they do not hold its scopes.** `ApplyCurrentUserDefaultsAsync`
   filters the member's own `TenantRoles` through the code-only list, drops the custom one, and
   `RecomputeGrants` then greys out everything it granted.

**Verified against master 2026-09-23**, every claim in the issue: `ScopeView.razor:189-193` builds from the
registry, line 249-250 filters against `_allRoleNames`, `ScopeReference.Build` takes the registry rather
than a role list, and `ScopeView` has six parameters, none of them roles.

## Why it matters more than a display bug

The claims pipeline resolves the same member **correctly** — `TeamGrantResolver.ResolveFromMemberAsync` goes
through `ITenantRoleService.GetEffectiveScopesAsync`, so those scopes really are in the principal. The page
and the principal disagree and the page is the wrong one.

This is the page somebody opens to answer *"why can I not see that?"*, commonly reachable by the people
holding least. Telling them they lack a permission they hold sends them to ask for a grant they already
have. Nothing is granted that should not be — the drift is in the **explanation**, not in enforcement.

It is also the exact failure `ResolveFromMemberAsync`'s own remarks warn about: *"Exists so the scope
computation has one copy, not two."* `ScopeView` is a second copy, and it has drifted the same way the gate
on `ITeamManagementService`'s reads did in #248.

## The fix

`ApiKeyRolePicker.ResolveAsync(tenantRoleService, registry, teamKey)` already does exactly the right
resolution, and `TeamComponent` and `ApiKeyView` both use it. `ScopeView` is the last role surface still on
the team-blind registry; the omission reads as unintended rather than decided.

1. **`ScopeReference.Build(IScopeRegistry, IReadOnlyList<TenantRoleDefinition>)`** — the existing
   registry-taking signature delegates to it. `ScopeReference` is pure, so the merged case is unit-testable
   with no team and no store.
2. **Resolve the team, then rebuild.** Keep the synchronous code-roles build for the first render so the grid
   is not empty pre-await, then rebuild once `GetSelectedTeamAsync` has answered. The team is *already*
   resolved inside `ApplyCurrentUserDefaultsAsync`, a few lines above the faulty filter, so this needs no new
   lookup — only ordering. **Line 249 must filter against the merged list**, which is symptom 3.
3. **`[Parameter] public IReadOnlyList<TenantRoleDefinition> Roles`**, mirroring `RoleEditor.Roles`: supplied
   wins, otherwise resolve as above. Lets a host compose the list, and makes the component testable without
   DI.
4. **Correct two stale prose lines** — the component comment at `ScopeView.razor:15` and the README's
   `ScopeView` bullet, both of which still say `ITenantRoleRegistry`.

## Decided: the visibility provider is **not** applied here

`ITenantRoleVisibilityProvider` is consulted by `TeamComponent` before building its picker. `ScopeView` will
continue not to consult it, and the reason goes in the code so it is not re-litigated silently.

The provider's own remarks are the argument: *"Hiding a role does not prune existing assignments and does not
affect scope resolution — a member already assigned a hidden role keeps it and still receives its scopes at
runtime."* Filtering by it here would therefore **reproduce symptom 3 for a different reason** — a member
holding a hidden role would again be told they lack what they hold. On a page whose whole purpose is
explaining access, hiding information is the one thing that cannot be right.

**Considered and rejected:** applying it to the hypothetical select bar (*"what would this role grant?"*)
while never applying it to what a member is credited with. Defensible, and it keeps the two lists consistent
with `TeamComponent`'s picker — but it means two role lists in one component for a distinction no user is
aware of, to hide a role the same page still has to credit when they hold it.

## Not in scope

- Migrating `ScopeView` off literal text. It sits in the `TextCoverageTests` **Pending** table at **14**
  strings, and that test fails if the count moves in *either* direction without the record being updated. The
  work here must leave it at 14, or update the number deliberately — it is not a licence to migrate the
  component, which is #204's job.
- Any change to how scopes are actually resolved. Enforcement is correct; only the page is wrong.

## Acceptance criteria

- [ ] A team's custom role appears in the Roles select bar and the Roles column on `<ScopeView />`.
- [ ] A member assigned a custom role has it preselected, and the scopes it grants are **not** greyed out.
- [ ] A host can pass `Roles` and have it win over resolution.
- [ ] With `ITenantRoleService` unregistered (`EnableDynamicRoles = false`), behaviour is unchanged.
- [ ] The first synchronous render is not empty — code roles show before the team resolves.
- [ ] `ScopeView.razor` still reports 14 literal strings to the text ratchet.
- [ ] Full suite green.

## Done condition

Criteria met, the issue commented and closed with what shipped, docs corrected, `plan/` removed in the
close-out commit, PR open.

## Version

A defect fix plus one additive opt-in parameter. **No `MAJOR_MINOR` bump** — no consumer has to do anything
to keep working; the page simply stops lying. CI takes the patch from git tags.
