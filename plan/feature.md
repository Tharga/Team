# Feature: an expandable profile-page card for impersonation and demo mode

## Goal

One expandable card, mountable on a host's profile page, carrying two things:

- **Impersonation** — view the app as another member of the selected team.
- **Demo mode** — a system user (e.g. a Developer) with a team selected toggles it on and keeps only the
  claims they would hold on that team as an ordinary team user, consent included. Their system scopes and
  application roles go away, so a demonstration shows the product rather than the administrative surface.

**Returning to the profile page and toggling demo off must restore the original system scopes.** That is a
requirement, not a nicety — it is what makes the mode safe to try.

## Build on `AccessSimulation`, never beside it

Most of this already ships, and the parts that do are the parts that are hard to get right:

- `AccessSimulationFilter` already removes `TeamClaimTypes.SystemScope` claims and non-team role claims,
  driven by `DropSystemScopes` / `DropAppRoles`. **All four existing targets already set both flags true**
  (`AccessSimulationTargets.cs` 38-39, 56-57, 70-71, 83-84), so every simulation kind already performs the
  system-claim removal demo mode wants.
- `AccessSimulationKind.User` is impersonation in the only sense worth having — claims-only, still audited
  as the real person, de-escalation-only because the filter can only ever remove.
- `AccessSimulationState.StopAsync()` clears the cookie and re-issues claims through the ordinary path.
  The "toggle off and get them back" requirement is already satisfied by the mechanism.

**A second claims filter would be a second place that decides access**, which the target architecture's
rule 2 forbids outright. This feature is a new entry point plus one new preset over existing machinery.

## Scope

1. **`ProfilePath` and `TeamPath` options** on `ThargaBlazorOptions`, read by `LoginDisplay`.
2. **A "drop only my system access" simulation target** — the one genuinely missing behaviour.
3. **The card component**, expandable, mountable on a profile page.
4. Mount it on the sample's profile page so the feature is demonstrable.

### Why the path options are part of this feature and not a separate one

`LoginDisplay` renders its two built-in menu items with an icon and no href, then switches on that icon to
navigate to the literals `"profile"` and `"team"` (`LoginDisplay.razor:109`, `:112`). Host-supplied items
are unaffected — they carry their own `Href` and are matched first (`:100`).

Today that is a latent trap: a host mounting `<UserProfileView />` anywhere but `/profile` gets a menu item
that 404s, uncorrectably. **Once the demo toggle lives on the profile page, that page becomes the road back
from a reduced session** — so the trap stops being latent and starts being the difference between "toggle
demo off" and "sign out and back in". Fixing it is a prerequisite, which is why this is one feature.

Precedent: `ThargaBlazorOptions.CreateTeamPath` (3.2.0) and `InvitePath` solve exactly this shape already.
Follow them — optional, null means today's behaviour.

## The exit path is the whole design risk

Three guards, all required:

- **Never gate the way out.** `AccessSimulationBar` already gets this right: it asks `CanSimulateAsync()`
  *only* when nothing is active (`AccessSimulationBar.razor:44`), because a simulation can remove the very
  scope that authorizes starting one. The card copies that exactly.
- **`AccessSimulationBar` stays in the layout.** The card is an additional way *in*; the always-visible
  banner remains the way *out*. A card reachable only by navigating to a page is not a safe sole exit.
- **The profile route must be reachable**, which is scope item 1.

## Out of scope

- Changing what `AccessSimulationFilter` removes, or how. The filter is correct and is not touched.
- Escalation of any kind. Everything here remains a subset of what the caller genuinely holds.
- Moving the sample's profile page. Where it lives is a consumer decision and stays one; the toolkit-side
  concern is only the hard-coded navigation.

## Acceptance criteria

- [ ] `ProfilePath` / `TeamPath` default to null and reproduce today's navigation exactly when unset.
- [ ] Setting either sends the corresponding built-in menu item there instead; host-supplied items keep
      working unchanged.
- [ ] A caller in demo mode holds no system scopes and no application roles, and their team access —
      including access that arrives via consent — is unchanged.
- [ ] Toggling demo off restores the original system scopes.
- [ ] The card's exit control renders regardless of whether the active simulation removed `simulation:use`.
- [ ] Consent-derived team access survives `DropAppRoles`, asserted by a test rather than by reading the
      issuance order.
- [ ] Build clean, full test suite green.

## Done condition

Demo mode and impersonation both exercised from the sample app's profile page, including toggling demo off
and confirming system access returns, and the user confirms.
