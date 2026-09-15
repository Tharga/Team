# Plan: TeamAvatar keeps its declared size in a flex row

GitHub [#282](https://github.com/Tharga/Team/issues/282). NuGet: `dotnet outdated` reported
**no outdated dependencies** at feature start, so there is no `chore(deps)` commit.

## Steps

- [x] 1. Failing test first — `TeamAvatarShapeTests`, mirroring `UserAvatarShapeTests`.
  Confirmed red on both branches: `flex-shrink:0` absent from the `<span>` and the `<img>`.

- [x] 2. Fix `TeamAvatar.razor` — `flex-shrink:0` added to both inline styles, matching
  `UserAvatar` exactly. Nothing else about either branch changed.

- [x] 3. Guard against a third occurrence — `AvatarShrinkGuardTests` scans every `.razor` under
  `Tharga.Team.Blazor` for a style attribute whose width comes from `@Size`, and requires
  `flex-shrink:0` on it. Keyed on the *shape* of the defect, not on a component name, so a
  third avatar written the same way fails on the build that adds it.
  - One entry per **style attribute**, not per file: a component has an image branch and an
    initials branch, and a file-level check would let one pass on the other's declaration.
  - Self-checks: the scan must still find both avatars and both of their branches (4 matches),
    and the predicate must still reject the unpinned shape.

- [x] 4. Full suite — **2,768 tests, 0 failed** (Blazor 1183, Service 963, Support 363,
  Mcp 107, MongoDB 103, Entra 38, Images 11). Solution builds clean.

- [x] 5. Docs review — `icons.md` describes where avatars render and the icon-resolution chain;
  nothing documents layout, sizing or CSS. `README.md` and `implementation-guide.md` likewise.
  **No doc change needed** — this is a rendering fix with no consumer-visible API or
  configuration surface. (`docs/_site/` is generated output and was not touched.)

- [ ] 6. Close-out (only on the user's confirmation) — re-run `dotnet outdated`, comment on and
  close #282, archive `plan/feature.md`, `git rm -r plan`,
  `fix: team-avatar-no-shrink complete`.

## Last session

Steps 1-5 done. `TeamAvatar` no longer shrinks in a flex row, and the guard closes the class
rather than the instance — this was the second report of the identical defect (#279 → #281 for
`UserAvatar`, then #282 for `TeamAvatar`), which is the point at which a review habit has
demonstrably not held.

Not browser-verified: no one has rendered a long-named, icon-less team in a narrow header.
Next: push, user tests from origin, then close-out.
