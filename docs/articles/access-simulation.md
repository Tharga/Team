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

## Who can use it

Anyone holding the **`simulation:use`** scope, registered at `AccessLevel.Administrator`. Since
Owner and Administrator are granted every registered scope, that means **team owners and administrators**
by default — without the toolkit hard-coding either. Widen it by granting the scope to a tenant role, or
withhold it by re-registering it at a level nobody has.

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

Both components route their wording through `IThargaTextProvider`:

| Component | Keys |
|-----------|------|
| `AccessSimulationCard` | `team.simulation.card.*` — ten keys |
| `AccessSimulationBar` | `team.simulation.bar.*` — five keys |

Enumerate `ThargaTextKeys.All` to generate the table with the English defaults; the banner's keys arrive
there like any other.

**The banner sentence is one key, not three.** `team.simulation.bar.viewingAs` is
`"Viewing as {0} — your own access is reduced."`, where `{0}` is what is being simulated. Translating a
whole sentence lets you put the target where your language wants it; a "viewing as" prefix and a "your
access is reduced" suffix would hard-code English word order. `team.simulation.bar.targetRole` and
`.targetAccessLevel` do the same for naming a role or a level. A translation that drops the `{0}` renders
the sentence without naming the target rather than failing.

**`AccessSimulationTargets.DemoLabel` is deliberately not translatable.** It is written to audit metadata,
where a value that varies by operator language cannot be searched or compared.

> **Not yet translatable:** `AccessSimulationDialog`, the "View as another user" screen. The way *out* of a
> reduced session translates; the way in does not, yet.

## What you can simulate

| Target | What it means |
|--------|---------------|
| **A member** | The access that person actually holds in this team |
| **A role** | Exactly what that tenant role grants |
| **An access level** | Exactly what that level grants |
| **Scopes** | A set you tick by hand, from the scopes you hold |
| **Demo mode** | Your own team access, unchanged — with your system-wide access dropped |

They all work the same way: each names a **target scope set**, and the simulation keeps what the target
has *and you also have*, removing everything else.

**Applying a role replaces, it does not add.** Simulating the `Support` role leaves you with `Support`'s
scopes and nothing more — not your own plus its.

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

In the audit log a demo records `simulation.kind = Scopes` with `simulation.target = Demo mode`, so it is
distinguishable from a hand-picked scope simulation by the target rather than the kind.

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
| `simulation.kind` | `User` · `Role` · `Scopes` · `AccessLevel` |
| `simulation.target` | The member, role or level being simulated |

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
