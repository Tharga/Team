# Plan: invite-token-decorators

- [x] NuGet update (Microsoft.Testing.Extensions.CodeCoverage 18.11.0 -> 18.11.2, test projects only). Baseline 2654 tests green.
- [x] Tests first: `DecoratorDefaultMemberTests` (guard over all eight Tharga.Team* assemblies, 11 decorator/contract pairs found), `InviteTokenDecoratorForwardingTests` (per decorator), `ShortInvitationLinkResolutionTests` (real AddThargaTeamBlazor chain, with and without audit logging). Red before the fix: exactly the two ITeamService decorators; no other decorator has a gap.
- [x] Forward GetTeamKeyByInviteKeyAsync in AuthorizationTeamServiceDecorator (unscoped, the code is the check) and AuditingTeamServiceDecorator (not audited, the code is a bearer credential).
- [x] Full suite green: 2675 (Service 963, Blazor 1101, MongoDB 103, Mcp 107, Support 352, Entra 38, Images 11). Warnings unchanged at 16.
- [x] Docs: README "Invitations" and implementation guide "What an invitation link looks like" — fixed-in-3.21.1 note (assumes no other release lands first) and the forward-every-default-member rule for host-written ITeamService decorators.
- [ ] Close-out (after user confirms): Requests/backlog sweep, archive feature.md, remove plan/, `fix: invite-token-decorators complete`, PR.
