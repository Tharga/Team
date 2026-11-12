# Feature: support-close-comment (#474)

An optional internal comment when closing a support case, shown in the conversation as the reason.

- `ISupportCaseService.CloseCaseAsync(teamKey, caseId, comment)`, same authorization as closing.
- Stored on the closure entry (`SupportMessage.InternalComment`), persisted in MongoDB.
- Internal: never mailed or posted, not audited, returned by `GetMessagesAsync` only to cross-team readers and the unassigned queue.
- Close dialog gets a reason field for staff and the unassigned queue; transcript shows it under the closure.
