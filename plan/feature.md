# Feature: split access simulation into "run as" and "demo mode"

**Issue:** [Tharga/Team#223](https://github.com/Tharga/Team/issues/223) — *Cannot restrict who may simulate.*

**Branch:** `feature/simulation-split` (from `master`, at 3.13.1)

## Goal

Let a host grant the two halves of access simulation separately, and make each one visible where it belongs:

- **Run as** — view as another user, access level, role or scope set. Team owners and administrators may use
  it. Shown in the navigation bar while active, so nobody forgets the session is reduced.
- **Demo mode** — drop your own system scopes and application roles. Staff only. **Invisible everywhere
  except the profile page**, because a banner announcing "demo mode" during a customer demo defeats the
  point of it.

## Why one scope was wrong

`SimulationScopes.Simulate` (`simulation:use`) is registered at `AccessLevel.Administrator`, and
`ScopeRegistry.GetScopesForAccessLevel` hands Owner and Administrator every registered scope. So the grant
reaches **every team owner and administrator in every tenant**, including customers' own — and a host cannot
narrow it, because `ScopeRegistry.Register` throws on a duplicate name so the scope cannot be re-levelled.

`UserProfileView.ShowAccessCard` is presentation only, by its own remarks: setting it false hides the
control while the principal keeps the scope. Hiding a capability and withholding it are not the same
statement.

**The two halves were never the same capability.** `AccessSimulationCardText.DemoDescription` documents demo
mode as dropping *"your system scopes and application roles"* — a customer's team owner holds no system
scopes, so for them that half offers to drop nothing. It is inert for exactly the audience that currently
sees it. "View as another user" is different and does earn its place at team level: checking what a Viewer
sees before inviting one is an ordinary tenant-owner task.

This is **option 2 from the issue itself** — *"gate demo mode on the caller actually holding system
scopes… this fixes the half that is most clearly misplaced, without any new configuration surface."*

## Design

### Two grants, using the split the toolkit already has

| Capability | Scope | Kind | Who gets it |
|---|---|---|---|
| Run as | `simulation:use` (unchanged) | **team**, at `Administrator` | Team owners and administrators — deliberately |
| Demo mode | `simulation:demo` (new) | **system** | Only a principal holding a system grant — i.e. staff |

Demo mode becomes a system scope rather than a team one because that is what it *does*: it drops system
scopes and application roles. Resolved with `TeamScopeGate.HasSystemScope`, never a bare `HasClaim`, so an
in-team claim of the same name cannot satisfy it — the same rule `teams:delete` and `teams:set-owner` follow.

**This closes #223 without touching `ScopeRegistry`.** The general "let a scope be declared grant-only"
request stays filed in the backlog for #232's residual; it is not needed here.

### Demo becomes a real kind, not a magic label

Today `AccessSimulationTargets.FromDemo` returns `Kind = AccessSimulationKind.Scopes` with
`Label = "Demo mode"`. Nothing can distinguish demo from a scope-set simulation except that string, and the
visibility rules below have to. Add `AccessSimulationKind.Demo`.

### Visibility

| State | Navigation bar | Profile page |
|---|---|---|
| Nothing active | Entry point, if the host wired one and the option allows | Card |
| **Run as** active | **Banner** — the user must know their view is reduced | Card |
| **Demo mode** active | **Nothing** | Card |

A host-level option controls whether the navigation bar carries the actions at all:

```csharp
o.Simulation.ShowInNavigation = false;   // default true — today's behaviour
```

The existing `ShowEntryPoint` / `ShowBanner` component parameters stay and continue to win where set, so a
host placing the bar by hand keeps full control. The option sets the default for hosts that do not.

## The safety question this design creates

**With no banner, the profile page is the only way out of demo mode.** That hazard already exists and is
already documented — the sample runs `<AccessSimulationBar ShowEntryPoint="false" ShowBanner="false" />`
with a comment saying exactly this:

> *"nothing on screen now says the session is reduced, so the only way back is the profile page. That works
> here because `/account` carries `[Authorize]` and nothing more — a host gating it on a scope a simulation
> can remove would strand the caller."*

What changes is that it becomes the **default** for demo mode rather than something a host opts into. So:

- The profile card must be a working exit whenever a simulation is active — verified, not assumed.
- `ShowAccessCard = false` already means *"I am placing `<AccessSimulationCard />` elsewhere"* by its own
  documentation, so the exit still exists by contract. Not overridden.
- **The residual risk is the host's and must be documented**: a host whose profile route is gated on a scope
  demo mode removes will strand the caller. The toolkit cannot see that route, so it cannot check it.

## Acceptance criteria

- [ ] A team owner or administrator with `simulation:use` can start a run-as simulation.
- [ ] The same caller **cannot** start demo mode; the demo control is not offered and the call is refused.
- [ ] A caller holding the `simulation:demo` **system** grant can start demo mode.
- [ ] An **in-team** claim named `simulation:demo` does not satisfy it.
- [ ] While run-as is active the navigation bar shows the banner and its stop action.
- [ ] While demo mode is active the navigation bar shows **nothing**.
- [ ] The profile card is a working exit from both.
- [ ] `o.Simulation.ShowInNavigation = false` suppresses the bar entirely; the default preserves today's
      behaviour.
- [ ] `AccessSimulationKind.Demo` is what distinguishes demo — no code matches on the label.
- [ ] Existing hosts that granted only `simulation:use` keep run-as and silently lose nothing else, because
      demo mode was never usable for a caller without system scopes.
- [ ] Full suite green.

## Done condition

Criteria met, docs updated, `Requests.md` and backlog closed out with evidence, #223 answered and closed,
`plan/` removed in the close-out commit.
