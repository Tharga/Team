# Plan: support-start-mail-case

- [x] 1. NuGet check: nothing new beyond the recorded holds.
- [x] 2. #483 contracts: `StartMailCaseRequest`, `StartMailCaseAsync`, `GetStartMailRouteAsync`, `GetCaseByIdAsync` (default members); service, authorization, auditing; first mail has no "Re:"; Mongo marks a staff-first case as not waiting on support.
- [x] 3. #483 Blazor: `SupportComposeMail`, New message on both boards, `SupportCaseById`, texts.
- [x] 4. #482: `SupportContactTable` (rows, copy buttons), organisation search, contact on assigned cases, `CaseExtras` slot, `CaseId` parameter on both boards.
- [x] 5. Tests and docs; the sample places the new parts.
- [~] 6. Push for testing; on the users word close-out, PR, close #483 and #482.

## Decisions
- The case is written first, then delivered through the existing mail channel as an answer is: sender rules, signature, quoting and tracker come for free, and a failing mail server leaves the case with the message marked not delivered.
- Unassigned needs `support:manage`, a team needs `support:all:manage`; there is no author, so membership and authorship give nothing.
- `GetCaseByIdAsync` is named differently from `GetCaseAsync(teamKey, caseId)` to avoid overload ambiguity.
- The audit entry carries the case and file count, never the address or text.
- Consent is always a row: the time when given, "No consent recorded" otherwise. The issue says to leave empty rows out, but that nobody recorded consent is something staff should see, and the old view said so.
- The open-case parameter is `CaseId` on both boards, as on `SupportCasesView`; a host reads its own query value into it.

## Release notes
New on `ISupportCaseService` (default members, so a host's own implementation keeps compiling): `StartMailCaseAsync`, `GetStartMailRouteAsync`, `GetCaseByIdAsync`. New components `SupportComposeMail`, `SupportCaseById`, `SupportContactTable`; "New message" on `SupportUnassignedView` and `SupportAcrossTeamsView`, which also take `CaseExtras` and `CaseId`. The contact now shows as a table with copy buttons and stays on an assigned case. Decorators of `ISupportCaseService` must forward the new members.
