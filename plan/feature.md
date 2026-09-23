# Feature: host extension points on TeamComponent's member grid

[Tharga/Team#294](https://github.com/Tharga/Team/issues/294), reported on `Tharga.Team.Blazor` 3.21.2.
Branched from `origin/master` at `1dac7e8` (PRs #297, #298 and #299 all merged); `dotnet outdated` clean, so
no leading `chore(deps)` commit.

## The gap

`TeamComponent<TMember>` exposes twelve parameters and **not one `RenderFragment`**. A host that extends the
member model therefore has nowhere to put the UI for it — no extra member-row action, no extra column.

Verified against master 2026-09-23: every parameter is a bool or a value, plus `OnMemberNameChanged` and
`CreateTeamRequested`.

The reported cost is concrete. A consumer binding a member to a project has to render a **second grid below
the member list**, duplicating the Member and Access columns directly above it. That second surface needs its
own scope check, its own load and its own reaction to `SelectedTeamChangedEvent` — all of which
`TeamComponent` already does — and the two lists can disagree on screen because nothing keeps them in step.

## This is the family's own shape, applied to the one surface missing it

`TeamsListView` already exposes exactly this, and the pattern is settled:

| Member | Kind | Where it renders |
|---|---|---|
| `TeamActionsTemplate` | `RenderFragment<TeamViewModel>` | beside the built-in control |
| `TeamActionItems` | `RenderFragment` of `RadzenSplitButtonItem` | inside the split-button dropdown |
| `TeamActionInvoked` | `EventCallback<TeamRowAction>` | a click on one of those items |

`UsersListView` has the same trio, and the `UsersView` wrapper forwards both. `TeamComponent` is simply not
in that list, and the issue is right that the omission looks unintended rather than decided.

**So this adds no new concept.** The work is mirroring a shape the library already ships, on the component
that lacks it.

## What gets added

```csharp
[Parameter] public RenderFragment<TMember> MemberActionsTemplate { get; set; }
[Parameter] public RenderFragment MemberActionItems { get; set; }
[Parameter] public EventCallback<MemberRowAction<TMember>> MemberActionInvoked { get; set; }
[Parameter] public RenderFragment MemberColumns { get; set; }
```

**`MemberColumns` is a raw `RenderFragment` appended inside the grid's own `<Columns>`**, not a trailing
per-row template. The issue offers both and the raw form is the right one here: the reported need is a
*named* column, and a per-row template gives cell content with no header, no sorting and no width. The host
supplies `RadzenDataGridColumn` elements, which is what they would write anyway.

`MemberRowAction<TMember>(string Action, TMember Member)` mirrors `TeamRowAction` / `UserRowAction`, generic
because this component is.

## Not in scope

- **Persistence for a host's added member fields.** The issue raises it and then sets it aside itself: *"a
  host extending `TMember` also has to write its own persistence path… That may be deliberate."* It is a
  separate question from a UI hook, and conflating them would make this change unreviewable. Worth filing on
  its own if a consumer needs it.
- Migrating `TeamComponent` off anything. It is already `Migrated` in the text ratchet at zero literals, so
  **any string added here fails the build** — which is correct, and means the new members carry no display
  text of their own.

## Acceptance criteria

- [ ] A host can append an action to a member row's menu and receive its click.
- [ ] A host can place a control beside the built-in member action button.
- [ ] A host can append a column, with its own header, to the member grid.
- [ ] Supplying none of them changes nothing — every existing consumer renders exactly as before.
- [ ] The hooks reach both layouts the component renders (card and grid), not just one.
- [ ] `TeamComponent.razor` still reports zero literal strings to the text ratchet.
- [ ] Full suite green.

## Done condition

Criteria met, the issue commented and closed with what shipped, docs updated on both surfaces, `plan/`
removed in the close-out commit, PR open.

## Version

Additive and opt-in — four new parameters and one new record, all defaulting to nothing. **No `MAJOR_MINOR`
bump**; a consumer who supplies none of them is unaffected.
