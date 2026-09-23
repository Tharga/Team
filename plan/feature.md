# Feature: time-bound team access requests

Spec: `$DOC_ROOT/Tharga/plans/Toolkit/Platform/planned/13-team-access-requests.md`. **Tier 1** (`mission.md`):
this grants access. Branched from `origin/master` at `da70d9d` (#278 and #280 merged).

## Goal

A holder of a consent role (e.g. a Developer) requests access to a team at a level, for a time or with no end.
The team's managers approve or deny it. Approval sets the team's consent for that window; when it runs out the
team's previous consent comes back. Managers learn of requests in the team view and through a new notification
icon in the top bar; every step is audited.

## Decisions (user, 2026-09-14)

| Question | Decision |
|---|---|
| What approval grants | The team's **role consent** — every configured consent role at the requested level, until expiry |
| On expiry | **Restore the consent in place before approval**; none if there was none |
| Who may request | **Anyone holding a configured consent role** (`ConsentOptions.Roles`) — no new scope |
| Requestable levels | **Viewer, User, Administrator** — not Owner, not Custom |
| Who approves / denies | Team managers (`team:manage`) |
| How managers learn | In the team view + audit (routable to Slack) **+ a new notification icon in the top menu** |

## Design

### Model (contracts in `Tharga.Team`)

- `TeamAccessRequest` record: `Id`, `RequesterKey`, `RequesterName`, `AccessLevel`, `Duration` (null = no end),
  `Message`, `RequestedAt`, `Status` (`Pending` · `Approved` · `Denied` · `Cancelled`), `DecidedBy`, `DecidedAt`,
  `GrantedUntil`.
- `ITeam` gains default members: `AccessRequests`, and a `TemporaryConsent` (`ExpiresAt`, `PreviousRoles`,
  `PreviousAccessLevel`) — null when consent is standing.
- **Duration, not an end date, is requested**: the window starts at approval, so time spent pending does not eat it.

### One rule for effective consent

`EffectiveConsent.Resolve(team, now)` → the roles and level in force: the stored consent while unexpired, the
previous consent after expiry. **Every consent read goes through it** — `TeamGrantResolver` (claims),
`TeamContextResolver` (API keys naming a team), `GetConsentedTeamsAsync`, `TeamVisibility` (badges),
`TeamsListView`, the MCP team resource. An architecture test fails on a direct read of `ConsentAccessLevel` /
`ConsentedRoles` elsewhere. Expiry therefore needs **no background job**: it takes effect at the next claims
issuance or revalidation.

### Operations (architecture v4: operations, not CRUD; one enforcement point)

On `ITeamService` (default interface members that throw, the `RestoreTeamAsync` pattern), enforced in
`AuthorizationTeamServiceDecorator`, audited in `AuditingTeamServiceDecorator`:

| Operation | Authorized by |
|---|---|
| `RequestTeamAccessAsync(teamKey, level, duration, message)` | Caller holds a consent role (app role, not a team-derived one); **not a member** of the team (membership outranks consent, so a request would grant nothing); level ∈ Viewer/User/Administrator; team live. One pending request per requester per team — a new one replaces theirs |
| `CancelTeamAccessRequestAsync(teamKey, requestId)` | The requester only |
| `ApproveTeamAccessRequestAsync(teamKey, requestId)` | `team:manage` **held as a member**; not the requester |
| `DenyTeamAccessRequestAsync(teamKey, requestId)` | `team:manage` held as a member |

**Approve is one atomic write** (rule 5): set consent (configured roles, requested level, `now + duration`),
capture the previous consent — unless a temporary consent is already active, in which case the original
previous is kept — and mark the request approved. The request lives on the team document, so this is a single
document update.

**Security tightening, in scope:** `SetTeamConsentAsync` also requires `team:manage` **as a member**. Today a caller
consented in at Administrator holds `team:manage` and can change that team's consent — with time bounds that means
removing their own expiry or approving their own access. A direct consent change by a manager makes the consent
standing and clears any expiry.

### Facades

- `ITeamManagementService` (gated): `GetAccessRequestsAsync(teamKey)`, `ApproveTeamAccessRequestAsync`,
  `DenyTeamAccessRequestAsync` — `[RequireScope(team:manage)]`.
- New `ITeamAccessRequestService` (filtered, registered by the library): `RequestTeamAccessAsync`,
  `CancelTeamAccessRequestAsync`, `GetMyAccessRequestsAsync`, and `GetRequestsAwaitingMeAsync` — pending requests on
  teams where the caller is a member holding `team:manage`, recomputed per team.

### Persistence

`TeamServiceBase` gains virtual throwing methods (the `SetTeamMemberSuspendedAsync` pattern) so custom hosts keep
compiling; `Tharga.Team.MongoDB` implements them on `TeamEntityBase` (`AccessRequests`, `TemporaryConsent`, enums
stored by name). Requests are kept, capped at the 50 most recent per team (`TeamAccessRequestRules.HistoryLimit`); the audit log is the record.

### UI

- **Requester:** on a team the caller is not a member of (`TeamComponent`), **Request access** opens a dialog —
  level, duration (1 hour · 8 hours · 1 day · 1 week · 30 days · no end), optional message. Their pending request
  shows with **Cancel**.
- **Manager:** the team card lists pending requests with **Approve** / **Deny**. Approving states plainly who gains
  access: *everyone* holding the consent roles, at that level, until that time.
- **Consent display** shows the expiry ("until …") and what it reverts to.
- **`<TeamNotificationMenu />`** — optional top-bar bell. Badge = requests awaiting the caller's decision; the menu
  lists them and links to the team. Also lists the caller's own pending requests. Sample places it in the header.

### Audit

`access-request.requested` · `.cancelled` · `.approved` (level, granted until, previous consent) · `.denied`, via the
decorator, so hosts can route them with existing notification routes.

## Not in scope

- A background job, and an audit event at the moment of expiry (the approval entry records the expiry time).
- Email to managers.
- Changing level or duration at approval — approve as requested, or deny.
- System API keys requesting access.

## Acceptance criteria

Confirmed in the sample by the user 2026-09-23, on the pushed branch, after the withdraw defect was fixed.

- [x] A consent-role holder who is not a member can request Viewer, User or Administrator, for a duration or no end.
- [x] Owner, Custom, non-consent-role callers and members are refused, at the service, not only in the UI.
      `AccessRequestAuthorizationTests` (11).
- [x] A member holding `team:manage` can approve or deny; the requester cannot approve their own; a consent-derived manager cannot approve or change consent.
      `DirectTeamScopeTests` (10), `AccessRequestAuthorizationTests`.
- [x] Approval sets consent for the window atomically and records the previous consent.
      One conditional positional update; `TeamRepositoryAccessRequestTests`, `TeamAccessApprovalTests` (6).
- [x] After expiry every consent read — claims, API-key team context, consented-team lookup, badges, MCP — sees the previous consent; guarded by an architecture test.
      `TeamConsent.Resolve` + `ConsentExpiryReadsTests` (7); the architecture test fails any direct read.
- [x] A direct consent change by a manager clears the expiry. `TeamRepositoryConsentTests` (2).
- [x] Every operation is audited with its metadata. `AccessRequestAuditTests` (6).
- [x] Requester and manager UI as described; the notification menu shows the count and items.
      Plus `<TeamAccessRequestButton />`, added 2026-09-23 so a host can place the ask anywhere.
- [x] Custom `TeamServiceBase` hosts compile; unimplemented operations throw a clear `NotSupportedException`.
- [x] Enums persisted by name (asserted). `TeamEntityAccessRequestSerializationTests` (2), plus the enum sweep.
- [x] Full suite green — 2,992.

**One defect found by the manual verification, which is why it was deferred to it.** Withdrawing threw
`MongoCommandException: The positional operator did not find the match needed from the query` — every
positional array write went through `Tharga.MongoDB.UpdateOneAsync` in its default mode, which finds the
document and then updates it by `_id` alone, so the `$elemMatch` never reached the update. Approve and deny
carried it identically. Fixed by passing `OneOption<TTeamEntity>.FirstOrDefault`, which also restores the
atomicity the code's own remarks claimed; guarded by `EveryPositionalWrite_IsAtomic`, verified to fail
without the fix while every rendered-shape assertion stayed green. **The shape tests could not have caught
it** — they assert what is sent, and the mode is invisible to them.

## Done condition

Criteria met, docs on both surfaces, spec 13 archived, `plan/` removed in the close-out commit, PR open.
