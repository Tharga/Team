# Feature: Support staff-only files

GitHub issue: #573. Branch: `feature/support-secure-files`. Minor release, additive only.

## Goal

A member writing to support can hand over a file that only the product's support staff and the member
who uploaded it can open. Colleagues reading the same case see that a file exists, never what it is.
The file never leaves the application, and its content is removed after a set time.

## Scope

- `SupportAttachment` gains `Secure`, `UploadedBy`, `Withheld` and `RemovedAt`.
- A member uploads secure files with a reply or when raising a case, through additive members:
  a new `ReplyToCaseAsync` overload taking `secureAttachments`, and `SupportRaiseOptions.SecureAttachments`.
- **One enforcement point**, `AuthorizationSupportCaseServiceDecorator`, beside the internal-comment
  filter. A secure attachment is returned in full to cross-team support readers (`support:all:read` /
  `support:all:manage`) and to its uploader. Every other reader gets it with `FileName` and
  `ContentType` blank and `Withheld = true`, and `OpenAttachmentAsync` refuses them.
  UI, REST and MCP therefore agree.
- A secure file never leaves: it is stripped from what the domain hands to every channel
  (mail, Slack, a host's own), to the assistant's responder and to conversation-feedback sinks.
  The mail channel also skips one itself.
- Opening, removing and a refused opening of a secure file are audited.
- `SupportCaseOptions.SecureFiles.KeepFor` defaults to 30 days. A hosted sweep deletes expired
  content and sets `RemovedAt`, safely on several instances at once. Cross-team support
  (`support:all:manage`) may remove a file earlier.
- Blazor: `SupportAttachmentPicker` gets a secure mode. `SupportPanel` offers it on the support
  tab only, never to the assistant. `SupportCaseConversation` offers it to members, and the
  transcript shows secure, withheld and removed files distinctly.

## Out of scope

- The raise form in `SupportCasesView` (not named in the issue). Possible follow-up.
- Secure files on support's own answers or on inbound mail.

## Acceptance criteria

1. A member's reply or new case can carry secure files; existing calls behave exactly as before.
2. Cross-team support and the uploader see and open a secure file. The team's own support readers
   and other members see it withheld (no name, no type) and opening it is refused.
3. No secure file reaches mail, Slack, a host channel, the assistant or a feedback sink.
4. Opening and removing a secure file, and a refused opening, write audit entries.
5. After `KeepFor` the content is gone and the entry shows it was removed. Two sweeps running at
   once remove each file once.
6. A store that does not keep the secure flag cannot make a secure file public: the upload is
   undone and refused.
7. All tests pass; README and docs describe the feature.

## Done when

Acceptance criteria met, docs updated, the user has tested from the pushed branch and confirmed.
