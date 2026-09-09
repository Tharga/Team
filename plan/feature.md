# Feature: a Razor comment inside a tag kills the circuit

**Issue:** [Tharga/Team#268](https://github.com/Tharga/Team/issues/268)
**Type:** fix
**Target release:** 3.21 — no bump; 3.21 is still unpublished
**New published packages:** none

## Goal

`/team` renders for a viewer holding an access level, and the Create-team button works.

## The defect

`TeamComponent.razor` put a Razor comment **inside the attribute list** of `<RadzenBadge>`. Razor does not
strip a comment in attribute position — it emits it as an attribute name, the browser rejects the
`setAttribute`, the render batch fails to apply, and the circuit is terminated.

The badge is in the "Your access" column, so the page died for **any viewer holding an access level on a
team** — and took the Create-team button with it, meaning a host that had configured `CreateTeamPath` could
never reach its own page.

Present in 3.20.1, the published release. No workaround but pinning.

## Scope

- Move the comment above the tag. **Exactly one occurrence library-wide**, confirmed by scanning every
  `.razor` file rather than assumed from the report.
- A source-scanning guard, because nothing that runs can see this class of defect: it compiles, and it
  fails in a browser applying a render batch. No unit test, container check or architecture test reaches
  that.

## Why the guard is a character scan rather than a pattern

The first attempt was a regular expression, and it **reported the shipped defect as clean**. The attribute
value on the offending line is `BadgeStyle="@(Enum.Parse<BadgeStyle>(...))"` — angle brackets inside an
attribute value — so any pattern treating `>` as the end of a tag walks straight past the one line it exists
to find. The scan tracks quotes and parentheses instead, and a test pins exactly that case.

## Also in this branch

**A follow-up recorded when the invitation throttle shipped, unblocked by #267 merging.** That PR added a
container-resolution harness; the throttle's own tests cover its registration extension but not the single
line in `AddThargaTeamBlazor` that calls it. One test now resolves `ITeamInvitationService` and asserts it
is the throttled one. **It landed in the same commit as the fix rather than its own, which was the
intention** -- `git add -A` swept it in. Recorded rather than rewritten: the history is not worth
re-cutting for it, but the commit message does not mention it and this note is where that is said.

## Acceptance criteria

1. No Razor comment sits inside a tag in any `.razor` file in the package.
2. The guard fails when the defect is reintroduced — checked against the real file, not assumed.
3. The guard cannot pass while scanning nothing, and is not fooled by angle brackets inside attribute
   values.
4. The invitation throttle is asserted to be applied by the registration, not merely registrable.
5. Full suite green.

## Done condition

All criteria met, #268 answered and closed, and the user has confirmed.
