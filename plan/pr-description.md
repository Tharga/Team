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
- **An answer speaks for support.** No staff member is named: no author heading, the host's signature after the
  answer, earlier support entries attributed to support's name in "On {date}, {name} wrote:", dates in the host's
  time zone (`o.Answers`).

## Mail to users (`Tharga.Team.Email`)

- **`AddThargaUserMail`** — categories each user turns on or off on their profile (`account` built in and
  required; `service` built in and required unless `ServiceRequired` is false), with how often each is sent and a
  required one linking to your terms, an unsubscribe link and the one-click headers on every optional mail,
  answered by `MapThargaMailUnsubscribe()` — a page that says it turns off only that category and shows the
  signed-in user every category, or offers sign-in — and a persisted queue any instance sends, reading the user's
  choice and address when it sends. There is no default sending address and no no-reply one.
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

## Mail to contacts (`Tharga.Team.Email`)

- **`AddThargaContactMessages`** — staff holding the `contacts:send` system scope write to people who are not
  users, listed by the host from its own records with its own filters (`ContactAddressee`: address, name, a
  reference returned with every outcome, a detail line, and an optional reason to leave the row out).
  `IContactMessageService` checks the list — each address once, who opted out, which rows are left out — previews
  the mail, queues one per address on the user mail queue with the content stored once, and keeps a log of what
  became of every address. `ContactMessageComposeView` and `ContactMessageLogView` sit beside the host's own filter.
- **An opt-out that holds.** Every contact mail says who sends it (`SenderLine`), why it came, and carries an
  unsubscribe link and the one-click headers; the page offers to stop that category or all mail from the product,
  and the opt-out is kept for the address, for good, checked again when each mail is sent.
- **`/login` honours a local `returnUrl`**, so the unsubscribe page's sign-in leads back to the mail settings.

## One mail template

**`AddThargaMailTemplate(o => o.Html = …)`** frames every HTML mail the toolkit sends — support answers, messages,
notices and invitations — in your own HTML, with `{{content}}`, `{{subject}}`, `{{footer}}` and `{{preheader}}`.
Mail written as text alone gets an HTML part so the frame always applies; without a template nothing changes. The
previews are rendered by the same code, in a sandboxed frame. `SupportHtml` gives support answers a frame of their
own.

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
- A host with its own `IUserMailOutbox` stores `UserMailBatch.RecipientAddresses` and leaves contact mail out of a
  user's history; `UserMailKind` gained `Contact`.
- New dependencies: `HtmlSanitizer` (Service), `DnsClient` and `AngleSharp` (Email).

Every change and the new text keys are listed in the *Moving to 4.2* sections of
[Support cases](docs/articles/support-cases.md), [Email](docs/articles/team-email.md),
[Mail to users](docs/articles/user-mail.md) and [Messages to contacts](docs/articles/contact-messages.md).
