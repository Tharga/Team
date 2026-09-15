# Access simulation — see the app as a less privileged user

Let a team administrator temporarily drop some of their own access, look at the application as a member
with less would see it, and click once to come back.

It exists to make **setting a user's access correct** easy — to answer *"if I give this person this role,
what will they actually get?"* without keeping a throwaway account per role combination, or editing your
own roles and remembering to put them back.

**De-escalation only.** The effective set is always a subset of what the caller genuinely holds.

## Setup

```csharp
builder.AddThargaTeam(o =>
{
    o.Blazor.Simulation.Enabled = true;
});
```

Then place the control wherever it belongs in your layout — typically the header, beside the team
selector:

```razor
<AccessSimulationBar />
```

That one component is both halves: the **"View as…"** entry point when nothing is simulated, and the
**banner with the way out** when something is. Placing it wires up both.

Off by default. A host that does not enable it is unaffected — the cookie is never read, the filter is
never reached, and the audit enricher is not even registered.

### The card on the profile page

`<UserProfileView />` renders `<AccessSimulationCard />` between the profile details and Claims whenever
simulation is enabled and the caller can simulate. It offers the same two things as the bar — a way in and,
while something is active, a way out — in a place that does not take up room on every page.

Nothing to wire: it is on by default. Set `ShowAccessCard="false"` on `UserProfileView` to place
`<AccessSimulationCard />` somewhere else yourself. Two copies on one page would each carry their own way
out, which reads as a bug rather than as redundancy.

### Turning either half of the bar off

Both halves of `AccessSimulationBar` can be suppressed independently, because the card offers the same
things elsewhere:

```razor
<AccessSimulationBar ShowEntryPoint="false" ShowBanner="false" />
```

| Parameter | Effect |
|---|---|
| `ShowEntryPoint="false"` | No "View as…" button here. Start a simulation from the card instead |
| `ShowBanner="false"` | No warning banner while a simulation is active — **and no way out here** |

> **`ShowBanner="false"` moves the exit, it does not remove the need for one.** The banner is the way back
> that does not depend on remembering where anything lives; switching it off makes the access card the only
> one. That is supported — it is why the card exists — but the profile page must stay reachable under a
> reduced session. Gate it on a scope a simulation can remove and there is no way back but signing out.
>
> The reason to do it is a **demonstration**: a full-width warning across every page is exactly what demo
> mode was turned on to avoid. For the access-checking job the feature was built for, leave it alone —
> being loud is the point.

### A compact indicator beside the team selector

For a quieter marker than the banner, place `<AccessSimulationIndicator />` in the header — typically right
beside `<TeamSelector />`:

```razor
<AccessSimulationIndicator />
<TeamSelector />
```

While a simulation is active it shows an **Access reduced** badge, with the banner's sentence as its tooltip,
and a button that returns you to your own access. Otherwise it renders nothing. It is optional and takes no
parameters; it does not start a simulation, so pair it with the card or the bar's entry point.

**It renders nothing during demo mode**, for the same reason the banner does not: a demonstration is meant
to look like an ordinary member's session. The profile card stays the way out of a demo.

## Who can use it

**Two capabilities, two grants.** They were one until 3.13.2, which is the fix for
[#223](https://github.com/Tharga/Team/issues/223).

| Capability | Scope | Kind | Reaches |
|---|---|---|---|
| **Run as** — view as another member, access level, role or scope set | `simulation:use` | **team**, at `Administrator` | Team owners and administrators, deliberately |
| **Demo mode** — drop your own system scopes and application roles | `simulation:demo` | **system** | Only a principal holding a system grant |

Run-as reaching every team owner is intended: checking what a Viewer sees before inviting one is an
ordinary tenant-owner task. Owner and Administrator are granted every registered scope, so that follows
without the toolkit hard-coding either level.

**Demo mode is a system scope because of what it does.** It removes system scopes and application roles —
so for a caller holding none, which is every customer's own team owner, it offered to drop nothing. It was
inert for exactly the audience that used to see it. Resolve it with `TeamScopeGate.HasSystemScope`, never a
bare `HasClaim`: an in-team claim spelled `simulation:demo` must not satisfy it.

> **Upgrading from 3.13.1 or earlier?** Both halves used to sit behind `simulation:use`. Nothing is lost
> silently — demo mode was never usable by a caller without system scopes anyway — but if your **staff**
> reached demo mode through a team-level grant, they now need `simulation:demo` mapped to a system role or a
> system API key. Run-as is unchanged.

> **Already registering `simulation:demo` yourself? Leave it or delete it — both work.** Before 3.14.0 no
> library code registered this scope, so a host that wanted demo mode had to register it. From 3.14.0 the
> library registers it too, and in 3.14.0 exactly that collision threw
> `System scope 'simulation:demo' is already registered.` at startup
> ([#237](https://github.com/Tharga/Team/issues/237)).
>
> **Fixed in 3.14.1:** registering a system scope that already exists is a no-op, whichever side got there
> first and whether or not the descriptions match. Your own registration is now harmless, so there is no
> upgrade step — delete the line if you want the library's catalogue description to show, keep it if you
> prefer your own wording, since the **first** registration's description is the one that is kept.

> **On 3.21.1 or earlier, run-as needed `users:manage` in practice.** The table above was the intent, but
> the member picker resolved each member's display name through a read gated on the `users:manage`
> *system* scope — which no team access level grants. Opening **View as another user…** as an ordinary team
> Owner or Administrator failed with
> `GetUserByKeyAsync requires the 'users:manage' system scope.`
>
> **Fixed in 3.21.2:** names come from the caller's own co-member projection, so run-as needs nothing beyond
> `simulation:use`. No upgrade step, and nothing to un-grant — but if you widened a role to `users:manage`
> to work around this, that grant also carries user deletion, and you can now narrow it again.

**Where each one shows.** A run-as simulation puts a banner in the navigation bar, because somebody working
with a reduced view needs to know. **A demo shows nothing there at all** — a banner reading "demo mode"
across a customer demonstration defeats the point of it — so the profile card is the way out. That is a rule
rather than an option; see *Turning either half of the bar off* for the hazard it inherits.

You can also switch the navigation controls off entirely:

```csharp
o.Blazor.Simulation.ShowInNavigation = false;   // default true
```

The component's own `ShowEntryPoint` and `ShowBanner` parameters still win where set, so placing the bar by
hand keeps full control.

**Ending a simulation is never gated.** A simulation can remove `simulation:use` itself, so requiring it
to stop would let someone strand themselves.

**Whether the controls appear is answered from your claims.** With no simulation active the principal
already carries the scopes the grant resolver issued, so neither the bar nor the card queries the team
store to decide whether to draw itself — which matters on a host that has replaced the toolkit's team cache
with one that caches nothing. While a simulation *is* active the claims have been filtered, so the real
grant is resolved instead: that is the one case where it cannot be read off the principal, and it is why
simulating `simulation:use` away does not lock you out of your own picker.

Because it reads claims, a grant changed mid-session reaches these controls at the next claim
revalidation — the same freshness every other scope-gated surface in the toolkit has.

## Translating it

Every simulation component routes its wording through `IThargaTextProvider`:

| Component | Keys |
|-----------|------|
| `AccessSimulationCard` | `team.simulation.card.*` — ten keys |
| `AccessSimulationBar` | `team.simulation.bar.*` — five keys |
| `AccessSimulationDialog` | `team.simulation.dialog.*` — eighteen keys |
| `AccessSimulationIndicator` | `team.simulation.indicator.*` — two keys, plus the bar's sentence for its tooltip |

Enumerate `ThargaTextKeys.All` to generate the table with the English defaults; the banner's keys arrive
there like any other.

**The banner sentence is one key, not three.** `team.simulation.bar.viewingAs` is
`"Viewing as {0} — your own access is reduced."`, where `{0}` is what is being simulated. Translating a
whole sentence lets you put the target where your language wants it; a "viewing as" prefix and a "your
access is reduced" suffix would hard-code English word order. `team.simulation.bar.targetRole` and
`.targetAccessLevel` do the same for naming a role or a level. A translation that drops the `{0}` renders
the sentence without naming the target rather than failing.

**Simulation labels are deliberately not translatable** — `AccessSimulationTargets.DemoLabel`, and the label
a composed simulation is given (see below). They are written to audit metadata, where a value that varies by
operator language cannot be searched or compared.

## What you can simulate

The **View as another user** dialog builds one set of scopes from any combination of:

| Part | What it adds |
|------|--------------|
| **An access level** | The scopes that level grants — every level except `Custom`, which grants none and is what choosing no level already means |
| **One or more roles** | The scopes each tenant role grants — code-registered roles, and custom roles when dynamic roles are enabled |
| **Scopes ticked by hand** | Anything else from the list |

Every registered team scope is listed as a checkbox, with a search box. Scopes the level or a role grants
show **checked and fixed**, marked *from level or role*: unticking one would be simulating a different level
or role rather than that one. Scopes **you do not hold** are listed but greyed out and marked, because a
simulation cannot keep them — the dialog says so above the choices.

**Starting from a member** fills in that member's access level, roles and scopes in one step, so the target
is exactly the access they hold. Change anything afterwards and it becomes a composed target of your own.

**How members are named.** A per-team name override wins where one is set; otherwise the name comes from the
user record — their display name, else a name derived from their email. The records come from the caller's
own co-member projection, or the full directory for a holder of `users:manage`. A member whose user record
is not in that set shows their raw key, which in practice means a member of a team you reach by consent
rather than membership, without `users:manage`.

Whatever is chosen, it works the same way: the dialog names a **target scope set**, and the simulation keeps
what the target has *and you also have*, removing everything else. Choosing nothing is allowed, and shows the
application with no team scopes at all.

**A level or role replaces your access, it does not add to it.** Simulating the `Support` role leaves you
with `Support`'s scopes and nothing more — not your own plus its.

**Demo mode** is the other target: your own team access, unchanged, with your system-wide access dropped. It
has its own button on the profile card.

### Demo mode

The one target that is not somebody else's access. It names **your own**, so the intersection removes
nothing within the team and the only thing that changes is the system half.

Use it to demonstrate the product: a system user — a Developer, say — selects a team, starts demo mode, and
the audience sees what a member of that team sees instead of the cross-team administrative surface. Stop it
and the system scopes come back.

- **Your team access is untouched**, including access that reaches you through consent rather than
  membership, and including your access level. No level is applied, so nothing is clamped.
- **Your system scopes and application roles go**, which is what removes the wider team list and the
  developer-only surfaces.
- It is started from the **card on the profile page** — one button, no target to pick.

In the audit log a demo records `simulation.kind = Demo` with `simulation.target = Demo mode`.

## What it cannot show, and why you are told

Before you apply a simulation, the picker tells you what it will **not** be able to reproduce.

This matters more than it sounds. If the target holds a scope you do not, the simulation shows the
intersection — **less than they really see**. Without a warning you would conclude *"they cannot reach
the billing page"* about something they can, and grant them more access than they need. That is the exact
outcome the feature exists to prevent, so the gap is stated rather than left silent.

Two things can be missing:

- **Scopes you do not hold yourself.** Rare for an administrator, who holds every *registered* scope —
  but a member's `ScopeOverrides` are not validated against the registry, so they can carry a scope no
  access level grants.
- **System-wide access.** Always, when simulating a person. System scopes come from application roles
  issued by your identity provider, which the toolkit does not store — so another user's system access is
  *unknown*, not empty.

**A simulation therefore shows access within the selected team, never someone's system-wide reach.** Your
own system scopes and application roles are dropped for every kind of simulation, so you will lose
cross-team visibility — including the wider team list — until you return.

## What it cannot reach

**Simulation filters claims.** A component that queries the store directly sees your real record however
thoroughly your claims were narrowed — a member record still says `Owner`, because nothing about your
stored access changes.

Everything in the toolkit routes authorization through claims, so this is invisible in normal use. If you
write a component that defaults UI state from a stored record, ask:

```csharp
if (AccessSimulationCookie.IsActive(principal)) { /* prefer the claim, not the record */ }
```

## Auditing

Anything done while simulating is **still recorded as you**. Simulation removes scopes and roles and
never touches identity claims, so the actor is the real person by construction.

Entries gain three metadata keys, so an otherwise puzzling record is legible — why an administrator's
action was refused, or performed at a level below the one they hold:

| Key | Value |
|-----|-------|
| `simulation.active` | `true` |
| `simulation.kind` | `User` for an unedited member pick · `Composed` for anything built in the dialog · `Demo` · and `Role`, `Scopes`, `AccessLevel` from earlier releases |
| `simulation.target` | The member's name; for `Composed`, the parts joined with ` + ` — e.g. `Viewer + Editor + reports:export` |

**This covers entries written from an interactive component**, not only from a controller. A circuit has no
`HttpContext` to read the caller from, so the toolkit publishes the circuit's principal for the length of
each inbound activity and the enricher reads the simulation from there — the same claim the HTTP path
reads, so the two cannot disagree about what was in force. No host wiring: enabling simulation registers
it.

## How it works

The active simulation rides in a session cookie, read once per request and carried on the principal
thereafter — the same pattern the selected team uses, and necessary because a live Blazor circuit has no
`HttpContext`.

**The cookie is not signed, and does not need to be.** The filter can only ever *remove* claims, so
editing the cookie to name scopes you do not hold achieves nothing. That is why the guarantee is a
property of the mechanism rather than of a calculation being correct.

**A simulation belongs to the team it was started in.** It records that team, and applies only while that
team is selected; on any other team it is ignored and you see your real access there. While it is active,
the selected team stays selected even if the simulation removed what let you see it — a team reached through
consent or `teams:read` — and ending it returns you to the same team. This was
[#276](https://github.com/Tharga/Team/issues/276): simulations used to follow you to a fallback team and apply
there.

> **Upgrading?** A simulation started before this release carries no team and no longer applies. Anyone
> mid-simulation sees their real access after the upgrade, and starts a new one if they still want it.

Starting or stopping writes the cookie and reloads the page, which re-issues claims through the ordinary
request path. The filter is applied on both claim-issuance paths — the HTTP one and the periodic
in-circuit revalidation — so a simulation does not quietly expire at the next revalidation interval.

**Access level is the one thing replaced rather than removed**, because `[RequireAccessLevel]` reads a
single value and `AuthorizeView Roles="Team…"` reads the matching role. Both move together, clamped so
the simulated level is never more privileged than the real one.

## Simulation does not end at the browser

The reduced claims are your claims. If your host authenticates its API with the cookie scheme, **your own
REST calls are de-escalated too** while a simulation is active. That is deliberate — the alternative is a
claim set that differs by surface, which is exactly the confusion the toolkit's one-enforcement-point rule
exists to avoid.

It does not apply to API keys. A key's scopes are directly editable, so simulating one would earn nothing.
