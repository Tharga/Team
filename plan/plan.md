# Plan: support-close-comment

- [x] 1. NuGet check: nothing to update beyond the recorded holds.
- [x] 2. Contract, service, decorators, Mongo mapping.
- [x] 3. Blazor: dialog field, transcript, both callers, text keys.
- [x] 4. Tests (service, authorization, channel, reopen, render) and docs.
- [~] 5. Push for the user to test; on their word the close-out commit without `plan/`, PR, close #474.

## Decisions
- Visibility rule: cross-team readers (`support:all:read` / `support:all:manage`) and the unassigned queue see it; the author, their team and team-scoped support readers do not.
- Not audited: the audit trail is read more widely than the conversation.
- `SupportQueueView` (team queue) is left as it was: its readers are the team's own.

## Release notes
New `ISupportCaseService.CloseCaseAsync(teamKey, caseId, comment, ct)` overload (implementers and decorators update); `SupportMessage.InternalComment`.
