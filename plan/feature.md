# Feature: support-send-as (#462, #473, #463)

Branch `feature/support-send-as`. Closes Tharga/Team#462, #473, #463 through the PR. Later PRs: #474 (close comment), #464 (Gmail-style quoting).

## Goal
A support answer goes out from the right address and signs as the right person, with nothing about any address built into the package.

## Scope

### #462 Send-as addresses per mailbox
- `MailOptions.SendAs`: extra addresses (each with an optional display name) the mailbox's SMTP account may send as. Bindable from configuration.
- A case remembers which of our addresses the customer wrote to (`SupportChannelBinding.MailboxAddress` already exists; confirm it holds the delivered-to address, not only the mailbox's own).
- Default sender of an answer, in order:
  1. the answering staff member's own address, when #473 allows it;
  2. the address the customer wrote to, when it is the mailbox's `FromAddress` or one of its `SendAs`;
  3. the mailbox's `FromAddress`.
- Per answer choice: `SupportAnswerRoute` lists the addresses the staff member may send from; the route line in `SupportCaseConversation` and `SupportUnassignedConversation` becomes a picker, preselected by the default above.
- The chosen sender decides the SMTP account and the `From` header, is checked in the service (an address in no list is refused, not only hidden in the UI), and is stored on the sent entry.
- The per-case reply address is built from the support address chosen (`support+...@` vs `hej+...@`) and both read back to the same case.

### #473 Answer from the staff member's own address (role based)
- `SupportAnswerOptions.StaffSenderRole`, default `Support`: members of that system role may have an answer go out from their own address and name. Empty turns the feature off (off by default is the safer reading of the issue; confirm).
- `Reply-To` is always the support address with the tracker, never the staff address, whether or not `PerCaseReplyTo` is on.
- "If possible": the staff address is used only when the mailbox declares it can send as it, by `MailOptions.SendAsDomains` (any address on those domains) or an explicit `SendAs` entry. Otherwise the chain falls back (see #462). A rejected sender at SMTP also falls back to the next address in the chain and is logged.
- Automatic mail (receipts, assistant answers, auto-close notices) is never sent from a personal address.
- `SupportAnswerRoute` shows `From` and `ReplyTo` before sending; the sent entry records the `From` it went out with.

### #463 Signature, closing text (extended per the user)
- `SupportAnswerOptions.NameTheAnswerer` (default false) and a configurable closing template, for example `Vänliga hälsningar,{newline}{Name}`, localized through the text provider; the configured signature lines follow.
- Per answer: "Sign with my name", on by default when the option is on.
- Quoted earlier entries written by support are named when they were signed with a name, otherwise `SupportName`.
- The name is the display name the service already resolves for `AuthorName`.
- Preview matches what is sent.

## Out of scope here
#474 (close comment) and #464 (Gmail-style quoting) are separate PRs.

## Acceptance
Each issue's own acceptance criteria hold. Tests use made-up addresses on more than one domain. Option off means mail is byte-identical to today.

## Done condition
All tests pass, docs (README and docs/ articles) updated, issues closed through the PR with evidence.
