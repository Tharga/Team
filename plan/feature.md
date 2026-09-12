# Feature: forward `GetTeamKeyByInviteKeyAsync` through the `ITeamService` decorators

Fixes [Tharga/Team#272](https://github.com/Tharga/Team/issues/272).

## Goal

Short invitation links (`?tic=<token>`) resolve again in a host that registers its team service through
`AddThargaTeamBlazor` / `AddThargaTeam`, and a default interface member added to a decorated contract can
no longer be silently swallowed by a decorator.

## Background

`ITeamService.GetTeamKeyByInviteKeyAsync` is a default interface member returning `null`, so a host with
its own store keeps compiling. `TeamServiceBase` implements it and `TeamServiceRepositoryBase` answers it
from the store — but the registered `ITeamService` is never the host's own service. `ThargaBlazorRegistration`
wraps it in `AuditingTeamServiceDecorator` and then `AuthorizationTeamServiceDecorator`, and neither
implements the member. The call therefore runs the interface's default body and returns `null`, whatever
the store would have said, and `TeamManagementService.ResolveCodeAsync` reads that as "unknown code".

Every other default member of `ITeamService` — `RestoreTeamAsync`, `PurgeTeamAsync`, `LeaveTeamAsync`,
`SetMemberSuspendedAsync`, `ExtendInvitationAsync` — is forwarded by both decorators. This one was missed.

Affected: 3.20.0, 3.20.1, 3.21.0 — every release since the short link form was introduced. Links minted
before 3.20 (`?TeamInviteCode=<base64 {TeamKey, Code}>`) carry their team key and never reach the lookup,
so they still resolve.

## Scope

- Forward `GetTeamKeyByInviteKeyAsync` in `AuthorizationTeamServiceDecorator`.
- Forward it in `AuditingTeamServiceDecorator`, as a silent pass-through with no audit entry.
- An architecture test asserting every decorator in the toolkit declares every default interface member of
  the contract it decorates.
- A regression test driving `TeamManagementService` over the decorated chain rather than a stub.

### Out of scope

- Changing the short-link format, the token, or the expiry model.
- Changing how `TeamServiceRepositoryBase` answers the lookup.
- Removing the default body from `ITeamService`. It is load-bearing — a host with its own store must keep
  compiling — and the architecture test is what makes it safe.

## Decisions

- **No scope check on the forward.** The invite code is the check, as for the other invitation reads. A
  first-level call must be *checked*, not necessarily checked by a scope, and the invitee holds no scopes
  because they are not yet a member.
- **No audit entry on the lookup.** It joins the pass-through reads at the top of
  `AuditingTeamServiceDecorator`, consistent with #262 making reads opt-in. Auditing it would let anyone
  holding a link write rows from an unauthenticated, pre-membership call.

## Acceptance criteria

- [x] `AuthorizationTeamServiceDecorator.GetTeamKeyByInviteKeyAsync` returns what the inner service returns.
- [x] `AuditingTeamServiceDecorator.GetTeamKeyByInviteKeyAsync` returns what the inner service returns and
      writes no audit entry.
- [x] A test resolving a short code through the full decorated chain returns the invitation, and fails
      against the unfixed decorators.
- [x] An architecture test fails if any toolkit decorator omits a default interface member of its contract.
- [x] Full suite green.

## Done condition

All acceptance criteria met, the full suite passes, the user has confirmed the fix, and #272 is closed with
the shipped version named.
