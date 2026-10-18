# Mail to users

The toolkit can mail the people who use your product: in **categories** each user chooses from, with an
**unsubscribe link** in every mail they could have turned off, through a **persisted queue** that whichever
instance claims a mail sends. Three things are built on it:

- **[Messages to users](user-messages.md)** — your own staff writing to chosen users of chosen teams.
- **[Automatic notices](user-notices.md)** — mail raised by something that happened, which each user tunes.
- **Invitations** — once mail to users is registered, invitations go through it too.

Every one of them is recorded, so a user's **mail history** shows what was sent to them and what became of it,
and a mail the receiving server returns is marked **bounced**.

The services are in `Tharga.Team.Email` (namespace `Tharga.Team.Email.Users`), the contracts in `Tharga.Team`,
storage in `Tharga.Team.MongoDB` and the views in `Tharga.Team.Blazor`. New in 4.2.

## Enabling it

```csharp
builder.Services.AddThargaTeamRepository(o =>
{
    o.RegisterUserRepository<UserEntity>();               // a mail's address is read from the user record
    o.RegisterTeamRepository<TeamEntity, TeamMember>();
});

builder.Services.AddThargaUserMail(o =>
{
    o.Mail.Smtp.Host = builder.Configuration["UserMail:Smtp:Host"];
    o.Mail.Smtp.UserName = builder.Configuration["UserMail:Smtp:UserName"];
    o.Mail.Smtp.Password = builder.Configuration["UserMail:Smtp:Password"];
    o.Mail.FromAddress = "hello@example.com";             // an address a reply reaches
    o.Mail.FromName = "Example";
    o.BaseUrl = "https://app.example.com";                // unsubscribe links are built on it

    o.AddCategory(new UserMailCategory { Key = "news", Name = "Product news", Description = "What is new, a few times a year." });
    o.AddCategory(new UserMailCategory { Key = "tips", Name = "Tips", OnByDefault = false });
});

var app = builder.Build();
app.MapThargaMailUnsubscribe();                          // where unsubscribe links lead
```

`AddThargaUserMail` registers `IUserMailPreferenceService`, `IUserMailHistoryService`, `IUserMailQueue`, the
background sender, a bounce handler and the invitation sender (see [Invitations](#invitations)), and turns on
ASP.NET Core Data Protection and the outbound mail policy.

Storage comes from `AddThargaTeamRepository`: the choices, the queue, and — with a user repository registered —
the directory that reads an address from the user record when a mail is sent. Each is registered with `TryAdd`,
so registering your own `IUserMailPreferenceStore`, `IUserMailOutbox` or `IUserMailRecipientDirectory` first keeps
mail elsewhere.

**There is no default sending address, and no no-reply one.** Until `o.Mail.FromAddress` and `o.Mail.Smtp.Host`
are set, mail is queued and kept but nothing is sent, and the sender says so once in the log. Set them and the
waiting mail goes. Use an address a reply reaches; `o.ReplyTo` sends replies somewhere else when that is wanted.
Only the SMTP half of `o.Mail` is used.

## Categories

Every mail goes out in a category, and a category is what a user turns on or off. Two are built in, and both are
**required** — nobody can turn them off, so no choice is stored for them and their mail carries no unsubscribe
link:

| Key | Name | For |
|---|---|---|
| `account` (`UserMailCategories.AccountKey`) | Account and security | The user's own account, access and its security; invitations go here |
| `service` (`UserMailCategories.ServiceKey`) | Service messages | What everyone needs to know, such as planned maintenance or changed terms |

They exist so that mail the toolkit sends itself always has a category every host has. Their keys are reserved.

A host adds its own with `o.AddCategory(new UserMailCategory { ... })`:

| Property | Default | |
|---|---|---|
| `Key` | — (required) | 1–64 lowercase letters, digits, dots, dashes or underscores, starting with a letter or digit |
| `Name` | — (required) | The English name users see |
| `Description` | null | One sentence on what the mail is about |
| `NameKey` / `DescriptionKey` | `userMail.category.{Key}.name` / `.description` | The text keys a translation is registered under |
| `Required` | `false` | Everyone receives it whatever they chose |
| `OnByDefault` | `true` | Whether a user who never chose receives it |

**The key is stored** on every choice and every queued mail, so it never changes once mail has gone out in it.
The name and description can change freely. A bad or duplicate key, a reserved key or a missing name is refused
when `AddThargaUserMail` runs, and so is an optional category without an absolute `BaseUrl`, because its mail
must carry a working unsubscribe link.

## What each user receives

A user receives a category when it is required; otherwise when they chose to; otherwise when it is
`OnByDefault`. **Only a choice that differs from the default is stored**, so changing `OnByDefault` changes it for
everyone who never chose. A choice follows the user, not the team: one person has one inbox.

`IUserMailPreferenceService` reads and changes choices:

| Member | |
|---|---|
| `GetCategories()` | Every registered category, built-in ones first |
| `GetChoicesAsync(userKey)` | The user's effective choice for each, saying whether it is their own or the default |
| `ReceivesAsync(userKey, categoryKey)` | Whether they receive one category |
| `SetChoiceAsync(userKey, categoryKey, receives)` | Records a choice for an optional category; refuses turning off a required one |

**Your own, or anyone's with `users:manage`.** The rule is checked in the service, because a scope alone cannot
say "yourself or staff". A caller who is neither is refused with `UnauthorizedAccessException`, and the refusal
is audited. A change is audited under the feature `usermail`, action `choice`, with `usermail.user`,
`usermail.category` and `usermail.receives`.

```razor
@* "Mail I receive", for the signed-in user; UserKey shows another user's to staff holding users:manage *@
<UserMailPreferencesView />
```

## Unsubscribing

A mail in an optional category carries a footer — a sentence saying why it came and an **Unsubscribe** link, in
both the HTML and the text body — and the `List-Unsubscribe` header. When the link is https it also carries
`List-Unsubscribe-Post: List-Unsubscribe=One-Click`, so a mail provider can offer its own one-click button
(RFC 8058). Mail in a required category carries none of this.

`MapThargaMailUnsubscribe()` answers the link at `UnsubscribePath` (default `/mail/unsubscribe`):

- **GET asks and changes nothing**, because mail scanners and link previews fetch every link in a mail. It
  renders a small server-side page with no script, which is not cached, not indexed and passes no referrer.
- **POST turns that one category off for that one user** — from the page's button, or from the mail provider's
  one-click request — and is audited under `usermail`, action `unsubscribe`.

The link's token names one user and one category and is protected by Data Protection under the purpose
`Tharga.Team.UserMail.Unsubscribe`. It does not expire, because a mail is read whenever somebody gets to it.
Raise `UnsubscribeTokenVersion` to invalidate every link already sent.

### What the host must allow

- **The path, anonymously, for GET and POST.** The route is mapped `AllowAnonymous`, and its POST takes no
  antiforgery token, because a one-click request carries none — the token in the link is the credential. A host
  whose own middleware challenges every request without a signed-in user must let `UnsubscribePath` through.
- **One Data Protection key ring, persisted, shared by every instance.** A link made on one instance is refused
  by another that holds different keys, and every link sent dies when keys are lost on a restart. Configure
  `AddDataProtection().PersistKeysTo…()` against a store every instance reaches, with one application name.
- **`BaseUrl` is the address users reach the site at**, absolute http or https; links are built on it.
- **Sign outgoing mail with DKIM on the sending server**, covering the `List-Unsubscribe` headers. Mail
  providers honour one-click unsubscribing only for signed mail.

## Queuing mail from server code

```csharp
public class TrialReminder(IUserMailQueue mail)
{
    public Task<UserMailQueued> QueueAsync(string userKey, CancellationToken cancellationToken)
        => mail.QueueAsync(new UserMail
        {
            RecipientUserKey = userKey,
            Category = UserMailCategories.ServiceKey,
            Subject = "Your trial ends on Friday",
            BodyHtml = "<p>Your trial ends on <b>Friday</b>. <a href=\"https://app.example.com/plans\">Choose a plan</a>.</p>",
            DeduplicationKey = $"trial-ending:{userKey}"
        }, cancellationToken);
}
```

**`IUserMailQueue` is unchecked.** It is marked `[EditorBrowsable(Never)]` and is for server code acting on its
own authority — a job, a handler. Never inject it into a component, a controller or an MCP provider: it would let
whoever is signed in mail anyone. Staff writing to users go through [`IUserMessageService`](user-messages.md),
which is gated.

- **A user is named by key, not by address.** The address is read from the user record when the mail is sent,
  and so is whether they still receive the category, so a change made after queuing is honoured.
- **Refused at once** with `ArgumentException`: an unregistered category, no subject, no body, or more than the
  limits allow.
- **`BodyHtml` is sanitized** by the toolkit's one sanitizer (`SafeHtml`, see
  [Formatting](support-cases.md#formatting)) and the text body is derived from it when `Body` is not given.
- **Files** (`UserMailAttachment`: file name, content type, bytes, and a `ContentId` for an image the HTML shows
  as `cid:`) ride in the queued mail up to `MaxAttachmentBytes` and `MaxAttachmentCount`; their bytes are dropped
  once the mail is sent or given up.
- **`DeduplicationKey`** makes queuing idempotent: a second mail with the same key is not queued, and the result
  names the first with `Duplicate = true`.
- **`ReplyTo`** on one mail overrides the configured reply address.
- **`Kind`** says where the mail came from in the recipient's history; it defaults to `Mail`.
- **`GetAsync(id)`** returns where a queued mail stands (`UserMailDelivery`).

Three more ways in:

| Member | For |
|---|---|
| `QueueBatchAsync(UserMailBatch)` | One mail to many users: content and files stored once, one queued mail per distinct recipient. `GetBatchAsync(id)` returns the counts and up to 100 failures; `ListBatchAsync(id, status, cursor, pageSize)` pages each mail |
| `SendNowAsync(UserMail)` | Records the mail and makes **one attempt at once, never retried**, returning whether it went — for mail somebody is waiting on, as an invitation is. It takes no deduplication key |
| An address alone | `RecipientAddress` (and `RecipientName`) with no user key, for somebody with no account yet. **Accepted only in a required category**, because nobody could opt out of it or unsubscribe |

## Sending

The sender runs on **every instance**. Each mail is claimed atomically for `Lease` (five minutes), counting the
attempt in the same write, so one instance sends it; a sender whose lease ran out cannot record over the one
that took over.

| Status | Meaning |
|---|---|
| `Queued` | Waiting to be sent, or to be tried again |
| `Sent` | The mail server accepted it |
| `Skipped` | The user turned the category off after it was queued — not a failure |
| `Failed` | Given up, with the reason |
| `Bounced` | Accepted, then returned by the receiving server, with its reason — see [Bounces](#bounces) |

- **Only a failure that may pass is retried** — the server refusing or being unreachable, a store that could
  not be read — after `RetryDelay`, doubling up to `MaxRetryDelay`, for at most `MaxAttempts` attempts. A user
  with no address, a category no longer registered, or a mail the outbound policy withholds would fail the same
  way every time, so it fails at once with the reason.
- **Outside production the [outbound mail policy](implementation-guide.md#mail-outside-production) applies**,
  as it does to invitations and support mail: only allowed domains, or the override address.
- **The recipient's address is never logged.** It is kept on the mail, where the history shows it.
- A done mail is removed `ThargaTeamOptions.UserMailRetention` (90 days) after it completed.

## Mail history

Every mail to a user — mail queued on its own, messages, notices and invitations — is in their history, newest
first, with the address it went to and what became of it.

```razor
@* The signed-in user's own; UserKey shows another user's to staff holding users:manage *@
<UserMailHistoryView PageSize="20">
    <TimeTemplate Context="time">@time.ToLocalTime().ToString("g")</TimeTemplate>
</UserMailHistoryView>
```

`UserMailHistoryView` takes `UserKey`, `TimeTemplate` and `PageSize` (20). Behind it is
`IUserMailHistoryService`:

| Member | Who |
|---|---|
| `GetMineAsync(cursor, pageSize)` | The signed-in user; no scope. Empty when nobody is signed in |
| `GetForUserAsync(userKey, cursor, pageSize)` | That user, or staff holding `users:manage` |

Each `UserMailHistoryItem` carries the id, `Kind` (`Mail`, `Message`, `Notice`, `Invitation`), category, subject,
status, reason, the address it went to, when it was queued and when it last changed.

- **What a history holds:** every mail queued for the user's key, and every mail queued to their **current**
  address alone — such as the invitation that brought them in. Another user's mail is never added. An
  invitation sent to an address the user has since changed away from is no longer in it.
- **How far back:** as long as the queue keeps mail, `UserMailRetention`.
- **Staff reading somebody else's history is audited** under `usermail`, action `history`, with `usermail.user`
  and `usermail.count` — how many mails were read, never an address. A refusal is audited too.

A host exposing it over REST calls the service and nothing else:

```csharp
[ApiController]
[Route("api/mail-history")]
public class MailHistoryController(IUserMailHistoryService history) : ControllerBase
{
    [HttpGet("mine")]
    public Task<UserMailHistoryPage> MineAsync(string cursor = null, int pageSize = 20)
        => history.GetMineAsync(cursor, pageSize, HttpContext.RequestAborted);

    [HttpGet("users/{userKey}")]
    public Task<UserMailHistoryPage> ForUserAsync(string userKey, string cursor = null, int pageSize = 20)
        => history.GetForUserAsync(userKey, cursor, pageSize, HttpContext.RequestAborted);
}
```

The service refuses with `UnauthorizedAccessException`; answer it with 403 the way your API does.

## Bounces

**A bounce returns to the address the mail was sent from**, so the toolkit only sees one when that address is a
mailbox it reads. For mail to users that means: **send from the address of a support mailbox** — the primary one
or one added with [`AddMailbox`](support-cases.md#several-mailboxes) — whose `Recipients` filter accepts it.
Sent from any other address, a bounce reaches that mailbox and nothing here, and the mail stays `Sent`.

When the support poller reads a delivery status notification it applies it to the support answer it names; a
bounce that names no support answer is handed to every registered `IMailBounceHandler` in turn, until one
recognises it. `AddThargaUserMail` registers one:

- The mail it names becomes **`Bounced`**, with the receiving server's reason and `BouncedAt`. It is matched by
  `Message-ID`, with or without angle brackets.
- **Once, however often the report is read.** Every instance reads the mailbox, and a restart reads it again; a
  second report changes nothing.
- A batch's counts follow: `UserMailOutcomes.Bounced` counts it, and `Sent` no longer does.
- A report naming mail older than the retention finds nothing and is ignored.
- A bounce nobody recognises is logged at Debug, without addresses.

**Your own mail can use the same seam.** Implement `IMailBounceHandler` and register it beside the others:

```csharp
public sealed class ReceiptBounceHandler(IReceiptStore receipts) : IMailBounceHandler
{
    public async Task<bool> HandleAsync(MailBounce bounce, CancellationToken cancellationToken = default)
        => await receipts.MarkUndeliveredAsync(bounce.OriginalMessageId, bounce.Reason, bounce.ReceivedAt, cancellationToken);
}

builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IMailBounceHandler, ReceiptBounceHandler>());
```

Return `true` when the bounce names a mail you know, whether or not anything changed, so the next handler is not
asked. Make it idempotent, and keep the reason out of your log: it often quotes the recipient's address.

## Invitations

With `AddThargaUserMail` registered, **invitations are sent as mail to users**:

- **Recorded**, in the required `account` category, addressed to the invited address alone, kind `Invitation`.
  It shows in the invited person's history once they have an account with that address, and is marked bounced
  when it comes back.
- **One attempt, at once, never retried.** The inviter is waiting, and is told "sent" or "could not send" exactly
  as before, with the link to copy either way. A mail they were told did not go must not arrive an hour later,
  so there is no background retry: after a passing failure, invite again — re-inviting the same address keeps
  its code.
- **Sent from `UserMailOptions.Mail`**, not from `EmailOptions`. With its sending address or SMTP host unset, an
  invitation is refused with that reason rather than falling back to another configuration.
- **A withheld invitation reports failure.** Outside production, an invitation the outbound policy withholds
  says "could not send", with the reason, instead of claiming it went.
- **The text** is `team.userMail.invitation.subject` (`{0}` the team) and `team.userMail.invitation.body` (`{0}`
  the invited person, `{1}` the team, `{2}` the link), resolved through `ITextResolver`.
- **A sender of your own is kept.** Anything registered as `ITeamEmailSender` other than the toolkit's SMTP
  sender — `AddEmailService<T>()`, for example — stays in place, whichever is registered first.
  `AddThargaEmailInvitations()` becomes unnecessary; registered as well, in either order, it is replaced.

## Text outside components

The footer, the unsubscribe page, the built-in categories and the invitation are written outside any component,
so they resolve through `ITextResolver`. `AddThargaTeamBlazor` registers one that asks your
`IThargaTextProvider`, so a host translating the components translates these too; without one, the English
defaults are used. The keys are `team.userMail.footer.*`, `team.userMail.unsubscribe.*`,
`team.userMail.category.account|service.name|description` and `team.userMail.invitation.*`, all listed in
`ThargaTextKeys.All`, plus your own categories' `userMail.category.{key}.*`.

**Mail follows the server's culture, not the recipient's.** There is no per-recipient language yet.

## Options

`AddThargaUserMail(o => …)` — `UserMailOptions`:

| Option | Default | |
|---|---|---|
| `Mail` | empty | The SMTP server, `FromAddress` and `FromName` mail goes out with; nothing is sent until the address and host are set |
| `ReplyTo` | null | Where replies go; null sends them to the sending address |
| `BaseUrl` | null | The site's absolute address; required once an optional category is registered |
| `UnsubscribePath` | `/mail/unsubscribe` | Where `MapThargaMailUnsubscribe` answers; must start with `/` |
| `UnsubscribeTokenVersion` | 1 | Raise it to invalidate every unsubscribe link already sent |
| `MaxAttempts` | 5 | Attempts at one mail before it is given up |
| `RetryDelay` | 1 minute | The wait after the first failed attempt, doubling after each |
| `MaxRetryDelay` | 1 hour | The longest wait between attempts |
| `Lease` | 5 minutes | How long an instance holds a mail it claimed |
| `PollInterval` | 30 seconds | How often an empty queue is looked at again |
| `BatchSize` | 50 | The most mails one pass sends before looking again at once |
| `MaxAttachmentBytes` | 10 MB | All files on one mail together; at most 15 MB, so a queued mail fits in one document |
| `MaxAttachmentCount` | 10 | Files on one mail |
| `MaxBodyLength` | 200 000 | Characters of HTML and text body together |

`ThargaTeamOptions` (`AddThargaTeamRepository`) names the collections — `UserMailPreference`, `UserMail` and
`UserMailBatch` — and sets `UserMailRetention` (90 days). See the
[Tharga.Team.MongoDB README](https://github.com/Tharga/Team/blob/master/Tharga.Team.MongoDB/README.md).

## Worth knowing

- **One language** — the server's — for every recipient.
- **Mail queued directly to a disabled user is still sent.** Messages and notices reach only active members.
- **A deleted user's choices and queued mail are not purged.** A mail still queued for them fails as having no
  user.
- **A bounce is only seen when the mail went out from a mailbox support reads**, as described above.

## Moving to 4.2

Mail to users is new in 4.2, so nothing changes until you call `AddThargaUserMail`. Coming from 3.x, follow
*Migrating to 4.0* in the [implementation guide](implementation-guide.md) first.

- **With `AddThargaUserMail`, invitations go through it.** Configure `UserMailOptions.Mail` — it is what they are
  sent from now, not `EmailOptions` — and drop `AddThargaEmailInvitations()`. A sender of your own is kept. A
  withheld or failed invitation now says "could not send".
- **`AddThargaTeamBlazor` registers `ITextResolver`** with `TryAdd`; one you registered first is kept.
- **`AddThargaTeamRepository` registers the user mail stores** with `TryAdd`, whether or not mail to users is
  used; one you registered first is kept.
- **`Tharga.Team.Email` takes a private framework reference to ASP.NET Core** (Data Protection and the
  unsubscribe route), and no longer lists `Microsoft.Extensions.Logging.Abstractions` and
  `Microsoft.Extensions.Options` as package references, which the framework provides.
- **`OutboundMail` gained a trailing positional parameter, `UnsubscribeUrl`** — see
  [Email](team-email.md#moving-to-42). Recompile.

### If you built against the 4.2 source before its release

The surface changed while it was being built. A host that linked the source along the way also needs:

- **`UserMail.RecipientUserKey` and `UserMailDelivery.RecipientUserKey` are no longer `required`**, because a
  mail may go to an address alone. `UserMail` gained `RecipientAddress`, `RecipientName` and `Kind`;
  `UserMailBatch` gained `Kind`; `UserMailItem` gained `BatchId`, `RecipientAddress` and `BouncedAt`;
  `UserMailDelivery` gained `BatchId`, `RecipientAddress`, `Kind`, `BouncedAt` and `ChangedAt`.
- **`UserMailStatus.Bounced` is new**, so a switch over statuses handles it, and **`UserMailOutcomes.Sent` no
  longer counts bounced mail** — `UserMailOutcomes.Bounced` does.
- **`IUserMailQueue` gained `ListBatchAsync` and `ListForRecipientAsync`**, and `SendNowAsync` as a default member
  that throws until implemented. Implementers and decorators add them.
- **`IUserMailOutbox.MarkSentAsync` gained `address`**, so a host's own outbox changes its signature. It also
  gained `AddBatchAsync`, `GetBatchContentAsync`, `GetBatchDeliveryAsync`, `ListBatchAsync`,
  `ListForRecipientAsync`, `MarkBouncedAsync` and `AddClaimedAsync` as default members that throw
  `NotSupportedException` until implemented; `MongoUserMailOutbox` takes the batch collection.
- **`IUserMailPreferenceStore.GetChoicesForCategoryAsync` and `IUserMailRecipientDirectory.FindManyAsync` are
  default members** that ask one key at a time; a host's own keeps compiling, and implements them to read once.
- **Invitations go through mail to users** whenever it is registered, as above.
- **`UserMailAudit` gained `HistoryAction` and `Count`.** New text keys: `team.userMail.history.*` (title,
  explanation, empty, showMore, notPermitted, noUser, to, `kind.*`, `status.*`) and
  `team.userMail.invitation.subject|body`.
