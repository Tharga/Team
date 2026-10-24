# Mail to users

The toolkit can mail the people who use your product: in **categories** each user chooses from, with an
**unsubscribe link** in every mail they could have turned off, through a **persisted queue** that whichever
instance claims a mail sends. Three things are built on it:

- **[Messages to users](user-messages.md)** — your own staff writing to chosen users of chosen teams.
- **[Automatic notices](user-notices.md)** — mail raised by something that happened, which each user tunes.
- **Invitations** — once mail to users is registered, invitations go through it too.
- **[Messages to contacts](contact-messages.md)** — your staff writing to people who are not users, through the same
  queue, with categories and opt-outs of their own.

Every one of them is recorded, so a user's **mail history** shows what was sent to them and what became of it,
and a mail the receiving server returns is marked **bounced**. Every one of them can be framed in the host's own
**[mail template](#the-mail-template)**, as support answers are.

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

Every mail goes out in a category, and a category is what a user turns on or off. Two are built in. A **required**
category cannot be turned off, so no choice is stored for it and its mail carries no unsubscribe link:

| Key | Name | For |
|---|---|---|
| `account` (`UserMailCategories.AccountKey`) | Account and security | The user's own account, access and its security; invitations go here. Always required |
| `service` (`UserMailCategories.ServiceKey`) | Service messages | What everyone needs to know, such as planned maintenance or disruptions. Required unless the host says otherwise |

They exist so that mail the toolkit sends itself always has a category every host has. Their keys are reserved.

**Making service messages optional.** A host that wants exactly one category nobody can turn off — account, which
it may describe as account, sign-in, security and essential system information — sets `o.ServiceRequired = false`.
Service messages are then on until a user turns them off, carry an unsubscribe link like any optional category, and
`BaseUrl` becomes required. Their text keys stay the same; describe them through your translations.

**Why a required category is required.** Wherever one is shown to a user — the preferences view and the
unsubscribe page — it reads "Always sent: part of the terms of use" (`team.userMail.alwaysSent`). Set
`o.TermsUrl` to a site path or an absolute address and the line links to your terms; without it, it says the same
with no link.

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
| `GetTermsUrl()` | `TermsUrl`, which a required category links to; null when unset |
| `GetCategoryFrequenciesAsync()` | How often each category was sent — see [How often](#how-often) |

**Your own, or anyone's with `users:manage`.** The rule is checked in the service, because a scope alone cannot
say "yourself or staff". A caller who is neither is refused with `UnauthorizedAccessException`, and the refusal
is audited. A change is audited under the feature `usermail`, action `choice`, with `usermail.user`,
`usermail.category` and `usermail.receives`. The categories, the terms and the frequencies name nobody, so any
caller may read them.

```razor
@* "Mail I receive", for the signed-in user; UserKey shows another user's to staff holding users:manage *@
<UserMailPreferencesView />

@* In a tab of the host's own, which names it: no card and no title *@
<UserMailPreferencesView Framed="false" />
```

Each category shows its name, its description, how often it is sent, and for a required one the always-sent line.
`Framed` (default `true`) draws the view's own card and title; `UserNoticePreferencesView` and
`UserMailHistoryView` take it too, so a host can place the three in tabs such as "Mail", "Notices" and "History".

### How often

Users deciding whether to turn a category off want to know how often it comes, so the preferences view and the
unsubscribe page say it in words under each category: *None in the past year*, *Fewer than one a year*,
*About N a year*, *About N a month* or *About N a week*.

- **Counted from the outbox** in one aggregation: the mails in each category **sent or bounced** — a bounced mail
  was sent — to a user, never one queued, skipped or given up, and never mail to an address alone such as an
  invitation. The figure is the average per user who received at least one, per month.
- **Over the past year, or what the outbox keeps.** Done mail is removed after `UserMailRetention` (90 days by
  default), so by default the past 90 days are counted and scaled; raise the retention to a year to count a whole
  year. With less than a year counted, none sent reads *None sent recently*.
- **The words:** under one a year, *fewer than one a year*; under about twelve a year, *about N a year*; under
  4.5 a month, *about N a month*; more, *about N a week* — N rounded, never below one. `UserMailFrequencyPhrase`
  decides it, in one place, and `UserMailCategoryFrequency.Phrase` gives it for a category.
- **Kept for ten minutes** on each instance. It is read-only statistics, so instances may differ by a few minutes.
- **One index.** `Tharga.Team.MongoDB` answers it from `Status_CompletedAt_Category_RecipientUserKey` on
  `UserMail` without reading a mail. A host's own `IUserMailOutbox` implements `CountSentByCategoryAsync` (and
  `Retention`); until it does, no figures are shown.

## Unsubscribing

A mail in an optional category carries a footer — a sentence saying why it came and an **Unsubscribe** link, in
both the HTML and the text body, in the template's `{{footer}}` when there is one — and the `List-Unsubscribe`
header. When the link is https it also carries
`List-Unsubscribe-Post: List-Unsubscribe=One-Click`, so a mail provider can offer its own one-click button
(RFC 8058). Mail in a required category carries none of this.

`MapThargaMailUnsubscribe()` answers the link at `UnsubscribePath` (default `/mail/unsubscribe`):

- **GET asks and changes nothing**, because mail scanners and link previews fetch every link in a mail. It
  renders a small server-side page with no script, which is not cached, not indexed and passes no referrer.
- **POST turns that one category off for that one user** — from the page's button, or from the mail provider's
  one-click request — and is audited under `usermail`, action `unsubscribe`.

The page says exactly what it does:

- **GET**: "Stop receiving News?", "This only turns off News. Other mail from Example is not affected.", the
  category's description, how often it is sent, and the button "Stop sending me News".
- **POST**: "You will no longer receive News." and how to turn it back on.
- **Below, on both**: for a request **signed in as the very user the link is for**, every category with its
  state — on, off, or always sent with the terms line — and how often it is sent, then "Change your mail settings"
  linking to `PreferencesPath`. For anyone else — nobody signed in, or somebody else — only "Sign in to see all the
  mail you get and choose what you want", linking to `SignInPath` with `returnUrl` set to `PreferencesPath`. Either
  link is left out when its option is unset.

**The token alone reveals only its own category.** Who is signed in is decided by the user key `IUserService`
resolves for the request's principal, compared with the key in the token; another user's choices are never shown.
The product is `ProductName`, else `Mail.FromName`, else the sending address. `PageCss` adds the host's style rules
after the page's own in its one inline style element; the page's policy still loads nothing from elsewhere, so no
images or fonts by address. Its paragraphs carry the classes `scope`, `description`, `frequency` and
`turn-back-on`, the list is `section.mail` with one `li` per category (`data-category`, `data-state`), and the
links are `p.settings` and `p.sign-in`. The page's language is the request's culture.

The link's token names one user and one category and is protected by Data Protection under the purpose
`Tharga.Team.UserMail.Unsubscribe`. It does not expire, because a mail is read whenever somebody gets to it.
Raise `UnsubscribeTokenVersion` to invalidate every link already sent.

### What the host must allow

- **The path, anonymously, for GET and POST.** The route is mapped `AllowAnonymous`, and its POST takes no
  antiforgery token, because a one-click request carries none — the token in the link is the credential. A host
  whose own middleware challenges every request without a signed-in user must let `UnsubscribePath` through.
- **Authentication before the route**, for the signed-in list: `UseAuthentication()` runs for the path as for any
  other, so a signed-in visitor's principal reaches it. Without it, everyone is offered sign-in.
- **A sign-in that honours `returnUrl`**, for the sign-in link to lead back to the mail settings. The toolkit's own
  `/login` (`UseThargaAuth`) does, for a path on the site — see
  [Where signing in returns](implementation-guide.md#where-signing-in-returns); a sign-in of your own must too,
  and should accept a local path only.
- **One Data Protection key ring, persisted, shared by every instance.** A link made on one instance is refused
  by another that holds different keys, and every link sent dies when keys are lost on a restart. Configure
  `AddDataProtection().PersistKeysTo…()` against a store every instance reaches, with one application name.
- **`BaseUrl` is the address users reach the site at**, absolute http or https; links are built on it.
- **Sign outgoing mail with DKIM on the sending server**, covering the `List-Unsubscribe` headers. Mail
  providers honour one-click unsubscribing only for signed mail.

## The mail template

One HTML frame for **every HTML mail the toolkit sends** — support answers, and mail to users: messages, notices
and invitations — and for the previews staff open before sending. Register it once, in any order with the modules
that send:

```csharp
builder.Services.AddThargaMailTemplate(o =>
{
    o.Html = File.ReadAllText("mail-template.html");
    o.SupportHtml = File.ReadAllText("support-template.html");   // optional: support answers alone
});
```

**Support answers can have a frame of their own.** An answer is a person writing back rather than the product
announcing, so `SupportHtml` frames support answers and their previews, with the same placeholders and
`{{footer}}` always empty; unset, they use `Html` as before. It is checked like `Html`. See
[Support answers](support-cases.md#how-an-answer-speaks-for-support).

```html
<!doctype html>
<html>
<head><meta charset="utf-8"><title>{{subject}}</title></head>
<body style="margin:0;padding:24px;background:#f3f4f6;font-family:Arial,sans-serif;">
  <div style="display:none;max-height:0;overflow:hidden;">{{preheader}}</div>
  <div style="max-width:640px;margin:0 auto;background:#ffffff;padding:24px;">
    <img src="https://app.example.com/mail-logo.png" alt="Example" height="32">
    {{content}}
    <div style="margin-top:24px;font-size:12px;color:#6b7280;">{{footer}}</div>
  </div>
</body>
</html>
```

| Placeholder | Becomes |
|---|---|
| `{{content}}` (required) | The mail's own HTML, sanitized; a mail with text alone gets its text escaped into paragraphs, with line breaks kept and web addresses as links |
| `{{subject}}` | The subject line, escaped, as it reads in production — without the prefix a redirect adds outside it |
| `{{footer}}` | For an optional category, why the mail came and the unsubscribe link; otherwise nothing. Without this placeholder the footer follows the content after a rule |
| `{{preheader}}` | The start of the text on one line, escaped, about 140 characters — the line an inbox shows after the subject |

- **The template is trusted host markup** and is used as written; only what goes into it is sanitized. Inline its
  styles, as most mail clients ignore a `<style>` block, and leave scripts out — mail clients drop them.
- **Placeholders are filled in one pass**, ignoring case and spaces inside the braces, so a placeholder written in
  a mail's own content stays text. Any other `{{name}}` is left as it is.
- **With a template every mail has an HTML part.** A mail written as text alone — an invitation, a plain support
  answer — gets one made from its text; its text alternative is unchanged.
- **Without a template nothing changes:** a formatted mail's HTML goes as it is, with the footer after a rule, and
  a text-only mail stays text.
- **A template without `{{content}}` is refused** when `AddThargaMailTemplate` runs. `services.Configure<MailTemplateOptions>(…)`
  sets it too, and is checked when the first mail is composed.
- **Invitations sent without mail to users** (`AddThargaEmailInvitations`) are framed as well.

**One renderer.** Sending and previewing go through the same composition — the footer, the template, the
sanitizing — so a preview shows the mail that is sent. See [Preview](user-messages.md#preview) for messages and
[Where the answer goes, and its preview](support-cases.md#where-the-answer-goes-and-its-preview) for support.

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

`UserMailHistoryView` takes `UserKey`, `TimeTemplate`, `PageSize` (20) and `Framed` (`true`; false leaves the
card and title to the host). Behind it is `IUserMailHistoryService`:

| Member | Who |
|---|---|
| `GetMineAsync(cursor, pageSize)` | The signed-in user; no scope. Empty when nobody is signed in |
| `GetForUserAsync(userKey, cursor, pageSize)` | That user, or staff holding `users:manage` |

Each `UserMailHistoryItem` carries the id, `Kind` (`Mail`, `Message`, `Notice`, `Invitation`), category, subject,
status, reason, the address it went to, when it was queued and when it last changed.

- **What a history holds:** every mail queued for the user's key, and every mail queued to their **current**
  address alone — such as the invitation that brought them in. Another user's mail is never added. An
  invitation sent to an address the user has since changed away from is no longer in it. Mail to
  [contacts](contact-messages.md) is never in it, even when the user holds the address it went to.
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

## The mail log

A user's history shows what was sent to them. **The mail log is the same list across everyone**: every mail the
application sent or tried to send, to users and to contacts, for staff holding `users:manage`.

```razor
@* On a staff page; the service refuses anyone without users:manage, and the view says so *@
<UserMailLogView PageSize="50" />
```

It lists newest first, with when it was queued, who it was for (the user's name, or the invited person's or
contact's name, and the address it went to), its kind, category, subject, **result** with the reason, and the team
it is about. **The mail itself is never shown or returned**: no body and no files. A row for a user has a button
that opens that user's own history. `UserMailLogView` takes `PageSize` (50), `TimeTemplate` and `Framed`, as the
history view does, and filters on kind, result, category, a day or a range of days (each day is the reader's own),
a user key or an address, and a team key.

Behind it is `IUserMailHistoryService.ListAllAsync(filter, cursor, pageSize)`, which takes a `UserMailFilter`; every
part given narrows the list and none lists everything:

| `UserMailFilter` | |
|---|---|
| `Kind`, `Status`, `Category` | Only mail of that kind, standing there, in that category |
| `From`, `To` | Queued at or after `From` and before `To`, in UTC |
| `RecipientUserKey`, `RecipientAddress` | Only mail for that user, or addressed to that address (compared trimmed and in lower case; a mail to a user is addressed to the address on their record when it is sent) |
| `TeamKey` | Only mail about that team, where the mail records one |

- **Only staff read it, whoever they ask about.** A caller without `users:manage` gets `UnauthorizedAccessException`,
  and a user who is in the log still cannot read it.
- **Audited** under `usermail`, action `log`, with `usermail.count` — how many mails were read, never an address or
  the filter. A refusal is audited too.
- **Contact mail is in the log**, unlike a user's history, as its own kind (*Contact*).
- **The team** is on a mail when it is about one team: an invitation is about the team it invites to, a staff message
  about the one team its recipients were chosen within, and a notice, or a summary of notices, about the team it
  names. A message chosen within several teams or all of them, and a summary of notices from several teams, belong
  to none. Set `UserMail.TeamKey` or `UserMailBatch.TeamKey` on mail you queue yourself. Mail queued before this
  version records no team.

A host exposing it over REST calls the service and nothing else:

```csharp
[HttpGet("log")]
public Task<UserMailHistoryPage> LogAsync([FromQuery] UserMailFilter filter, string cursor = null, int pageSize = 50)
    => history.ListAllAsync(filter, cursor, pageSize, HttpContext.RequestAborted);
```

**A send the queue refuses is in the log too**, as *Not sent* with the reason, although the caller still gets the
exception: an unknown category, a mail with no subject or body, files over the limits, an optional category to an
address alone, an address a mail cannot go to, or a mail of kind `Contact` queued here.

- It records what the caller named — the user or address, category, subject, kind and team — **never the body or
  files**, each cut short.
- **Once a day per cause.** The same refusal of the same mail on the same day is recorded once, so a host bug in a
  loop cannot fill the collection; the next day it is recorded again.
- **A refused batch is one record**, with its kind, category, subject and team and no recipient.
- **It is not in the user's own history**, only in the log: the reason is for staff, not for the person it was meant for.
- It expires like any mail that is done. **Failing to record it never changes what the caller hears.**

**Support answers are not in the log.** An answer goes out through the support mailbox's own channel rather than
this queue, and its delivery is recorded on the case message it belongs to, so that is where to look for it.

A host with its own `IUserMailOutbox` implements `ListAsync(filter, cursor, pageSize)` to support the log; until it
does, `ListAllAsync` throws `NotSupportedException`. It implements `AddRefusedAsync(mail, reason, deduplicationKey, at)`
to keep refusals, and without it they are simply not recorded. The built-in MongoDB outbox adds the index `TeamKey_Id`,
created at start-up like the others.

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
  the invited person, `{1}` the team, `{2}` the link), resolved through `ITextResolver`. It is text alone; with a
  [mail template](#the-mail-template) it also goes as HTML, its link clickable.
- **A sender of your own is kept.** Anything registered as `ITeamEmailSender` other than the toolkit's SMTP
  sender — `AddEmailService<T>()`, for example — stays in place, whichever is registered first.
  `AddThargaEmailInvitations()` becomes unnecessary; registered as well, in either order, it is replaced.

## Text outside components

The footer, the unsubscribe page, the built-in categories and the invitation are written outside any component,
so they resolve through `ITextResolver`. `AddThargaTeamBlazor` registers one that asks your
`IThargaTextProvider`, so a host translating the components translates these too; without one, the English
defaults are used. The keys are `team.userMail.footer.*`, `team.userMail.unsubscribe.*`,
`team.userMail.category.account|service.name|description`, `team.userMail.invitation.*`,
`team.userMail.alwaysSent` and `team.userMail.frequency.*` — the last two shared with the preferences view — all
listed in `ThargaTextKeys.All` (`UserMailText.All`), plus your own categories' `userMail.category.{key}.*`. Contact
mail adds `team.contactMail.*` (`ContactMailText.All`); see [Text](contact-messages.md#text).

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
| `ServiceRequired` | `true` | False makes service messages a category each user may turn off, on until they do |
| `TermsUrl` | null | A site path or absolute address of the terms of use, which a required category's always-sent line links to |
| `ProductName` | null | The product the unsubscribe page names; null uses `Mail.FromName`, then the sending address |
| `PreferencesPath` | null | The host's mail settings, a site path or absolute address; the unsubscribe page links there and sign-in returns there |
| `SignInPath` | null | Where a user signs in; the unsubscribe page offers it, with `returnUrl`, to anyone not signed in as the link's user |
| `PageCss` | null | Style rules added to the unsubscribe page's own; may not contain `</` |
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
- **How often a category is sent is what was kept**: with the default 90-day retention, the past 90 days scaled to
  a year, and a category sent quarterly may read as none sent recently.

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
- **The mail template is new** (`AddThargaMailTemplate`, `MailTemplateOptions`). Nothing is framed until you set
  one; then support answers, mail to users and invitations all carry it, and text-only mail gains an HTML part.
- **`AddThargaTeamRepository` also registers the contact mail stores** — the opt-outs and the log — with `TryAdd`,
  whether or not [contact mail](contact-messages.md) is used.
- **`/login` returns to `returnUrl` when it is a path on the site**, where it always returned to the start page, so the
  unsubscribe page's sign-in link leads back to the mail settings — see
  [Where signing in returns](implementation-guide.md#where-signing-in-returns). Anything else still returns home.

### If you built against the 4.2 source before its release

The surface changed while it was being built. A host that linked the source along the way also needs:

- **The source now carries 4.0 and 4.1**, which it did not while it was built: an unset access level grants
  nothing and `ITeamMember.AccessLevel` is `AccessLevel?`, an invitation must name a level, `ITeamEmailSender` has
  only `SendInviteAsync(TeamInviteMail)`, and API keys can be personal. Work through *Migrating to 4.0* in the
  [implementation guide](implementation-guide.md) as a 3.x host would.
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
- **The footer goes into the template's `{{footer}}`** once a template is set; without one, the HTML footer is as
  it was. Mail composed with a template is the same for every kind of mail, previews included.
- **Service messages can be made optional** (`ServiceRequired`, default `true`, so nothing changes until you set
  it), and `TermsUrl`, `ProductName`, `PreferencesPath`, `SignInPath` and `PageCss` are new options.
- **The unsubscribe page changed**: it names the product and says it turns off only that category, how often it
  comes, the button reads "Stop sending me {0}", and below it lists every category for the signed-in user the link
  is for or offers sign-in to anyone else. A host matching on its old markup or wording updates.
- **Mail to contacts is new** ([Messages to contacts](contact-messages.md)) and shares the queue, so:
  - `UserMailKind` gained **`Contact`** (a switch over kinds handles it), and **`UserMailBatch` gained
    `RecipientAddresses`** (`UserMailBatchAddress`), the addresses a batch goes to alone. `IUserMailQueue` refuses
    both — contact mail is queued only by `IContactMessageService` — and a host's own `IUserMailOutbox` stores
    `RecipientAddresses` in `AddBatchAsync` (one mail per address, after the users, counted in the batch's recipients).
  - **`IUserMailOutbox` gained `CountSentToContactsByCategoryAsync`**, a default member that throws
    `NotSupportedException`, which shows no contact figures; `IUserMailCollection` gained the same, and `UserMail` the
    index `Kind_Status_CompletedAt_Category_RecipientAddress`.
  - **A user's history leaves contact mail out**: `ListForRecipientAsync` no longer returns mail of kind `Contact` to
    the address, and a host's own outbox does the same.
  - **The unsubscribe route answers a contact's link too**, when `AddThargaContactMessages` is registered; the token may
    name an address instead of a user. Tokens already sent keep working.
  - The queue's refusals for a mail with no subject or body or too many files now say "A mail…" rather than "A mail
    to a user…".
- **`IUserMailPreferenceService` gained `GetTermsUrl()` and `GetCategoryFrequenciesAsync()`**, default members
  returning null and nothing, so a host's own implementation or decorator keeps compiling and forwards them to show
  the terms link and the frequencies.
- **`IUserMailOutbox` gained `CountSentByCategoryAsync` and `Retention`**, default members (the first throws
  `NotSupportedException`, which shows no figures). `IUserMailCollection` (`Tharga.Team.MongoDB`) gained the same
  two as default members, and the `UserMail` collection a new index, `Status_CompletedAt_Category_RecipientUserKey`,
  created as its other indexes are.
- **`UserMailPreferencesView`, `UserNoticePreferencesView` and `UserMailHistoryView` take `Framed`** (default
  `true`, as before).
- **Text keys removed:** `team.userMail.preferences.required` (the required line is now
  `team.userMail.alwaysSent`, shared with the unsubscribe page), `team.userMail.unsubscribe.confirm` (the button
  is now `team.userMail.unsubscribe.stop`) and `team.userMail.unsubscribe.doneDetail` (now
  `team.userMail.unsubscribe.turnBackOn`); `UserMailPreferencesViewText.Required`, `UserMailText.PageConfirm`,
  `UserMailText.PageDoneDetail`, `UserMailTexts.PageConfirm*` and `UserMailTexts.PageDoneDetail*` went with them.
- **Text keys added:** `team.userMail.alwaysSent`, `team.userMail.frequency.*` (none, noneRecently,
  underOneAYear, perYear, perMonth, perWeek) and `team.userMail.unsubscribe.*` (onlyThis, stop, turnBackOn,
  yourMail, on, off, settings, signIn).
