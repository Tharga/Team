# Feature: TeamAvatar keeps its declared size in a flex row

GitHub: [Tharga/Team#282](https://github.com/Tharga/Team/issues/282).

## Goal

A `TeamAvatar` rendering initials keeps its declared `Size` in both dimensions when it sits in a
flex row beside text that wants more room, instead of being squeezed into a narrow rectangle.

## The defect

Both branches of `TeamAvatar` set `width` and `height` inline, but a flex item's `flex-shrink`
defaults to `1`. The `<span>`'s automatic minimum size is its min-content width — just the
initials — so the flex algorithm may shrink it below `Size`. The `<img>` branch resolves its
minimum from the specified width, so only the initials badge is visibly affected.

Measured on the identical `UserAvatar` defect (#279): 11.7 × 24 px instead of 24 × 24 px.

## Where it shows

`TeamAvatar` sits beside a team name in a flex row in:

- `Features/Team/TeamSelector.razor` — selected team (24px) and list entries (20px)
- `Features/Team/TeamComponent.razor` — 24px
- `Features/User/TeamsListView.razor` — 32px

A team with no icon and a long name, in a narrow header or column, gets a squeezed badge.

## This is the second occurrence

`UserAvatar` had the same defect, reported as #279 and fixed in #281 by adding `flex-shrink:0`
to both branches. `TeamAvatar` was not changed at the time. A sweep confirms these are the only
two avatar components in the library, and the icon dialogs delegate to them rather than
hand-rolling a preview — so fixing this closes the class, not just the instance.

## Scope

- `Features/Team/TeamAvatar.razor` — `flex-shrink:0` on both branches, matching `UserAvatar`.
- A shape test beside `UserAvatarShapeTests`, pinning both branches.
- A scan-based guard so a *third* avatar cannot reappear without it. Two identical reports is
  the point at which a review habit has demonstrably not held.

## Out of scope

- Any change to avatar sizing, shape, colour or the icon-resolution chain.
- `UserAvatar`, which is already correct.

## Acceptance criteria

- [ ] The initials `<span>` carries `flex-shrink:0`.
- [ ] The `<img>` branch carries `flex-shrink:0`, so the two branches cannot drift.
- [ ] A guard fails if any avatar component sets an inline `width` from a size parameter
      without `flex-shrink:0`, and the guard proves it can actually see both components.
- [ ] Nothing else about the rendered avatar changes.

## Done condition

Full suite green, issue #282 closed with what shipped, user confirms.
