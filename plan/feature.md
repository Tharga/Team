# Feature: A team names itself

Fixes [#254](https://github.com/Tharga/Team/issues/254).

## Goal

`TeamEntityBase.ToString()` returns the team's name, so no host's team entity can dump its full
state into rendered markup.

## What is actually wrong (corrects the issue's own diagnosis)

The issue guesses that "a `TextProperty` / display selector is missing". It is not.
`TeamSelector.razor:44` already sets `TextProperty="Name"`.

Measured against Radzen.Blazor 11.2.5 by rendering a dropdown bound to a record, the markup is:

```html
<div role="combobox" aria-label="Arjeplogs kommun">          <-- correct
  <div class="rz-helper-hidden-accessible">
    <input aria-hidden="true" readonly
           value="TeamEntity { Id = ..., Key = ..., SyncedIconSource = https://... }"
           aria-label="TeamEntity { Id = ..., ... }" />      <-- the dump
  </div>
  <span class="rz-dropdown-label">Arjeplogs kommun</span>    <-- correct
  ...
  <li role="option" aria-label="Arjeplogs kommun">           <-- correct
```

Radzen feeds that hidden input from `Value.ToString()` and ignores `TextProperty` there. There is no
Radzen parameter to override it, so `ToString()` is the only lever.

Two consequences for how the issue is worded:

- **The screen-reader claim is wrong.** The combobox root carries the correct `aria-label`, and the
  dump-bearing input is `aria-hidden="true"`, so assistive technology does not announce it.
- **The exposure and tooling claims are right.** The dump is in the DOM of every page for every user —
  entity id, consent access level, external icon URL, and whatever properties a host has added to its
  own entity — and anything reading labels out of the DOM gets it.

`TeamSelector` is the only control in the library that binds an entity object as its value; every other
`RadzenDropDown` binds a primitive (`string`, `AccessLevel`, `IEnumerable<string>`), most with an explicit
`ValueProperty`. So the reported surface is the only current one — but the fix belongs on the entity,
because a host binding its own team entity to its own control has the same problem and no way to know it.

## Scope

- `TeamEntityBase<TTeamMemberModel>` gains `public sealed override string ToString() => Name;`
- `sealed` is the crux, not decoration: a C# record synthesizes its own `ToString()` in **every**
  declaration unless a base declares a *sealed* override. A plain `override` would be silently
  re-synthesized by each host's `record TeamEntity : TeamEntityBase<TeamMember>;` — leaving the bug
  exactly where it was reported. A test proves this on a derived record, not on the base.

## Out of scope

- Changing `TeamSelector` to bind `Key` instead of the entity. It would work, but the visible text and
  every real accessibility attribute are already correct; the entity binding is not itself the defect.
- The other entity records (`DefaultUserEntity`, `SupportCaseEntity`, `IconEntity`, ...). None is bound
  to a control anywhere in the library. Fixing them now would be building on spec.

## Acceptance criteria

1. `ToString()` on a record **derived** from `TeamEntityBase` returns the team's name.
2. A test-local record deriving from `TeamEntityBase` — standing in for a host's own entity, including
   extra properties the toolkit has never seen — also returns the name.
3. Rendering `TeamSelector` with a selected team produces markup containing no property dump.
4. Full test suite green.

## Done condition

All four criteria met, docs reviewed, issue #254 answered and closed with what shipped.
