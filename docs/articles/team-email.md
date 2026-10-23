# Email: team mailboxes, the postmaster and invitations

`Tharga.Team.Email` is the mail layer of the toolkit: reading a mailbox over IMAP, sending over SMTP, and the
MIME handling both need. Four things use it:

- **Support cases by email**, in `Tharga.Team.Support`, which now depends on this package.
- **Team mailboxes** — an address per team that receives real mail, described below.
- **Invitation email**, which moved here from `Tharga.Team.Service` in 3.29.
- **[Mail to users](user-mail.md)** — categories, unsubscribe links and a persisted queue, with
  [messages](user-messages.md) and [automatic notices](user-notices.md) on top. New in 4.2.

The package exists so that MailKit reaches only hosts that use mail. `Tharga.Team.Blazor` never references it:
the views depend on contracts in `Tharga.Team` alone.

## The transport

Everything above goes through one transport, `IMailClient`, which never throws: every outcome is a result.

- **Sending:** `OutboundMail` carries the text body, and optionally `BodyHtml` (sanitized by the caller) sent as
  `multipart/alternative`, `Attachments` (`MailAttachment`, with a `ContentId` for an image the HTML shows as
  `cid:`, sent beside it as `multipart/related`), threading headers, a reply address, and `UnsubscribeUrl`, sent as
  `List-Unsubscribe` — with `List-Unsubscribe-Post` for one-click unsubscribing when it is https.
- **Reading:** `InboundMail` carries the envelope and the text, and also `BodyHtml` — **exactly as the sender
  wrote it, not sanitized**; run it through `SafeHtml` before showing it — and `Attachments`
  (`InboundAttachment`: attachments, inline images with their Content-ID, and forwarded mail as `.eml`, with
  file names sanitized). A delivery status notification fills `BouncedMessageId` and `BounceReason`.
- **Who sent it:** every mail read is checked by DKIM. `SenderVerified` is true when a signature verifies, signs
  `From` and the whole body, and its domain is the `From` domain or a parent of it; `SenderDomain` names that
  domain. Keys are looked up in DNS (cached ten minutes, a miss one minute, a lookup cut off after five seconds),
  and anything that goes wrong means not verified. **It vouches for the domain's mail system, not for a person.**
- **A mailbox that could not be read** returns `MailFetchResult.Error` with the server's reason, never the
  password.
- **`MailOptions`** gained `FirstReadSince`, where a mailbox with no read position starts, and the file limits
  `MaxAttachmentBytes` (25 MB), `MaxAttachmentTotalBytes` (50 MB) and `MaxAttachmentCount` (20).
- **`HtmlText.ToPlainText(html)`** is the plain-text rendering the toolkit uses for HTML: paragraphs kept, list
  items as `- ` lines, quotes as `> ` lines, links as `text (address)`.
- **`IMailBounceHandler`** receives a bounce support read that names no support answer; see
  [Bounces](user-mail.md#bounces).

## Team mailboxes

Every team can receive mail at an address nobody can guess, and a host decides what becomes of it.

```csharp
builder.Services.AddThargaTeamRepository(o =>
{
    o.RegisterTeamRepository<TeamEntity, TeamMember>();   // provides the key and mail stores
});

builder.Services.AddThargaTeamMailbox(o =>
{
    o.Domain = "example.app";                      // every team address is on this domain
    o.Suffix = "-case";                            // {key}-case@example.app
    o.Mail.Imap.Host = builder.Configuration["Mailbox:Imap:Host"];
    o.Mail.Imap.UserName = builder.Configuration["Mailbox:Imap:UserName"];
    o.Mail.Imap.Password = builder.Configuration["Mailbox:Imap:Password"];
});

builder.Services.AddScoped<IInboundMailContentStore, MyBlobContentStore>();   // where raw mail is kept
```

Without IMAP settings the mailbox issues and rotates addresses but reads nothing. With them, a poller reads the
mailbox, and **refuses to start without an `IInboundMailContentStore`**, naming the interface.

### Addresses

A team's address is `{key}{suffix}@{domain}` — for example `8fjgthitwne3-case@example.app`. The key is random,
lowercase letters and digits (twelve by default, about 62 bits), and names no team: an address built from a team
id can be guessed, and then anyone can feed mail into a team.

An item inside the team — a case, a ticket — has an address of its own: `{key}{suffix}+{itemKey}@{domain}`. The
item key is the host's and is handed back with every mail sent to that address. `ITeamMailboxService.GetItemAddressAsync`
builds it on the team's current key.

### Rotating

A team holds at most two keys: the **primary**, used for everything new, and a **secondary**, still accepted until
it retires.

- `EnsureKeyAsync` issues the first key, and does nothing when the team has one. Two instances issuing at the same
  moment end with one key.
- `RotateKeyAsync` makes a new primary and keeps the old one as the secondary for `RetirementPeriod` (default 90
  days). A secondary from an earlier rotation retires at once.
- Mail to a secondary still reaches the team, marked `RetiringKey` with the retirement date, so the team can tell
  the sender the new address in time.
- Retired keys are kept, never reissued: mail to an old address reaches the postmaster, never a team that drew the
  same key later.
- **Host code can issue a key on its own authority** through `ITeamMailboxProvisioning` — when a feature is switched
  on, or to show an item's address to whoever may read the item. It is an *internal* service: it checks nothing, so
  it is never injected into a component, and a host calls it only behind a check of its own.

### Receiving

**One catch-all mailbox receives everything.** There is no mailbox per team to provision or rotate; the whole
domain lands in one, and each message is routed by the address it was actually **delivered** to —
`Delivered-To`, `X-Original-To`, `Envelope-To`, most trustworthy first — never the `To` header, which a forward or
a BCC does not reflect.

| Delivered to | Goes to | `MailRouteReason` |
|---|---|---|
| A team's primary key | That team | `PrimaryKey` |
| A team's secondary key, before it retires | That team, marked | `RetiringKey` |
| A retired key | The postmaster | `RetiredKey` |
| A key nobody holds, or no address of ours | The postmaster | `UnknownAddress` |

**Nothing is dropped.** A mail that reached an organisation's address may already count as received by it, even at
a retired or mistyped address, so whatever no team claims is kept by the postmaster.

**Each delivery is kept once**, however many instances read the mailbox and however often. A mail's id is derived
from its Message-Id and the address it was delivered to — one mail sent to two teams is two deliveries — and the
store refuses an id it already holds. Unlike the support event ledger, this does not expire.

**A mail that cannot be kept is retried, not skipped.** The raw message is saved first and the mail recorded after,
so a crash in between leaves only content that the next read overwrites. If either fails, the poller stops just
before that mail and reads it again next time; the mails before it in the batch are kept.

The mailbox is only ever read: nothing is marked, moved or deleted on the server.

**A forwarding catch-all names its own mailbox first.** A catch-all that forwards to a mailbox — cPanel's *Default
Address*, for example — writes that mailbox into `Delivered-To` and keeps the address the sender wrote in
`Envelope-To`. Routing reads every delivery header and skips any address that is not a team's, so the mail still
reaches its team; and for mail no team claims, the postmaster records the first address on your domain that is not
the catch-all mailbox itself (`TeamMailboxOptions.Mail.Imap.UserName`), so staff see who the sender meant.

Setting one up:

- Point the domain's catch-all at one mailbox, and give that mailbox's IMAP settings to `TeamMailboxOptions.Mail`.
- Turn off any automatic folders for plus-addressing (`name+tag@`) on that mailbox: item addresses use `+`, and the
  poller reads one folder (`Mail.Folder`, `INBOX` by default).
- Send a test mail to an address nobody owns and check that the postmaster shows that address, not the mailbox's.

### Health

`ITeamMailboxHealth.CheckAsync` connects to the catch-all mailbox and opens its folder read-only, and returns a
`MailboxHealth` (`IsConfigured`, `IsReachable`, `Error`) without throwing. Put it on your health endpoint: the
poller only logs a failed read, so a wrong password otherwise shows as mail that never arrives.

### What a host does with the mail

Nothing is registered anywhere automatically. `ITeamMailboxService` lists a team's mail, opens one, returns its
raw content, and records what became of it with `SetOutcomeAsync(teamKey, mailId, outcome, owner)` — an outcome and
an owner in the host's own terms (`"registered"`, a case id). Recording `null` returns the mail to the unhandled
list. Who handled it and when is kept.

`ITeamMailNotifier` raises `MailUpdated` for every mail received, handled, routed, archived or restored
(`TeamMailChange` `Received`, `Handled`, `Rerouted`, `Archived`, `Restored`; the postmaster's own changes carry no
team). It is a singleton raised in this process only, outside any circuit: a component marshals with `InvokeAsync`,
and a host with several instances relays it over its own backplane. It carries no content — a handler reads the mail
back through the service, where access is checked.

**Several instances and the postmaster.** The mail the postmaster holds belongs to no team, so a change to it has no
team group to be relayed to; send it to the staff who may read the postmaster (`mailbox:postmaster:read`) instead.
Which changes those are is `TeamMailUpdatedEventArgs.AffectsPostmaster`: a change with no team (mail taken in,
archived or restored) **and a routing**, which names the team the mail went to and takes it off the postmaster's list.
Testing the team key for null alone leaves a routed mail on every other instance's page.

```csharp
// On every instance: send what changed here to the staff on the other instances.
notifier.MailUpdated += (_, e) =>
{
    if (e.AffectsPostmaster) _ = backplane.SendToPostmasterReadersAsync();
};
```

```razor
@* On a page showing the postmaster; when the backplane delivers the message, ask the view to read again *@
<PostmasterView @ref="_postmaster" />

@code {
    private PostmasterView _postmaster;

    private Task OnPostmasterChangedAsync() => _postmaster.RefreshAsync();
}
```

`RefreshAsync()` reads the list and the count again through `IPostmasterService`, so nothing in the message is trusted
and a missed or repeated message does no harm. An open card keeps what it opened. Do not answer a relayed message with
`ITeamMailNotifier.Notify` while also relaying from `MailUpdated`: the notification would be relayed again.

### Content

The toolkit keeps the envelope — sender, recipients, subject, Message-Id, route — and hands the **raw message**
(`.eml`) to `IInboundMailContentStore`, which the host implements: blob storage, a file share. Nothing large
reaches the toolkit's database. `SaveAsync` must overwrite: the same delivery read twice saves the same content
under the same id.

### Scopes

| Scope | Kind | Grants |
|---|---|---|
| `mailbox:read` | Team | List and open the team's mail, see its addresses |
| `mailbox:handle` | Team | Record what became of a mail |
| `mailbox:manage` | Team | Issue and rotate the team's address |
| `mailbox:postmaster:read` | System | List and open mail no team owns, and its files |
| `mailbox:postmaster:manage` | System | Route such a mail to a team, or archive and restore it |

Three team scopes, because three different people use a mailbox: someone handling mail every day should not
rotate the address every sender knows. Their access levels are the host's — `ReadAccessLevel` and
`HandleAccessLevel` default to User, `ManageAccessLevel` to Administrator, and `null` registers a scope grant-only,
held only through a role.

## The postmaster

Mail no team owns. `IPostmasterService` is a system service: no team scope reaches it, because unrouted mail may
hold any team's material.

- **The listing is deliberately thin** — sender, subject, time and reason, and for an archived mail when and by
  whom — enough to decide what to open.
- **Opening is the audited act.** `OpenAsync`, `GetContentAsync`, `GetBodyAsync` and `OpenAttachmentAsync` are
  recorded every time, under `mailbox:postmaster:read`.
- **`RouteAsync(mailId, toTeamKey)` delivers a mail to a team**, marked `Rerouted`. It refuses a team that does not
  exist, and a mail someone else already routed returns `null`. The parameter is not `teamKey` because the team is
  where the mail goes, not who is asking: routing is authorized by the system scope alone.
- **Retention:** mail the postmaster holds longer than `PostmasterRetention` (default 90 days) is deleted with its
  content, archived or not, and every deletion is recorded.

When a team is removed, its mailbox goes with it: its mail, the content kept for it, and its keys.

### Held and archived

A mail the postmaster holds waits for someone to route it — or to put it away without routing it: spam, a mail sent
by mistake, one already dealt with elsewhere.

- **`ArchiveAsync(mailId)`** records the outcome `PostmasterMailOutcome.Archived` (`"archived"`) on the mail, with
  `HandledAt` and `HandledBy` — the archiver's name as it was then — and announces `TeamMailChange.Archived`. The mail
  leaves `ListAsync()` and `GetCountAsync()`, and stays the postmaster's: it can still be opened, read, restored and
  routed, and it goes with the same retention as the rest.
- **`RestoreAsync(mailId)`** clears the outcome, who and when, puts the mail back among the held mail and announces
  `TeamMailChange.Restored`.
- **Both are idempotent and take `mailbox:postmaster:manage`**, recorded as changes. Archiving an archived mail, or
  restoring a held one, returns it unchanged and announces nothing; a mail routed to a team, or one the postmaster no
  longer holds, returns `null` — neither works on a team's mail.
- **Routing an archived mail** delivers it as any other: the outcome is cleared on the way, so the team receives it
  unhandled.
- **`ListAsync(PostmasterMailState state, cursor, pageSize)`** lists `Held` or `Archived` mail. Both are ordered by
  when the mail was received, newest first, with the same cursor — archived mail is not ordered by when it was
  archived; each summary says when (`PostmasterMailSummary.ArchivedAt`) and by whom (`ArchivedBy`). `ListAsync()`
  without a state is the held mail, as before.

### Reading a mail

A view cannot parse a mail itself, so the postmaster reads it for one:

- **`GetBodyAsync(mailId)`** returns a `ReceivedMailBody`: `Text` (the text part, or the formatting rendered as
  text), `Html` (the formatting through `SafeHtml` — formatting, web and mail links, remote images replaced by their
  alt text, `cid:` references kept; null when there is none), `Attachments` (every file as a `SupportAttachment`:
  attachments, inline images and forwarded mail as `.eml`, signatures left out, each `Id` its position counted
  from 1) and `Images` (the inline images the formatting names, of a type a browser shows without running anything,
  up to `ReceivedMailBody.MaxInlineImageBytes`, 2 MB, to show in place). Nothing is cut: quoted history stays,
  since the point is to find out whose the mail is. Null when the postmaster holds no such mail or its content is
  no longer kept.
- **`OpenAttachmentAsync(mailId, attachmentId)`** returns one file as a `SupportAttachmentContent` for the caller to
  dispose, or null. Serve it as `SupportAttachmentContentTypes.ServedAs` says, never as the declared type.

A host with its own REST surface adds endpoints for listing by state, archiving, restoring, the body and a file,
each calling `IPostmasterService` so the scope and the record apply.

## Views

Both are in `Tharga.Team.Blazor` and take a `TimeTemplate` to render moments the host's way.

- **`PostmasterView`** — reads like the support mailbox. A switch shows the **Held** or the **Archived** mail; each
  mail is an expandable card whose header shows the subject (or "(no subject)"), the sender, when it arrived, why it
  reached the postmaster and, once archived, when and by whom. **Opening a card is the audited open**, once per mail
  however often the list is read again: it shows who the mail went to (From, To, Cc, Delivered to, Received) and the
  message as a conversation bubble — the sender, when it was written, the formatting sanitized with its own images in
  place, or the text as paragraphs — with its files to open or save through `OpenAttachmentAsync`. Inside a held mail
  it is routed to a team chosen from a list (when the reader may list teams) or typed, or **archived** after a
  confirmation; an archived mail is **restored**. A reader who may read but not decide is told so.
  `ShowTitle` (default `true`) shows the title and the count of held mail; set it to `false` when the view sits in a
  tab whose header shows the count from `GetCountAsync()`. `RefreshAsync()` reads the list again for a host that
  hears of a change on another instance; the in-process notifier is listened to as before.
- **`TeamMailAddressView`** — the selected team's address with a copy button, a retiring address as a warning with
  its date, and changing the address only after a confirmation that says senders must be told. `AllowChange`
  hides creating and changing for a reader without `mailbox:manage`.

A reader without the grant sees a refusal, never the data. Texts are in `PostmasterViewText` and
`TeamMailAddressViewText`.

## Invitation email

`AddThargaEmailInvitations()` registers the SMTP invitation sender. Settings are `EmailOptions`, from
`o.Email` on the Blazor options as before, or passed to the call:

```csharp
builder.Services.AddThargaEmailInvitations(o =>
{
    o.SmtpHost = "smtp.example.com";
    o.FromAddress = "hello@example.com";   // an address a reply reaches
});
```

It sends through the same transport as everything else, obeys the outbound mail policy, and throws when the server
refuses, so the invite dialog can tell the inviter. A sender the host registered itself — `AddEmailService<T>()` —
still wins, whichever is registered first.

**With [mail to users](user-mail.md#invitations) registered, this sender is replaced**: invitations are recorded in
its outbox, sent from `UserMailOptions.Mail` in one immediate attempt, marked bounced when they come back, and
shown in the invited person's mail history. `AddThargaEmailInvitations()` is then unnecessary.

## Moving to 4.2

Coming from 3.x, follow *Migrating to 4.0* in the [implementation guide](implementation-guide.md) first.

- **`OutboundMail`, `InboundMail` and `MailFetchResult` gained trailing positional parameters** — `OutboundMail`:
  `Attachments`, `BodyHtml`, `UnsubscribeUrl`; `InboundMail`: `BouncedMessageId`, `BounceReason`,
  `SenderVerified`, `SenderDomain`, `Attachments`, `BodyHtml`; `MailFetchResult`: `Error`. Recompile; code that
  deconstructs them positionally adds the new elements.
- **`InboundMail.BodyHtml` is raw.** Sanitize it before showing it anywhere.
- **`InboundMail.HadAttachments`** is true when files were present, whether or not they were carried in
  `Attachments`; mail read from a mailbox now carries them.
- **Every mail read is DKIM-checked over DNS**, so a host reading mail needs outbound DNS. Without it nothing is
  verified and nothing breaks.
- **Dependencies:** the package adds `DnsClient` and `AngleSharp`, takes the ASP.NET Core framework reference
  privately (Data Protection and the unsubscribe route of mail to users), and no longer lists
  `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.Options`, which the framework provides.
- **With `AddThargaUserMail`, invitations go through mail to users** — see above.

### The postmaster

- **`IPostmasterService` gained five members**: `ListAsync(PostmasterMailState, cursor, pageSize)` (an overload, so
  code compiled against `ListAsync(cursor, pageSize)` keeps running), `ArchiveAsync`, `RestoreAsync`, `GetBodyAsync`
  and `OpenAttachmentAsync`. A host implementing or decorating it adds them; one with its own REST surface adds the
  endpoints.
- **`GetCountAsync()` and `ListAsync()` leave archived mail out.** Nothing is archived until someone archives it, so
  existing data counts as before.
- **`ITeamMailStore` gained three default members** — `ListUnroutedAsync(PostmasterMailState, …)`,
  `ArchiveUnroutedAsync` and `RestoreUnroutedAsync`. A host's own store keeps compiling, lists held mail through its
  old method, and refuses archiving with `NotSupportedException` until it implements them. It should also leave
  archived mail (outcome `"archived"`) out of `ListUnroutedAsync(cursor, pageSize)` and `CountUnroutedAsync`, and
  clear the outcome, `HandledAt` and `HandledBy` in `RouteAsync`. The built-in store does all of it.
- **`TeamMailChange` gained `Archived` and `Restored`**, after the existing values; a `switch` over it handles them.
- **`PostmasterMailSummary` gained `ArchivedAt` and `ArchivedBy`; `ReceivedMailBody` and `ReceivedMailImage` are new.**
  `InboundMailItem.HandledBy` on an archived mail is the archiver's name, where on a team's mail it is the subject of
  whoever recorded the outcome.
- **MongoDB:** the `InboundMail` collection gains the index `TeamKey_Outcome_ReceivedAt` (team, outcome, received time
  descending, mail id), created at start-up like the others.
- **`PostmasterView`** lists cards rather than buttons, opens a mail when its card opens, and calls `ListAsync(state, …)`
  and `GetBodyAsync`; a host whose tests click a subject button to open a mail clicks the card instead. It takes
  `ShowTitle` and offers `RefreshAsync()`. The texts are in `PostmasterViewText`; translate the new keys —
  `team.mailbox.postmaster.state.label` ("Which mail"), `.state.held` ("Held"), `.state.archived` ("Archived"),
  `.noneArchived` ("No mail has been archived."), `.archived.by` ("Archived by {0}"), `.archived.when` ("Archived"),
  `.archive` ("Archive"), `.confirmArchive.title` ("Archive this mail?"), `.confirmArchive.body` ("The mail goes to
  no team. It moves to the archived mail, where it can be restored until it is deleted with the rest."),
  `.confirmArchive.accept` ("Archive"), `.confirmArchive.cancel` ("Cancel"), `.archived` ("The mail was archived."),
  `.restore` ("Restore"), `.restored` ("The mail is back among the held mail."), `.noText` ("The mail has no
  text."), `.contentUnavailable` ("What the mail said is no longer kept."), `.manageNotPermitted` ("You may read this
  mail, but not route, archive or restore it.") and `.noSuchTeam` ("No team has that key."). The files reuse the
  `team.support.attachments.*` keys. No existing key was reworded.

## Moving to 3.29

> [!IMPORTANT]
> **The built-in SMTP invitation sender moved to `Tharga.Team.Email`.** Setting `o.Email` no longer registers a
> sender by itself. Install `Tharga.Team.Email` and call `builder.Services.AddThargaEmailInvitations();`. Until
> you do, startup logs a warning and invitations are offered as a link to copy. A custom `ITeamEmailSender` needs
> no change.

- **`SmtpTeamEmailSender` is gone.** It lived in `Tharga.Team.Service` and used `System.Net.Mail`; the replacement
  uses MailKit. Code that referenced the type directly registers `AddThargaEmailInvitations()` instead.
- **The mail transport moved** from `Tharga.Team.Support.Email` to the namespace `Tharga.Team.Email`, and
  `ISupportMailClient` is now `IMailClient`. Support configuration (`SupportCaseOptions.Email`) is unchanged apart
  from the namespace of `MailOptions`.
- **`UseSsl` on port 587 or 25 now means STARTTLS, required**, as it did for the old invitation sender. The support
  transport used to try TLS from the first byte there and could not connect; elsewhere (465, 993) nothing changes.
