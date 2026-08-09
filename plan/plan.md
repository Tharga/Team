# Plan: host-supplied profile-menu items

## Steps

- [x] 1. NuGet check. Only `SixLabors.ImageSharp` 3.1.12 -> 4.0.0, held for its paid build-time licence.

- [x] 2. Placement and gating confirmed with the user before building: the `LoginDisplay` profile menu, with
      an optional scope/role gate.

- [x] 3. `TeamMenuItem` + `TeamMenuItemVisibility` in `Framework`. The visibility rule is pure, so it is
      asserted by tests rather than living in markup.

- [x] 4. `ThargaBlazorOptions.AddMenuItem(...)`, validating icon / key / href up front.

- [x] 5. `LoginDisplay` renders visible items between the built-ins and Logout. Labels resolved once in
      `OnInitializedAsync`; host items matched on `Value` in `ClickAction` **before** the icon switch, which
      throws on anything unknown.

- [x] 6. Tests - `TeamMenuItemTests`, 11 cases: registration and order, refusal of unusable input, ungated
      visibility, scope gate, system-scope provenance, role gate, both-gates-must-hold, anonymous hidden.

- [x] 7. Build + full suite: all 7 assemblies green, warnings at the **11** baseline.

- [x] 8. Documentation - a new "Adding your own profile-menu items" section in the implementation guide,
      beside the existing text-provider section, including the rendering-not-access warning.

- [ ] 9. Close-out: archive `feature.md` to the Plan directory `done/`, `git rm -r plan`, final commit, push,
      open the PR. **Only when the user confirms.**

## Notes / decisions

- **Label is a `TextKey`, not a string.** That is what makes per-language work with no new mechanism, and it
  was the reason for bundling this with #204.
- **Matched on `Value`, not `Icon`.** `ClickAction` keys the built-ins off `Icon` and throws on unknown ones;
  a host reusing `group` would otherwise trigger the wrong action.
- **Both gates must hold when both are set** - the narrower reading, and the safer default.

## Last session

Steps 1-8 complete. Nothing pushed, no PR.

**#204 itself is not started** - sized at ~74 display strings and needing a bulk-resolve helper first, because
the text provider is async. Its own branch. See `feature.md`.

Audit completed this session: nineteen records described finished work as outstanding. Plan 06's contents and
the ReSharper-warnings item remain unverified.
