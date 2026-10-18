# Support mail, complete — and mail to users (4.2)

Support by mail now works end to end, and the toolkit can mail the people who use your product: in categories
they choose from, through a persisted queue, with staff messages and automatic notices on top.

## Support mail

- **Known senders land in their team.** Every mail read is checked by DKIM. A mail whose verified sender is the
  one account holding the address, active in exactly one team, opens in that team as if raised on the site;
  anything less opens unassigned with the teams the address matched as suggestions, the reason, and a one-click
  assign.
- **Several mailboxes.** `o.AddMailbox("billing", m => …)` beside `o.Email`; each is read on its own, a case is
  answered from the mailbox it came in through, and staff see which address that was. `FirstReadSince` starts a
  mailbox that already holds mail at a date.
- **Files both ways.** A mail's attachments, inline images and forwarded mail are kept (GridFS by default, or your
  own `ISupportAttachmentStore`), answers and replies carry files, and a file opens under the case's own read
  rules, in the browser only when its type cannot run script.
- **Formatting both ways.** Answers are written in a small rich-text editor, a mail's HTML is kept without its
  quoted thread, and everything goes through one sanitizer (`SafeHtml`) on the way in and at every render; a
  formatted answer is mailed as HTML beside the same text.
- **The thread.** An answer carries the whole conversation and every message id is recorded, so a reply to any
  of them finds its case.
- **Delivery.** Every answer says whether it was delivered, with the reason when it was not; a bounce marks the
  answer it names; **Send again** (`ResendAsync`) retries it.
- **The unassigned queue reads like the cases view**: expandable cards, the host's dates, a filter with search,
  and `RefreshAsync()` for a host's backplane.
- **Where the answer goes.** Beside every support answer box: the address the answer is mailed to and the support
  address it goes out from, or that it is read on the site, and outside production whether the outbound policy
  redirects it or holds it back (`GetAnswerRouteAsync`). **Preview** shows the answer as the customer will receive
  it, with the conversation quoted (`PreviewAnswerAsync`).

## Mail to users (`Tharga.Team.Email`)

- **`AddThargaUserMail`** — categories each user turns on or off on their profile (`account` and `service` built
  in and required), an unsubscribe link and the one-click headers on every optional mail, answered by
  `MapThargaMailUnsubscribe()`, and a persisted queue any instance sends, reading the user's choice and address
  when it sends. There is no default sending address and no no-reply one.
- **`AddThargaUserMessages`** — staff holding the `messages:send` system scope write to chosen users: a filter of
  teams and who in them (role, access level or everyone), and users added by name whatever the filter says. Before
  sending they see the list of recipients — who came in how, and who turned the category off
  (`GetAddresseesAsync`) — and a preview of the mail (`PreviewMailAsync`); afterwards, what became of each mail.
- **`AddThargaUserNotices`** — a catalogue of notices raised from server code, delivered at once or in a daily
  summary, chosen per user over each team's default (the `notices:manage` team scope).
- **History and bounces** — every mail to a user is in their history (`IUserMailHistoryService`,
  `UserMailHistoryView`), and mail sent from a support mailbox's address is marked bounced when it comes back.
- **Invitations** go through the same outbox once it is registered: recorded, one immediate attempt from its
  address, so the inviter is still told whether it went.

## One mail template

**`AddThargaMailTemplate(o => o.Html = …)`** frames every HTML mail the toolkit sends — support answers, messages,
notices and invitations — in your own HTML, with `{{content}}`, `{{subject}}`, `{{footer}}` and `{{preheader}}`.
Mail written as text alone gets an HTML part so the frame always applies; without a template nothing changes. The
previews are rendered by the same code, in a sandboxed frame.

## Moving to 4.2

- `ISupportCaseService` gained nine members (files, formatting, `OpenAttachmentAsync`,
  `GetAttachmentLimitsAsync`, `ResendAsync`, `GetAnswerRouteAsync`, `PreviewAnswerAsync`) and
  `ISupportCaseStore.SetMessageDeliveryAsync` an `error` parameter: implementers and decorators add them. A host
  with its own REST surface adds endpoints for the answer's route and its preview.
- `OutboundMail`, `InboundMail` and `MailFetchResult` gained positional parameters: recompile.
- Support mail from a known sender may now open in their team; register an `ISupportSenderDirectory` that finds
  nobody to keep every mail unassigned. Reading mail needs outbound DNS for the DKIM check.
- Mail attachments are now stored in your database (25 MB a file, 50 MB and 20 files a mail by default).
- `SupportUnassignedView` carries no heading any more.
- With `AddThargaUserMail`, invitations are sent from `UserMailOptions.Mail`, not `EmailOptions`.
- New dependencies: `HtmlSanitizer` (Service), `DnsClient` and `AngleSharp` (Email).

Every change and the new text keys are listed in the *Moving to 4.2* sections of
[Support cases](docs/articles/support-cases.md), [Email](docs/articles/team-email.md) and
[Mail to users](docs/articles/user-mail.md).
