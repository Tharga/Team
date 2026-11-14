# Plan: support-start-mail-case

- [x] 1. NuGet check: nothing new beyond the recorded holds.
- [x] 2. Contracts: `StartMailCaseRequest`, `ISupportCaseService.StartMailCaseAsync`, `GetStartMailRouteAsync`, `GetCaseByIdAsync` (default members); service, authorization, auditing; first mail has no "Re:"; Mongo marks a staff-first case as not waiting on support.
- [x] 3. Blazor: `SupportComposeMail` (dialog and placeable), New message button on both boards, `SupportCaseById`, texts.
- [x] 4. Tests and docs.
- [~] 5. Push for testing; on the users word close-out, PR, close #483.

## Decisions
- The case is written first, then delivered through the existing mail channel as an answer is: sender rules, signature, quoting and tracker come for free, and a failing mail server leaves the case with the message marked not delivered.
- Unassigned needs `support:manage`, a team needs `support:all:manage`; there is no author, so membership and authorship give nothing.
- `GetCaseByIdAsync` is named differently from `GetCaseAsync(teamKey, caseId)` to avoid overload ambiguity. It covers #482 item 7; #482 still has items 1-6.
- The audit entry carries the case and file count, never the address or text.

## Release notes
New on `ISupportCaseService` (default members, so a host's own implementation keeps compiling): `StartMailCaseAsync`, `GetStartMailRouteAsync`, `GetCaseByIdAsync`. New components `SupportComposeMail` and `SupportCaseById`; "New message" on `SupportUnassignedView` and `SupportAcrossTeamsView`. Decorators of `ISupportCaseService` must forward the new members.
