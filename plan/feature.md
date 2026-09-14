# Feature: user-avatar-round

## Goal
Keep the `UserAvatar` badge round inside a flex row. Fixes GitHub issue Tharga/Team#279.

## Problem
Both branches of `UserAvatar.razor` (the `<img>` and the initials `<span>`) set `width` and `height`
inline, but a flex item defaults to `flex-shrink: 1`. The span's automatic minimum size is the
min-content width of its initials, so a crowded flex row shrinks it below `Size` and it renders as an
oval (measured 11.7 x 24 px instead of 24 x 24 px). The `<img>` is unaffected only because a replaced
element resolves its minimum from its specified width.

## Scope
- Add `flex-shrink:0` to the inline style of both the `<img>` and the initials `<span>` in
  `Tharga.Team.Blazor/Features/User/UserAvatar.razor`.
- A bUnit test in `Tharga.Team.Blazor.Tests` that renders both branches and asserts the style carries
  `flex-shrink:0`.
- NuGet updates applied up front as a separate commit.

Out of scope: any other change to `UserAvatar` markup or sizing.

## Acceptance criteria
- The initials badge renders with `flex-shrink:0` in its inline style.
- The picture avatar renders with `flex-shrink:0` in its inline style.
- A test pins both, and was seen failing before the fix.
- Full solution test suite passes.

## Done condition
The user has tested the branch and confirmed; then close-out per the Feature Workflow (comment on and
close #279, archive this file, remove `plan/`).
