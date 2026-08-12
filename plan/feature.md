# Feature: AuditLogView reads the caller-name source it is allowed to read (#222)

Closes [Tharga/Team#222](https://github.com/Tharga/Team/issues/222).

## Goal

`<AuditLogView TeamKey="…" />` must render for the callers entitled to read that team's audit log.

Today it throws out of `OnInitializedAsync` for any of them:

```
GetAsync requires the 'users:manage' system scope.
  at AuthorizationUserServiceDecorator.RequireUsersManageAsync
  at AuditLogView.BuildCallerNameCacheAsync
  at AuditLogView.OnInitializedAsync
```

## The defect

`BuildCallerNameCacheAsync` (`AuditLogView.razor.cs:206`) guards on the service being **registered**, not on
the caller being **permitted**:

```csharp
var userService = ServiceProvider.GetService<IUserService>();
if (userService != null)
{
    await foreach (var user in userService.GetAsync())   // requires users:manage
```

`users:manage` is a **system** scope and no team access level grants one — a team scope is claim type
`Scope`, a system scope is `SystemScope`. So the callers entitled to read their own team's log are exactly
the ones who cannot render it.

**Customer-visible at Eplicta FortDocs today.** Their nav shows the Audit item to any holder of team
`audit:read` — Owner and Administrator both — while `users:manage` is granted to global Eplicta roles only,
deliberately. Every customer organisation Owner gets an error page. Widening their grant is **not** the fix:
it would hand a tenant the whole user directory to work around a display-name cache.

## This is #139 again, in the component that was missed

#139 was the same defect in `TeamComponent`. Fixing it built exactly the machinery this needs, and
`AuditLogView` was never moved onto it:

- `IUserService.GetTeamMemberUsersAsync()` — the co-member projection, gated on authentication alone
  (`AuthorizationUserServiceDecorator.cs:61`).
- `UserDirectoryGate.Resolve(hasUsersManageScope)`, whose summary already describes this exact case.

`TeamComponent.razor:353` reads through the gate. `AuditLogView` still calls `GetAsync()`.

## Scope

1. **Resolve the source through `UserDirectoryGate`**, as `TeamComponent` does.
2. **Make the cache build non-fatal.** The issue's own wording is that the page *fails rather than
   degrading*, so degrading is part of the ask. The cache is cosmetic — `GetCallerDisplayName` already falls
   back to the raw caller identity — so nothing it does should be able to take a page down. This also covers
   a host's custom `IUserService` throwing for a reason the gate cannot predict.

Co-members is arguably the **more correct** set here regardless of permissions: the cache exists only to turn
a caller identity into a display name, and a team-scoped audit log shows callers from that team.

## Out of scope

- The other surfaces reading `IUserService`. If the sweep finds more, they are recorded, not fixed here —
  this is the second instance of one shape and the third would want the analyzer, not another patch.
- `AuditLogView`'s export column headers, which stay literal by deliberate decision.

## Acceptance criteria

- [ ] A caller holding team `audit:read` but **not** `users:manage` renders the audit log.
- [ ] A caller holding `users:manage` still gets the full directory, so display names do not regress for
      staff.
- [ ] Caller names still resolve for co-members; unknown identities still fall back to the raw identity.
- [ ] A throwing `IUserService` degrades the names, never the page.
- [ ] The gate decision is unit-tested without rendering, matching `UserDirectoryGate`'s existing tests.
- [ ] Build clean, full test suite green.

## Done condition

A FortDocs customer Owner can open the audit page, and the user confirms.
