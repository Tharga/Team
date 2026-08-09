# Feature: host-supplied profile-menu items

## Goal

Let a host add its own entries to the `LoginDisplay` profile menu — icon, label, link — localized through the
mechanism that already exists, and optionally hidden from callers who cannot use them.

## Why this shape

Requested alongside #204. The label is a **`TextKey`**, not a string, so an injected item resolves through the
same `IThargaTextProvider` the built-in entries use. A host already bridging that provider to its own content
system gets translated menu items with no further work, and there is no second localization mechanism to keep
in step with the first.

## Scope

- `TeamMenuItem` — icon, `TextKey`, href, optional `RequiredScope` / `RequiredRole`.
- `ThargaBlazorOptions.AddMenuItem(icon, textKey, defaultText, href, requiredScope, requiredRole)`.
- `TeamMenuItemVisibility.IsVisible(principal, item)` — pure and separately testable.
- `LoginDisplay` renders visible items between the built-ins and Logout, resolving labels once in
  `OnInitializedAsync` rather than per render, because the provider is async and may reach an external source.

## Two traps found while building

**`ClickAction` switches on `args.Icon` and throws on anything unknown.** A host item with any icon would have
hit the `default:` branch and thrown `ArgumentOutOfRangeException`. Host items are therefore matched **first**,
on `Value` (the href) rather than the icon — which also means a host reusing `group` or `logout` navigates to
its own link instead of triggering a built-in action.

**A scope may arrive by either provenance.** The gate accepts a team scope *or* a system scope, so a
cross-team administrator is not hidden from a link because their grant came the other way.

## Acceptance criteria

- [x] Registration records icon, key, default and href, in order; an unusable icon/key/href is refused.
- [x] An ungated item is visible to anyone, including an unauthenticated caller.
- [x] A scoped item needs that scope; a system scope also satisfies it.
- [x] A role-gated item needs that role; with both gates set, both must hold.
- [x] A gated item is hidden from an anonymous caller.
- [x] A host icon colliding with a built-in navigates to the host's link, not the built-in action.
- [x] Full suite passes with no new warnings.

## Done condition

A host can add localized, optionally-gated menu items without touching the toolkit's components.

## Stated explicitly, in code and docs

**The gates control rendering, not access.** They hide a link the caller cannot use; the page behind it still
gates itself. A hidden menu item is a courtesy, never a protection — said in the XML docs, the guide, and the
test class remarks, because this is exactly the assumption that has cost this codebase before.

## Not included — #204 itself

Routing `TeamComponent`, `UsersView` and `AuditLogView` text through the provider is **not** in this branch.
Sizing it: roughly **74 candidate display strings** (24 / 3 / 47), and `IThargaTextProvider.GetAsync` is async
— `LoginDisplay` resolves four into fields in `OnInitializedAsync`, and doing that 47 times in `AuditLogView`
wants a bulk-resolve helper first. That is its own branch, and mixing a new capability with a 74-string sweep
would make poor release notes.
