# Feature: support mail, complete

Branch `feature/support-mail` off `origin/master` (3.29.0). One release for all of it, rather than several small ones (decided by the consumer, 2026-10-17). A consuming product links this source while it is built.

## Goal
Support by mail works end to end and is pleasant to work: answers reach the sender, known senders land in their team, files and formatting survive both ways, several mailboxes can feed one support, and the queue reads like the cases view. The sending parts are shared with mail to users (categories, messages, notices).

## Scope
1. Keep a mail-opened case's binding; never re-read the last mail (`n:*`). Done.
2. Duplicates in the support event ledger are ordinary, not errors.
3. Match the sender to a user and team: one team → assigned; several → unassigned with the teams suggested; unknown → unassigned.
4. Attachments in and out: stored through a host content store, listed on the message, downloadable under the case's scopes, attachable to an answer.
5. Rich text: messages carry sanitized HTML with a plain-text alternative; incoming HTML kept (sanitized); an HTML editor in the answer box.
6. Several support mailboxes, each with its own read position; an answer goes out from the mailbox the case came in through.
7. Delivery status visible (Pending, Sent, Failed with reason) and a retry.
8. The unassigned queue reads like the cases view: expandable cards, dates through the host's time template, filters.
9. No no-reply default anywhere: From and Reply-To are real, answerable addresses.
10. Mail to users (shared with support): categories with opt-out and an unsubscribe link; staff writing to users by person, list, role, access level, team or teams (no team writes to its users by hand, decided 2026-10-17); automatic notices from an event catalogue with per-user choices. Built on 4 and 5.
11. After the consumer's run against real mailboxes (2026-10-18): where a support answer goes, beside the answer box; writing to users as a filter plus added users with the resulting recipient list; one host mail template for every HTML mail, and a preview of messages and answers rendered by the same code.

## Done when
All of the above is in, every suite is green, the docs (`docs/articles`, READMEs) describe it with a "Moving to" section, and the consumer has exercised it against real mailboxes.
