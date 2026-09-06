# User management & directory verification

The platform stores a user record for everyone who signs in. This article covers the administration
features built on top of that store: per-user activity tracking (last seen), verifying users against an
external directory (Microsoft Entra ID), listing directory-only users, and deleting users — from the
application and, on explicit opt-in, from the directory.

## Last seen

The user service stamps `LastSeen` on the user record whenever the user makes an authenticated request,
throttled to at most one write per interval (default **15 minutes**, per process). The stamp happens in
the user-resolve path, so it works whether or not a team is selected.

Tracking is **opt-in by entity shape**: declare the property on your user entity and the toolkit starts
writing it — leave it off and nothing is written.

```csharp
public record UserEntity : EntityBase, IUser
{
    public required string Key { get; init; }
    public required string Identity { get; init; }
    public required string EMail { get; init; }
    public string Name { get; init; }

    public DateTime? LastSeen { get; init; }     // opt-in: last authenticated activity
    public string DirectoryId { get; init; }     // opt-in: Entra object id (oid)
}
```

To change the stamp interval, override the virtual on your user service:

```csharp
public class UserService : UserServiceRepositoryBase<UserEntity>
{
    protected override TimeSpan? LastSeenStampInterval => TimeSpan.FromMinutes(5);
    // TimeSpan.Zero stamps every resolve; null disables stamping entirely.
}
```

The users admin list (`<UsersView />` → Users tab) shows a **Last seen** column. This is distinct from
the per-team-member `LastSeen`, which tracks when a member last selected that team. A user who has never
made an authenticated request reads as **Never** rather than blank — "we have no value" and "this has
never happened" are different answers on a list whose purpose is finding dormant accounts.

The same list shows a **Teams** count per user, with the memberships behind it. How much it counts depends
on the caller's own visibility: without the `teams:read` system scope it counts only teams the caller
belongs to, so a user whose teams the caller shares none of reads as **0**. Grant `teams:read` — see
[cross-team visibility](implementation-guide.md#cross-team-visibility-for-oversight-roles) — and the count
covers every team, matching the Teams tab of the same component.

## Directory linking (`DirectoryId`)

Directory operations resolve the user by the Entra object id (`oid`). The toolkit captures it two ways:

- **New users** — populate it in `CreateUserEntityAsync` from the sign-in claims:

  ```csharp
  protected override Task<UserEntity> CreateUserEntityAsync(ClaimsPrincipal principal, string identity)
      => Task.FromResult(new UserEntity
      {
          Key = ...,
          Identity = identity,
          EMail = principal.GetEmail(),
          Name = principal.GetDisplayName(),
          DirectoryId = principal.GetDirectoryId()   // the oid claim, either raw or .NET-mapped
      });
  ```

- **Existing users** — backfilled automatically from the `oid` claim on their next authenticated visit,
  and by directory verification: when a user without a stored id is matched by email, the found object
  id is persisted (relink).

## Registering Microsoft Entra ID as the directory

Install **Tharga.Team.Entra** and register it; configuration is read from the same `AzureAd` section
the platform sign-in uses:

```csharp
builder.Services.AddThargaEntraUserDirectory(builder.Configuration);
```

App-only Graph authentication needs a client secret (or any `Azure.Core.TokenCredential` — certificate,
managed identity):

```csharp
// dotnet user-secrets set "AzureAd:ClientSecret" "<secret>"
builder.Services.AddThargaEntraUserDirectory(builder.Configuration, o =>
{
    // o.Credential = new ManagedIdentityCredential();   // instead of a secret
});
```

Grant the app registration **application** permissions in Entra, with admin consent:

| Feature | Graph permission |
|---|---|
| Verify users, list directory-only users | `User.Read.All` |
| Delete users from Entra | `User.ReadWrite.All` |

When no directory service is registered, all directory features (verify actions, the Directory column,
the directory-only tab, the delete-from-directory opt-in) are hidden — the rest of user administration
still works.

### A half-configured directory counts as no directory

Registering `AddThargaEntraUserDirectory` without complete credentials — no `TenantId`, `ClientId` or
`ClientSecret`, and no explicit `Credential` — leaves a directory that cannot answer. **Those same
features stay hidden, exactly as if nothing were registered.** Offering a Verify button that throws on
click is worse than not offering it.

`IUserDirectoryService.IsConfigured` is what reports this. It defaults to `true`, so a custom directory
implementation needs no change; override it if yours can be registered without being usable.

```csharp
// Both are equivalent from the UI's point of view.
var unavailable = provider.GetService<IUserDirectoryService>() is not { IsConfigured: true };
```

> [!NOTE]
> A common cause is Azure AD **B2C**, where the app registration has no `TenantId` key at all — the
> tenant is embedded in `Authority`. Binding the `AzureAd` section then leaves `TenantId` null and the
> directory silently unusable. Set it explicitly via the `configure` callback.

Calling the service directly still throws `InvalidOperationException` naming the three settings, so a
host that bypasses the UI gets a diagnosis rather than a silent failure.

**A half-set configuration warns; an empty one does not.** Only one of the two is a mistake:

| Configuration | Directory features | Log |
|---|---|---|
| No credential field set at all | hidden | silent — reads as a deliberate opt-out |
| Some set, some missing | hidden | **Warning** naming exactly which values are missing |
| A `Credential` supplied, or all three set | available | silent |

Registering the directory in every environment while supplying secrets in only some is a normal shape,
so that stays quiet. Half-filling a credential is not deliberate, and the symptom gives no clue where to
look — so it warns once at startup, naming the missing values.

## The `users:manage` scope

All user administration — **including viewing the users and teams admin lists** — requires the
**`users:manage` system scope** (registered automatically, like `teams:delete`). Grant it by mapping
an app role:

```csharp
o.ConfigureSystemRoles = roles =>
{
    roles.Map("Developer", SystemUserScopes.Manage);
};
```

Authorization is enforced in the service layer by decorators over `IUserManagementService` **and
`IUserService` itself**, so the same rules protect the Blazor circuit and any consumer REST endpoint:

- **Self-service passes for any authenticated caller** — resolving the current user, the
  invitation-accept name seeding, and setting one's *own* display name.
- **Co-members pass for any authenticated caller** — `IUserService.GetTeamMemberUsersAsync()` returns the
  users who share at least one team with the caller, plus the caller. It takes no argument, so the
  visibility set is derived entirely from the caller's own memberships and there is nothing to widen.
- **Everything cross-user requires `users:manage`** — enumerating *all* users, reading a user by key,
  setting another user's name, activity/directory writes, and deletion.

Team access level never grants `users:manage` — it is a *system* scope, mapped from app roles, while
access levels grant only team scopes. The two arrive as different claim types
(`TeamClaimTypes.SystemScope` and `TeamClaimTypes.Scope`), so a team-level grant of a scope name cannot
satisfy a system-wide check even where the same name is registered at both levels.
A team owner is therefore an ordinary caller here, and the
co-member projection is what lets `<TeamComponent />` show member emails, names and avatars without it.
Without that projection a member row falls back to "Unknown" with no email, because accepting an
invitation clears the per-team name override and promotes it to `IUser.Name`.

The `<UsersView />` tabs check the scope up front and render a notice instead of the lists when the
caller lacks it. Still protect the *page* that embeds the component with `[Authorize]` — and note that
Blazor only enforces page-level `[Authorize]` when your router uses `AuthorizeRouteView` (an attribute
on a non-page component does nothing).

## Which scope gates what

Every action on `<UsersView />` is gated by a **system** scope, and deleting a team needs three of them
doing three different jobs. The complete picture:

| Action | Scope(s) | Also needs |
|---|---|---|
| See either tab at all | `users:manage` | — |
| See teams you are not a member of | `users:manage` + `teams:read` | — |
| Delete a team | `users:manage` + `teams:delete` | `teams:read` in practice — see below |
| Delete a user | `users:manage` | never your own row — see below |
| Choose who owns a team | `users:manage` + `teams:set-owner` | the new owner must already be a member |
| Verify a user against the directory | `users:manage` | a **configured** `IUserDirectoryService` |
| The directory-only tab | `users:manage` | a **configured** `IUserDirectoryService` |
| Per-row audit history | `audit:read` | `ShowAuditLogButton="true"` |

**You cannot delete your own account, whatever you hold.** The Delete action is disabled on the
signed-in caller's own row and labelled *"Delete (this is you)"*. Deleting yourself drops your user
record and, through `RemoveUserFromAllTeamsAsync`, your membership of every team — while your session
continues holding claims that no longer correspond to anything. An administrator who genuinely should go
needs another administrator to remove them, which also guarantees somebody is left holding
`users:manage`. It is the same class of guard as refusing to demote a sitting owner, and it is the most
likely way to strand a team with no owner at all: the sole owner of a team is very often the same person
administering users.

**`teams:read` is not required to delete a team, but without it there is usually nothing to delete.**
The Delete action itself is gated on `teams:delete` alone. `teams:read` decides *which teams the grid
lists* — without it the caller sees only teams they belong to, and the point of a cross-team delete is
acting on teams you are not a member of. Grant all three for an operator role.

**All of these must be system grants.** They arrive as `TeamClaimTypes.SystemScope`, while an access
level grants `TeamClaimTypes.Scope`. Registering a scope of the same name at an access level does not
satisfy these checks — see [The `users:manage` scope](#the-usersmanage-scope).

**`teams:read` can arrive two ways**, which is why it is absent from the sample's role mapping:

```csharp
o.Blazor.Consent.GrantTeamsRead = true;                   // adds teams:read on top of the mapping
o.ConfigureSystemRoles = roles =>
{
    roles.Map("Developer", SystemUserScopes.Manage, SystemTeamScopes.Delete);
};
```

Mapping `SystemTeamScopes.Read` directly in `ConfigureSystemRoles` is equivalent; `GrantTeamsRead` is
the consent-flavoured shortcut.

### `ConfigureSystemScopes` does not withhold these from API keys

`ConfigureSystemScopes` is what makes a scope grantable to a **system API key**, and it is easy to read
that as *the* gate. It is not, for these two: `users:manage` and `teams:delete` are **auto-registered**
by the framework, because the admin surfaces need them grantable. Omitting them from
`ConfigureSystemScopes` does **not** withhold them from keys.

If you have written a comment or a test asserting that a system key cannot receive `users:manage` or
`teams:delete` because you left them out of `ConfigureSystemScopes`, that assertion does not hold. Gate
those keys by not granting the scope to the key, rather than by relying on registry contents.

### A known asymmetry: deleting users

Deleting a **team** requires a dedicated `teams:delete` scope on top of `users:manage`. Deleting a
**user** requires `users:manage` alone — there is no `users:delete`.

The asymmetry runs opposite to the blast radius. A user delete removes the user from *every* team,
deletes the record, and can optionally delete the account organization-wide from the directory. So a
role granted `users:manage` purely to *view* the admin lists can also delete every user in the system.
If that is wider than you intend, do not map `users:manage` to a broad support role — map it only to the
role you would trust with user deletion.

## Choosing who owns a team

The platform refuses to grant or revoke `Owner` through `SetMemberRoleAsync`, and `TransferOwnershipAsync`
requires the caller to *be* the current owner. Deleting a user, however, removes them from every team —
owner included. So deleting the wrong person could produce a team nobody could ever own again, and the
only remedy was `teams:delete`.

**Two halves close that: a warning before, and a repair after.**

### Before — the delete dialog names the teams

The confirmation lists the teams the user owns and states that ownership cannot be transferred once they
are gone. Transfer ownership first if those teams are still in use. Available programmatically:

```csharp
var owned = await userManagementService.GetOwnedTeamsAsync(userKey);
```

Deletion is **not refused** for a sole owner. There are legitimate cases — winding up a one-person team —
and the state is now repairable, so the warning is the right weight.

### Choosing who owns a team — `teams:set-owner`

The **`teams:set-owner`** system scope authorizes making any existing member the **sole owner** of a team,
demoting every other owner to `Administrator`. It appears on the Teams tab of `<UsersView />` under a label
that follows the team's current state — **Assign owner**, **Change owner**, or **Reduce to a single owner**.
One operation, three situations:

```csharp
var result = await teamService.SetOwnerAsync<TMember>(teamKey, newOwnerUserKey);
```

| Starting state | What happens | Why you would do it |
|---|---|---|
| No owner | The candidate becomes owner | The owner was deleted — `RemoveUserFromAllTeamsAsync` takes them with everyone else |
| One owner, someone else | The candidate becomes owner; the sitting owner becomes `Administrator` | A handover the owner cannot perform themselves — they have left, or the account is gone |
| Several owners | The candidate keeps or gains `Owner`; **all** the others become `Administrator` | A team synced from a system whose model permits several owners |
| The candidate already owns it alone | Nothing. No write, no audit entry | A sync pass over a team that is already correct |

**A team ends up with exactly one owner.** That is the invariant the operation exists to restore, and it is
structural rather than checked: `SetMemberRoleAsync` refuses to grant or revoke `Owner` in either direction,
so no ordinary path can add a second or remove the last, and this operation always promotes before demoting
— the team is never momentarily ownerless, even if a write fails part-way.

One condition is enforced in the service, not just the UI:

| Condition | Why |
|---|---|
| The new owner is **already a member** | A caller holding this scope is not a member of the team, so without it they could install anyone — including themselves |

Note what is *not* a condition: the current owner count. Refusing on a team that already has an owner would
rule out the two cases this exists for.

It is a **system** scope with no in-team fallback, deliberately unlike `teams:delete`, for two reasons. On an
ownerless team no in-team caller can exist. On a team that has an owner, the in-team caller who should move
ownership *is* the owner, and `TransferOwnershipAsync` is already their path — an in-team fallback here would
let an Administrator depose the owner, which `SetMemberRoleAsync` exists to refuse.

**Both the operation and its refusals are audited**, and the entry names every owner demoted, not just the
one promoted. A call that changed nothing writes no entry at all, so a sync running on a schedule does not
bury the real events.

`SetOwnerResult` tells the two empty cases apart: `Changed` is false only when nothing was written, while
`DemotedOwnerKeys` is empty both for a no-op *and* for a genuine repair of an ownerless team.

Invited members are not offered as candidates. They have not accepted, so making one owner would hand the
team to somebody who may never arrive.

> **Renamed in 3.14, and the capability changed with it.** Through 3.9.0–3.13.0 this was
> **`teams:assign-owner`**, and it refused outright on a team that already had an owner — it repaired
> ownerless teams and nothing else. `teams:set-owner` can depose a sitting owner, which is the point, so the
> grant was renamed rather than widened in place: a host that granted the old string does not silently
> acquire the larger capability. **The old name now authorizes nothing.** Remap it — a startup check fails
> with both names rather than leaving holders refused at the point of use with nothing explaining why.
>
> `AssignOwnerAsync` is removed. `SetOwnerAsync` covers everything it did.

## Where names are edited

A user has one **root name** (`IUser.Name`), shared everywhere, and optionally a **per-team override**
(`ITeamMember.Name`) that applies only inside one team. Each is edited on exactly one surface:

| Surface | Edits | Who |
|---|---|---|
| `<UserProfileView />` (profile page) | root name | the user, for themselves |
| `<UsersView />` (users page) | root name | a holder of `users:manage` |
| `<TeamComponent />` (team page) | per-team override only | a holder of `member:manage` **on the selected team** |

### Writing the name back to the directory

By default a rename stays in this application. Set **`o.Blazor.WriteNameToDirectory = true`** and an
**administrative** rename also writes `displayName` to the external directory.

```csharp
var result = await userManagementService.SetUserNameAsync(userKey, "Real Name");
// result.DirectoryUpdated, result.DirectoryError
```

| Situation | Result |
|---|---|
| Option off | Local write only. `DirectoryUpdated: false`, no error |
| User not linked to a directory account | Local write only. **Not an error** — there is nothing to write to |
| Directory write succeeded | `DirectoryUpdated: true` |
| Directory write failed | `DirectoryError` set. **The local write is not rolled back** |

**Self-service renaming is never pushed**, whatever the option says: a user editing their own display
name here should not silently rewrite the organization's directory.

The two writes fail independently on purpose. Coupling them would let a directory outage block renaming a
user in this application, so the UI reports a directory failure as a warning rather than a failed rename
— because the rename did happen.

Which side owns display names is a per-host decision, which is why this is off by default. A host
federating from a corporate directory wants the directory authoritative; an application that collects no
attributes at sign-up is the opposite case, holding the real name while the directory holds a
placeholder.

The team surface never writes the root name. Submitting a member's displayed name unchanged stores *no*
override, so the row keeps tracking that user's later renames rather than pinning a copy of the name.

Accepting an invitation clears the per-team override and promotes the admin-entered name to `IUser.Name`,
so an accepted member's name and email come from the user record — see the co-member note above.

## Verifying users

- **Per user** — the Verify action on a row checks the directory and shows a badge:
  **Found** (exists, enabled), **Disabled** (exists, account disabled), **Not found** (the stored
  directory id no longer exists — the user was deleted in Entra), **Not linked** (no directory id and
  no email match).
- **Verify all** — sweeps every user, updating badges as results stream in.

Verification by a stored directory id deliberately does **not** fall back to email: a broken link is a
finding, not a lookup miss.

## Directory-only users

The **Users in directory only** tab on `<UsersView />` lists users that exist in the directory but have no local
user record — matched by directory id with an email fallback, so pre-existing local users without a
stored `oid` are not falsely reported. Nothing is fetched until you press **Load** (a tenant's
directory can be large); results stream in page by page.

## Suspending instead of deleting

Deletion is final: it removes the user from every team and drops the record. For the ordinary
cases — someone on leave, an account suspected of compromise, a contractor between engagements — there
are three reversible alternatives, each bounded differently.

| To stop | Use | Scope | Reach |
|---|---|---|---|
| A person signing in at all | `IUserManagementService.SetUserDisabledAsync` | `users:manage` | The whole application |
| A person working in **one team** | `ITeamManagementService.SetMemberSuspendedAsync` | `member:manage` on that team | That team only |
| An API key | `IApiKeyManagementService.SetKeyDisabledAsync` | `apikey:manage` | That key |

All three record **when** and **by whom**, are audited under distinct actions in each direction
(`disable`/`enable`, `suspend`/`restore`), and are reversible without losing anything.

### Disabling a user

A disabled user is refused at sign-in **and evicted from a live session** within
`ClaimRevalidation.Interval` — a signed-in user holds a Blazor circuit with claims already issued, so
refusing future sign-ins alone would leave them working indefinitely.

That eviction is deliberately **fail-open**: if the user store cannot be reached, nobody is signed out.
Treating a store failure as "disabled" would sign out every user at once, turning a database blip into an
outage. The check runs again next interval.

Two things it does **not** do:

- **It does not cascade to that user's API keys.** A key is not a session — it is an independent
  credential with its own lifecycle, and disabling a person should not silently retire integrations they
  happen to have minted. Where both must stop, that is two deliberate acts, which is also what keeps each
  reversible on its own.
- **It is not `DirectoryUserStatus.Disabled`.** That means disabled *in the directory* — blocked from the
  organization rather than from this application. The two are independent, can disagree, and appear as
  separate badges on the row.

**Nobody can disable themselves.** An administrator who locks themselves out needs a second
administrator to undo it, and refusing the self-case guarantees somebody is left holding `users:manage`.
The rule is enforced in the service, not only hidden in the UI, because `ActionItems` lets a host inject
a row action that dispatches straight to the handler.

To persist it, declare `DisabledAt`/`DisabledBy` on your user entity and implement
`IUserService.SetUserDisabledAsync`. Both are opt-in by shape, like `Icon` and `DirectoryId`; the
persistence hook **throws** rather than doing nothing when unimplemented, so a containment that was never
applied cannot be reported as success.

### Suspending a team member

Bounded to one team. The member keeps their membership, access level, roles and history, **still sees the
team in the selector**, and holds no scopes in it.

Seeing the team is the point: a membership that silently vanishes is indistinguishable from removal, and
the member cannot tell that it is reversible or who to ask. Place `<SuspendedTeamNotice />` in your
layout to explain it — the toolkit owns no shell, so it cannot take the page over for you. Security does
not depend on that component: no scopes are granted either way, so every `[RequireScope]` refuses.

> **It is not a `MembershipState`.** That was the obvious design and it does the opposite of the
> intent. Stores list a user's teams by filtering `State == MembershipState.Member`, so a suspended
> state would drop the team out of the selector — and that filter lives in your code, not the toolkit's.
> Suspension is `SuspendedAt`/`SuspendedBy` on the member instead, leaving `State` alone, so every
> existing query keeps working untouched.

Two refusals, both in the service:

- **The Owner cannot be suspended**, for the same reason they cannot leave or be demoted — it would leave
  a team whose ownership nobody can transfer, since transfer requires the caller to be the owner.
- **Nobody can suspend themselves.**

**A suspended member can still leave.** `ITeamDirectoryService.LeaveTeamAsync` is the one team operation
carrying no scope, precisely so that holding none does not trap somebody in a team — and it strands
nothing, because the Owner cannot be suspended in the first place. Restoring them afterwards means
re-inviting rather than un-suspending.

And one that came from the field: **an invited member cannot be suspended.** An unaccepted invitation
grants no access, so there is nothing to take away; withdraw the invitation instead.

#### Resolving a member by key: which lookup

This is worth knowing before you write anything that looks up a member, because getting it wrong is what
produced that invited-member bug.

| Call | Answers | Sees invited/rejected |
|---|---|---|
| `GetTeamMemberAsync(teamKey, userKey)` | "Can this user act as a member?" | **Host-dependent** |
| `GetMembersAsync(teamKey)` | "Who is on the roster?" | Always |

`GetTeamMemberAsync` resolves through your store's *"teams this user belongs to"* query. The MongoDB
store filters that on `State == MembershipState.Member`, so a pending invitee comes back null —
indistinguishable from a stranger. A store written differently may return them.

So treat a non-null result as *"has some membership"* and null as *"cannot act as a member"*, and never
as a reliable answer to **which** state someone is in. Anything that must tell the states apart — a
refusal message, a roster count, an admin grid — reads `GetMembersAsync`.

## Deleting users

Requires the **`users:manage`** system scope and nothing further — there is no separate delete scope.
See [A known asymmetry: deleting users](#a-known-asymmetry-deleting-users) before granting it broadly.

The Delete action (or `IUserManagementService.DeleteUserAsync`) always performs the **local** delete:

1. Removes the user from **every** team (any membership state).
2. Deletes the user record.
3. Writes audit entries.

Deleting from the directory is an **explicit opt-in** — a checkbox in the confirm dialog, off by
default — because it removes the account **organization-wide**, not just from your application. Entra
performs a soft delete: an administrator can restore the user for 30 days.

A directory failure never rolls back the local delete; it is reported on the result:

```csharp
var result = await userManagementService.DeleteUserAsync(userKey, deleteFromDirectory: true);
// result.RemovedTeamCount, result.DirectoryDeleted, result.DirectoryError
```

## Reading the admin grids

Both tabs of `<UsersView />` answer operator questions the lists could not previously answer.

**Users tab.** The signed-in user's own row is tinted with a left accent bar, so "which one is me" needs
no scanning. Expanding a row shows three identifiers, each with a copy button, because they answer
different correlation questions:

| Identifier | What it is | Use it for |
|---|---|---|
| **User key** | This application's own id for the user | Database documents, support tickets |
| **Identity** | The authentication subject from the identity provider | Matching a sign-in; stable across name and email changes |
| **Directory id** | The Entra `oid` / Graph object id | Looking the account up in Entra, or a Graph query |

The directory id distinguishes two kinds of absence rather than showing a blank, which would read as
"this user has no directory account":

- **Not stored** — the host's user entity does not declare `DirectoryId`, so none is ever persisted. See
  [Directory linking (`DirectoryId`)](#directory-linking-directoryid) to opt in.
- **Not resolved yet** — the entity does declare it, and this user has not been resolved since. It is
  captured automatically on their next resolve.

**Teams tab.**

| Column | What it tells you |
|---|---|
| Avatar | The team's uploaded icon, or its initials |
| Name | Plus an **Empty** badge when the team has no members at all |
| Owner | The member at `AccessLevel.Owner`, or a **No owner** badge — an ownerless team is a data defect, not a blank |
| Members | Accepted members, with a `+n invited` badge when invitations are still outstanding |
| Last used | The most recent `LastSeen` across the team's members, or **Never** |

**Last used is team selection, not sign-in.** `ITeamMember.LastSeen` records when a member last selected
that team, so the maximum across members reads as "when anyone last used this team". A team whose members
sign in daily but never switch to it still reads as stale — which is the intended signal.

The member count deliberately separates accepted members from outstanding invitations: a team showing a
flat "5" may be one member and four abandoned invitations, and that distinction is usually what a decision
to delete the team turns on. `TeamViewModel.MemberCount` keeps its original meaning (every row, accepted
or not) for existing consumers; `ActiveMemberCount` and `InvitedCount` are the split.

Expanding a team row shows the **team key** with a copy button, and each member name links across to that
user on the Users tab. The reverse works too — a team in a user's membership list opens that team.

### Audit history from the team page

`<TeamComponent ShowAuditLogButton="true" />` adds a per-member action opening that member's audit log
**scoped to that team** — both the caller and the team are pinned, so a team administrator sees what one
of their members did inside their own tenant and nothing else.

The action is hidden unless the caller holds `audit:read`, and the two ways of holding it differ:

- **System grant** (`o.ConfigureSystemRoles`) — reads any team's log, so the action appears on every team.
- **Team grant** (an access level) — issued for the *selected* team, so the action appears only there.

No extra configuration is needed for the boundary: the same `audit:read` rule already enforces it
server-side, and this only stops the UI offering what would be refused.

> The pin matches on `CallerIdentity`, which is a display string rather than a stable id (see
> [Reading the admin grids](#reading-the-admin-grids)). If your identity provider puts a display name in
> the name claim rather than the email, the dialog may come up empty even though entries exist.

The same capability exists per API key — `<ApiKeyView ShowAuditLogButton="true" />` and
`<SystemApiKeyView ShowAuditLogButton="true" />` — pinned to `CallerKeyId`, which *is* a stable id, so
that one is exact.

### Audit history per row

Opt in with `<UsersView ShowAuditLogButton="true" />` to add a per-row action on both tabs that opens the
audit log already scoped to that team or user, in a resizable dialog. It is off by default because the
audit log is only queryable once audit storage is configured — see [Audit](#audit).

A team row pins `TeamKey`. A user row pins `CallerIdentity`, because audit entries record the caller's
identity rather than the user record's key.

## Deleting teams

The **Teams** tab of `<UsersView />` offers a Delete action on each row, gated by the `teams:delete`
system scope. It deletes any team irrespective of the caller's membership or access level, and
irrespective of the `AllowTeamCreation` option that governs self-service deletion on `<TeamComponent />`.

**This is the only way to delete a team you do not own.** The in-team path is an Owner act from 3.21 —
`team:manage` no longer authorizes deletion — so if operators or team administrators in your product need
to delete teams, `teams:delete` is what to grant them.

**This is deliberately not a consent decision.** Consent governs what a team exposes *inbound* — which
global roles may reach into it and at what access level. Whether an operator may destroy the team is a
different question, so no consent option grants `teams:delete`, and a team that has consented to nothing
is still deletable by a holder of the scope. Contrast `teams:read`, which `Consent.GrantTeamsRead` does
grant, because enumerating teams genuinely is discovery of what has opted in.

Nothing grants `teams:delete` out of the box — map it to the roles that should have it:

```csharp
o.ConfigureSystemRoles = roles =>
{
    roles.Map("Developer", SystemTeamScopes.Delete);
    roles.Map("Administrator", SystemTeamScopes.Delete);
};
```

The scope must be held as a **system** grant. Registering `teams:delete` at an access level would produce
a `TeamClaimTypes.Scope` claim, which never satisfies the system-wide check — the same claim-type split
described under [The `users:manage` scope](#the-usersmanage-scope).

Two capabilities are separate on this surface: viewing the Teams tab requires `users:manage`, deleting a
team requires `teams:delete`. A caller granted only the latter sees no tab; a caller granted only the
former sees the list with no Delete action.

**A third scope decides what is on the list.** Without `teams:read` the grid shows only teams the caller
belongs to, so a `teams:delete` holder sees the Delete action but not the cross-team rows it exists for.
Grant all three — `users:manage`, `teams:read`, `teams:delete` — for an operator who should be able to
delete any team. See [Which scope gates what](#which-scope-gates-what) for the full matrix.

Deleting is confirmed with the team name and its member count, and **cannot be undone** — there is no
soft delete or restore today.

## Audit

User administration is audited under feature `user`: `verify` (with the outcome), `verify-all` (one
summary entry with the processed count), and `delete` (team count, whether the directory user was
deleted, any directory error). The all-team removal inside a delete is additionally audited under
feature `team` as `remove-member-all`. The directory-only listing is a read and is not audited.

## Custom directory providers

Entra is an implementation of the `IUserDirectoryService` abstraction. To back verification against a
different directory, implement the interface (verify a user, delete by directory id, enumerate users)
and register it:

```csharp
builder.AddThargaTeam(o =>
{
    o.AddUserDirectoryService<MyLdapDirectoryService>();
});
```
