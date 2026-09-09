# Feature: two registration holes in AddThargaTeamBlazor

**Issues:** [Tharga/Team#261](https://github.com/Tharga/Team/issues/261) and
[#265](https://github.com/Tharga/Team/issues/265) (the same defect, reported twice),
[#266](https://github.com/Tharga/Team/issues/266)
**Type:** fix
**Target release:** 3.21 — no bump; 3.21 is still unpublished, so these ride with it
**New published packages:** none

## Goal

An application that references `Tharga.Team.Blazor` starts, and its icon dialogs open. Neither is true on
3.20.1.

## The two defects

### 1. The management service cannot be constructed (#261, #265)

`AddThargaTeamBlazor` builds `TeamManagementService<TMember>` with `Activator.CreateInstance` and a
positional argument list of **six**. The constructor takes **seven**, the last with a default value —
added by #249.

**`Activator.CreateInstance` does not fill in optional parameters.** So a change that is source-compatible
everywhere else in C# broke every host declaring a member type, at the first resolve, with
`MissingMethodException` — while the build was clean and the whole suite passed.

The reflective construction itself is deliberate and stays: the container would otherwise pick the
greediest constructor it can *fully* satisfy, so one unregistered optional dependency would silently
select a lesser overload and quietly go back to refusing reads it should allow. The cost of that choice is
an argument list that has to be kept in step by hand, and nothing was keeping it.

### 2. The icon dialogs kill the circuit (#266)

`TeamIconDialog` and `UserIconDialog` inject `IIconProcessor` as required. `AddThargaTeamBlazor` never
registered one.

The no-op default existed — but was registered by **`Tharga.Team.MongoDB`**, which is the wrong package to
own it: it is not the one whose components need it, and a host is not obliged to use it. So a host on
`Tharga.Team.Blazor` without that path got a dialog that terminated the circuit, and a user got a menu item
that did nothing.

The rest of the library documents the processor as optional and absent-by-default —
`IconCapability.CanProcessImages` explicitly handles null, and the docs say *"without one, icons are stored
as-is"*. **The documentation was right and the registration was wrong**, so no doc change is needed.

## Why they arrived, which is the part worth fixing properly

Both were invisible to the existing tests, for the same reason twice.

`TeamServiceRegistrationCompletenessTests` asserts a **descriptor exists** for every facet. That stays true
however broken the factory behind it is, because a factory lambda is not run until something resolves it.
And nothing at all connected *"this component injects X"* to *"the registration provides X"* — a Blazor
`@inject` is resolved at render time, so container validation never reaches it either.

So the fixes are two lines, and the guards are the work:

- **`TeamServiceResolutionTests`** resolves every facet, and the concrete management service, from a real
  container. Verified against the defect: with the seventh argument removed, 6 of its 7 tests fail.
- **`ComponentDependencyResolutionTests`** walks every `[Inject]` on every component in the package, keeps
  the Tharga-owned types, and resolves each. Verified against the defect: with the processor registration
  removed, it fails naming `IIconProcessor`.

## Out of scope

- Removing `Tharga.Team.MongoDB`'s own `TryAddScoped<IIconProcessor, NoOpIconProcessor>`. It is harmless
  (TryAdd) and a non-Blazor host still needs it.
- The support-module services the component guard excludes. A component injecting one still dies where the
  host has not added that package — the difference is that the fix there is a decision (ship the seam, or
  make the component conditional) rather than a missing line. Recorded in the guard's own exclusion list.

## Acceptance criteria

1. A container built by `AddThargaTeamBlazor` with a member type resolves every facet and the concrete
   management service.
2. Every Tharga-owned service injected by a component in this package resolves from that same container.
3. Both guards demonstrably fail when their defect is reintroduced — checked, not assumed.
4. Full suite green.

## Done condition

All criteria met, all three issues answered and closed (#265 as a duplicate of #261), and the user has
confirmed.
