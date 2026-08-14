# Feature: the access card draws without a gap, and the bar can be translated

Fixes [Tharga/Team#219](https://github.com/Tharga/Team/issues/219) and
[Tharga/Team#221](https://github.com/Tharga/Team/issues/221), both filed by Eplicta FortDocs 2026-08-10
against Tharga.Team.Blazor 3.12.0. One feature because both are the same component pair, and FortDocs'
action for either is the same single upgrade.

## Goal

1. `AccessSimulationCard` appears on first paint for a caller who may simulate, instead of being absent
   and then appearing.
2. Every user-facing string in `AccessSimulationBar` resolves through `IThargaTextProvider`, as the card's
   ten `team.simulation.card.*` keys already do.

## #219 — what the report assumed, and what the code does

The report says the card "fills the member, role and scope pickers — several round trips before anything
can be drawn". **That is not what the shipped card does**: `GetMemberTargetsAsync`, `GetRoleTargetsAsync`,
`GetAccessLevelTargets` and `GetOwnScopesAsync` are all called by `AccessSimulationDialog`, not by the
card. Suggestion 1 in the issue — load the pickers lazily — is therefore already the case, and re-doing it
would change nothing.

What the card actually awaits before it can draw:

| Step | Cost |
|---|---|
| `TextProvider.ResolveAsync(AccessSimulationCardText.All)` | Host-dependent. For FortDocs this is a Quilt4Net content lookup, so possibly a network call |
| `SimulationState.GetActiveAsync()` | Reads a claim off the auth state. Cheap |
| `SimulationState.CanSimulateAsync()` | `ResolveRealGrantAsync` → auth state, `GetCurrentUserAsync`, `TeamGrantResolver`. **The database call the report is really describing** |

**The grant resolution is redundant in the only case it is used.** Both the card and the bar call
`CanSimulateAsync` *only when no simulation is active* — and with none active the principal carries the
caller's real, unfiltered claims. `TeamMembershipClaimsBuilder` builds those claims by calling the same
`TeamGrantResolver.ResolveAsync` with the same arguments and emitting every `grant.Scopes` entry as a
`TeamClaimTypes.Scope` claim, and `AccessSimulationFilter` only ever *removes* claims. So the answer the
card pays a round trip for is already on the principal, put there by the code that owns the rule.

This is why the fix removes the wait rather than covering it up, which is what the report asked for.

## #221 — what cannot be translated

The banner's wording ("Viewing as", "your own access is reduced", "Return to my access"), the entry-point
label ("View as…"), and the target descriptions `Describe` composes ("the X role", "access level Y").
`AccessSimulationBar.Text` overrides the button label only, and only as a literal rather than through the
text provider the rest of the toolkit uses.

FortDocs is Swedish-first, and "Return to my access" is the exit — the control someone needs to find under
pressure, which is the worst thing to leave in English.

## Scope

- A claims fast path inside `CanSimulateAsync` for the no-simulation-active case.
- The card decides whether it renders *before* awaiting text, then shows a loading frame while text
  resolves.
- A new `AccessSimulationBarText` alongside `AccessSimulationCardText`, resolved the same way.
- `AccessSimulationBar.Text` keeps winning when a host sets it.

## Out of scope

- **`AccessSimulationDialog` is also untranslated** — "See the application as someone with less access",
  "Simulate", "A member", "A role", the gap warning. #221 names the bar, so this feature delivers the bar.
  Raised with the user as the obvious next gap; a Swedish bar in front of an English dialog is half a
  feature for this consumer.
- Caching the grant. The report explicitly does not ask for it and is right not to — the lifetime is the
  host's decision, and FortDocs has already decided theirs is "none".
- #223 (who may simulate) and #225 (ownership transfer).

## Acceptance criteria

- [ ] With no simulation active, `CanSimulateAsync` answers from the principal's claims and performs no
      team lookup at all.
- [ ] With a simulation active, it still re-resolves the real grant — a caller who simulated the scope
      away must not be locked out of their own picker.
- [ ] Both paths agree: the same caller gets the same answer either way.
- [ ] The card renders nothing for a caller who cannot simulate — no placeholder flash where there was
      previously nothing.
- [ ] The card renders a loading frame, not an empty region, while its text resolves.
- [ ] Every string the bar renders resolves through `IThargaTextProvider`, including the target
      description, and the target stays visually emphasised inside the translated sentence.
- [ ] A host-supplied `AccessSimulationBar.Text` still wins over the resolved label.
- [ ] A translation that drops the `{0}` placeholder degrades to a sentence without the name rather than
      throwing.
- [ ] Full test suite green; `dotnet build -c Release` clean.

## Done condition

The user confirms, the docs surfaces are reviewed, and #219 and #221 are closed with the shipped evidence
alongside the corresponding entries in the central requests file and FortDocs' request file.
