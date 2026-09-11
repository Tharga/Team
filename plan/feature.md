# Feature: invite-token-decorators

Source: GitHub Tharga/Team#272. Tier 2 (a defect a consumer cannot work around).

## Goal

Short invitation links (`?tic=<token>`, minted since 3.20) resolve to their invitation in a real host.

## Cause

`ITeamService.GetTeamKeyByInviteKeyAsync` is a default interface member returning null.
`AuthorizationTeamServiceDecorator` and `AuditingTeamServiceDecorator` wrap every registered `ITeamService`
and neither implements it, so the call runs the default body and the store is never asked.

## Scope

- Forward `GetTeamKeyByInviteKeyAsync` in both decorators (pass-through read; not audited, the invite code
  is never recorded).
- Architecture guard: every decorator in the toolkit implements every default member of the interface it
  decorates. Fix any further gaps it finds.
- Behavioural tests per decorator, and one test through the real `AddThargaTeamBlazor` chain.
- Docs: note for hosts writing their own `ITeamService` decorator.

## Acceptance criteria

- `ITeamInvitationService.GetInvitationAsync(<short token>)` resolves through the registered chain.
- The guard fails on a decorator that leaves a default member to the interface.
- Full suite green.

## Done condition

PR merged; released as 3.21.1; #272 closed with a comment naming the fixed version.
