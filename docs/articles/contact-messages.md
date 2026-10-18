# Messages to contacts

Your staff write to **contacts** — people who are not users of the product, such as prospective customers whose
addresses you hold in your own records. **You choose who**, with your own filters over your own data, and hand the
toolkit the list; the toolkit composes the mail, shows a preview, queues one mail per address, sends it, records what
became of each, and **honours every opt-out for good**: each mail carries a link that works for a person with no account,
and an address that used it is never mailed in that category again — or, if they asked, in any.

It is built on [mail to users](user-mail.md) — the same queue, SMTP server, template, retries, outbound mail policy,
bounces and unsubscribe route — and sits beside [messages to users](user-messages.md). The service is in
`Tharga.Team.Email` (namespace `Tharga.Team.Email.Users`), the contracts in `Tharga.Team`, storage in
`Tharga.Team.MongoDB` and the views in `Tharga.Team.Blazor`. New in 4.2.

## Enabling it

```csharp
builder.Services.AddThargaTeamRepository(o => { /* user and team repositories, as for mail to users */ });

builder.Services.AddThargaUserMail(o =>
{
    o.Mail.Smtp.Host = builder.Configuration["UserMail:Smtp:Host"];
    o.Mail.FromAddress = "hello@example.com";
    o.Mail.FromName = "Example";
    o.BaseUrl = "https://app.example.com";                // required: every contact mail carries an unsubscribe link
});

builder.Services.AddThargaContactMessages(o =>
{
    o.SenderLine = "Example Ltd, 1 Example Street, 123 45 Example Town";   // who sends it, in every footer
    o.FromAddress = "sales@example.com";                  // optional; default the address of mail to users
    o.AddCategory(new ContactMailCategory
    {
        Key = "offers",
        Name = "Offers",
        Description = "What we think may be of interest to your organisation."
    });
});

builder.AddThargaTeam(o =>
{
    o.ConfigureSystemRoles = roles => roles.Map("Sales", SystemContactScopes.Send);
});

var app = builder.Build();
app.MapThargaMailUnsubscribe();                           // the same route answers a contact's link
```

- **Call it after `AddThargaUserMail`**, whose queue and settings it uses; it throws otherwise, and when `BaseUrl` is
  unset.
- **At least one category**, and every key well formed, unique and **not the key of a category of mail to users** —
  built-in ones included — so a mail's category says which kind it is. Each is refused when the registration runs.
- **The opt-outs and the log come from `AddThargaTeamRepository`.**
- **Grant `contacts:send`** (`SystemContactScopes.Send`) through a system role, as above, or to a system API key. It is
  the only scope and every operation needs it; holding `messages:send` grants nothing here.
- **Nothing is sent until mail to users can send**: contact mail goes out through its SMTP server, and the sender waits
  until its sending address and SMTP host are set, as for any mail to users.

## Categories

Contact mail goes out in categories of its own, registered with `o.AddCategory(new ContactMailCategory { ... })`:

| Property | Default | |
|---|---|---|
| `Key` | — (required) | 1–64 lowercase letters, digits, dots, dashes or underscores, starting with a letter or digit; not a key of mail to users |
| `Name` | — (required) | The English name, on the unsubscribe page and to staff |
| `Description` | null | One sentence on what the mail is about, on the unsubscribe page |
| `NameKey` / `DescriptionKey` | `contactMail.category.{Key}.name` / `.description` | The text keys a translation is registered under |

**A contact category is always optional and never a user's choice.** There is no `Required`: every contact mail can be
stopped. Contact categories are never listed in a user's mail settings, and their mail is never part of a user's mail
history — not even when the address later becomes a user's. **The key is stored** on every mail and every opt-out, so it
never changes once mail has gone out in it.

## Who a message goes to

You hand over a list of `ContactAddressee`:

| | |
|---|---|
| `Address` | The address to mail (required) |
| `Name` | The name the mail is addressed to |
| `Reference` | **Your own reference for the row**, such as your record's key; it comes back with every outcome, so you can record the activity on your record |
| `Detail` | A short line shown beside the addressee, such as the organisation they belong to |
| `ExcludedReason` | Why you list a row but leave it out — "Already a customer"; shown as it is, never mailed |

The service turns the list into **distinct addresses, in the order first listed**:

- **An address is compared trimmed and ignoring case**, so two rows naming `Kim@Example.com` and ` kim@example.com` are
  one mail, carrying **both rows' references**.
- **A row with a reason never stops another row's address.** An address is mailed when any row naming it has no reason,
  and its references are then those rows'; when every row naming it has a reason, it is left out with the first reason
  and every row's reference.
- **The name and detail** are the first given by a row that is mailed.
- **A value that is not a mail address** is left out, marked as such.
- **An address that opted out** of the category, or of all contact mail, is marked and not mailed.
- **A list holds at most `MaxAddressees` rows** (10 000 by default, at most 50 000); a longer one is refused.

## Opting out

An opt-out belongs to the **address**, not to a person or a user, and is **kept for good**: it is the record that the
person asked not to be mailed, and the only thing that keeps the next list from reaching them. Each is stored once per
address and category — the first is kept — with when and how it was asked for:

| How | `ContactMailSuppressionSource` |
|---|---|
| The unsubscribe link, confirmed on its page | `Link` |
| The mail provider's one-click request (RFC 8058) | `OneClick` |
| Staff, through `SuppressAsync`, as asked some other way | `Staff` |

An opt-out stops **one category**, or **all contact mail** from the product. It is checked three times: when the list is
checked, when the message is sent — an address that opted out is not queued — and again when each mail is sent, so an
address that opts out after the message was queued is skipped rather than mailed.

## The mail

- **From** `FromAddress` and `FromName`, else those of mail to users; **Reply-To** `ReplyTo`, else that of mail to users,
  else the sending address. Use an address a reply reaches.
- **The footer** ends every contact mail, in the text and the HTML, in the template's `{{footer}}` when there is one:
  - *Sent by {sender}.* — `SenderLine`, else the product's name. Mail to people who did not ask for it should say who
    sends it and where they are; put your company's name and postal address there. A line break in it is kept.
  - *You receive this because {product} thinks it may be of interest to you.*
  - *Unsubscribe*, linking to the unsubscribe page with a token naming the address and the category.
- **The headers** `List-Unsubscribe` and, over https, `List-Unsubscribe-Post`, so a mail provider can offer its own
  one-click button.
- **The product's name** is `UserMailOptions.ProductName`, else the contact `FromName`, else that of mail to users, else
  the sending address.
- **Everything else is mail to users'**: the [mail template](user-mail.md#the-mail-template), sanitizing, files and their
  limits, retries, the [outbound mail policy](implementation-guide.md#mail-outside-production) outside production, and
  [bounces](user-mail.md#bounces) — a contact mail the receiving server returns is marked bounced, seen when it was sent
  from the address of a mailbox support reads.

## The unsubscribe page

`MapThargaMailUnsubscribe()` answers a contact's link on the same path as a user's, under the same headers — not cached,
not indexed, no referrer, a policy that runs no script — and with your `PageCss`:

- **GET asks and changes nothing**: "Stop receiving Offers?", "This only turns off Offers. Other mail from Example is not
  affected.", the category's description, **how often it is sent**, and two buttons: **"Stop sending me Offers"** and
  **"Stop all mail from Example"**.
- **POST** opts the address out the way the button said: the form field `scope` is `category` or `all`. A one-click
  request (`List-Unsubscribe=One-Click`), or a post with no choice, stops the category. The page then says "You will no
  longer receive Offers." or "You will no longer receive mail from Example.", and "Your address is kept only to make sure
  this is respected."
- **Nobody here has an account**, so the page lists no categories and offers no sign-in, whoever is signed in.
- **The token** names the address and the category, under the same Data Protection purpose and version as a user's;
  raising `UnsubscribeTokenVersion` invalidates both kinds. A user's link never opts an address out, and a contact's never
  touches a user's choices. Without `AddThargaContactMessages`, a contact's link is not valid.
- **Audited** under `contacts`, action `unsubscribe`, with the category or `all` and how, never the address — once,
  however often it is asked.

**How often** is counted as for mail to users, over contact mail per distinct address: the contact mails sent or bounced
in the category over the past year — or what the outbox keeps — per address that received any, in the same words.
`Tharga.Team.MongoDB` counts it from one index.

## The service

`IContactMessageService` is registered as a gated system service: a call without `contacts:send` is refused before it
reaches the implementation, and the refusal is audited.

| Member | |
|---|---|
| `GetChoicesAsync()` | The contact categories, the limits on body and files (mail to users' own), and `MaxAddressees` |
| `GetAddresseesAsync(addressees, category)` | The list checked: each distinct address with `Status` — `Reached`, `OptedOut` (`OptedOutOfAll` when of every category) or `LeftOut` (`InvalidAddress`, or your `ExcludedReason`) — its name, detail and references, and the counts |
| `PreviewMailAsync(ContactMessage)` | The mail as a contact receives it — sender, subject, the body in the template with the footer — without sending or storing anything; the list is not read and the link opts nobody out |
| `SendAsync(ContactMessage)` | Queues one mail per address reached, stores the content and files once, and returns the message with **every listed address and where it stands, with your references** |
| `GetSentAsync(cursor, pageSize)` | The log, newest first, with live counts |
| `GetSentMessageAsync(id)` | One message with its counts |
| `GetRecipientsAsync(id, status, cursor, pageSize)` | One message's addresses: those queued, in the order they were, then those never queued, each with what became of it and your references |
| `SuppressAsync(address, category)` | Staff opt an address out of one category, or with no category of all contact mail; true when it was new |

- **Sending** refuses with `ArgumentException` a category that is not a contact one, a list longer than allowed, a list
  of which nothing can be mailed, and a message with no subject or body or beyond the limits.
- **The message is queued before it is logged**, so a refused message leaves no record, and one that cannot be logged is
  still sent, with its id in the log.
- **A sent message** carries `RecipientCount` (queued), `OptedOutCount`, `LeftOutCount`, `AttachmentCount`, who sent it
  and when, and `Outcomes` — how many queued mails stand where; one whose address opted out after it was queued counts as
  skipped.

## What became of each address

`ContactMessageRecipient` carries the address, name, detail and **references**, and a `Status`:

| Status | |
|---|---|
| `Queued` | Waiting to be sent, or to be tried again (`Reason` says why the last attempt did not succeed) |
| `Sent` | Accepted by the mail server, with its `MessageId` |
| `OptedOut` | Not sent: the address had opted out when the message was sent, or did before its mail went; `OptedOutOfAll` when of every category |
| `Failed` | Given up, with the `Reason` |
| `Bounced` | Returned by the receiving server, with its `Reason` |
| `LeftOut` | Never queued: `InvalidAddress`, or your reason in `Reason` |

`ChangedAt` is when it last changed, or when the message was sent for an address never queued. A status filter reads
only that status; `OptedOut` finds both kinds.

## Auditing

The proxy's per-call trace is off for this service; it writes its own entries under the feature `contacts`
(`ContactMessageAudit`), **with counts and never an address**:

| Action | When | Metadata |
|---|---|---|
| `send` | A message sent, or refused | `contacts.id`, `.category`, `.subject`, `.recipients`, `.optedOut`, `.leftOut`, `.attachments` |
| `log` | A page of the log read | `contacts.count` |
| `open` | One message read | `contacts.id`, `.recipients`, `.queued`, `.sent`, `.skipped`, `.failed`, `.bounced` |
| `recipients` | A page of addresses read | `contacts.id`, `.status`, `.count` |
| `unsubscribe` | An address opted out by its link | `contacts.category` (or `all`), `contacts.source` |
| `suppress` | Staff opted an address out | `contacts.category` (or `all`), `contacts.source` = `Staff` |

## Components

```razor
@attribute [Authorize]

@* Your own filter over your own records, then the toolkit's view of what that list becomes *@
<MyContactFilter @bind-Selection="_addressees" />
<ContactMessageComposeView Addressees="@_addressees" MessageSent="@RecordAsync" />
<ContactMessageLogView @ref="_log" />

@code {
    private IReadOnlyList<ContactAddressee> _addressees = [];
    private ContactMessageLogView _log;

    private async Task RecordAsync(SentContactMessage sent)
    {
        foreach (var recipient in sent.Recipients)
        foreach (var reference in recipient.References)
            await Crm.LogActivityAsync(reference, sent.Subject, recipient.Status);

        await _log.RefreshAsync();
    }
}
```

- **`ContactMessageComposeView`** — placed beside your own filter. `Addressees` is your list, checked again whenever you
  hand over another one. It shows each distinct address with its name, address and detail, marks who opted out (with
  what, in its title) and the rows left out with your reason or as no address, counts them, and shows `RecipientPageSize`
  (50) rows at a time with more on request. Then the category, subject, formatted body and files, a **Preview** that opens
  [`MailPreviewDialog`](user-messages.md#components), and Send, which asks first with the count. `MessageSent` is raised
  with the message as `SendAsync` returned it — every listed address, where it stands and its references.
- **`ContactMessageLogView`** — the log as expandable cards: subject, category, how many it reached, failed and bounced,
  who sent it and when. Opened, a card shows how many stand where — sent, waiting, opted out, failed, bounced, left out —
  and every address with its status, time and reason, filtered by status and read a page at a time. `TimeTemplate`,
  `PageSize` (20), `RecipientPageSize` (20) and `RefreshAsync()`.

Both render a note rather than throwing when the reader does not hold `contacts:send`; gate the page on the scope as well.

## Over REST

The toolkit ships no endpoints for this. A host calls the service and nothing else:

```csharp
[ApiController]
[Route("api/contact-messages")]
public class ContactMessagesController(IContactMessageService contacts) : ControllerBase
{
    [HttpGet("choices")]
    public Task<ContactMessageChoices> ChoicesAsync() => contacts.GetChoicesAsync(HttpContext.RequestAborted);

    [HttpPost("addressees")]
    public Task<ContactAddresseeList> AddresseesAsync([FromBody] ContactAddressee[] addressees, [FromQuery] string category)
        => contacts.GetAddresseesAsync(addressees, category, HttpContext.RequestAborted);

    [HttpPost("render")]
    public Task<MailPreview> RenderAsync(ContactMessage message) => contacts.PreviewMailAsync(message, HttpContext.RequestAborted);

    [HttpPost]
    public Task<SentContactMessage> SendAsync(ContactMessage message) => contacts.SendAsync(message, HttpContext.RequestAborted);

    [HttpGet]
    public Task<SentContactMessagePage> LogAsync(string cursor = null, int pageSize = 20)
        => contacts.GetSentAsync(cursor, pageSize, HttpContext.RequestAborted);

    [HttpGet("{id}")]
    public Task<SentContactMessage> OneAsync(string id) => contacts.GetSentMessageAsync(id, HttpContext.RequestAborted);

    [HttpGet("{id}/recipients")]
    public Task<ContactMessageRecipientPage> RecipientsAsync(string id, ContactRecipientStatus? status = null, string cursor = null, int pageSize = 50)
        => contacts.GetRecipientsAsync(id, status, cursor, pageSize, HttpContext.RequestAborted);

    [HttpPost("suppressions")]
    public Task<bool> SuppressAsync([FromQuery] string address, [FromQuery] string category = null)
        => contacts.SuppressAsync(address, category, HttpContext.RequestAborted);
}
```

The service refuses with `UnauthorizedAccessException` and `ArgumentException`; answer them with 403 and 400.

## Storage

| Collection | `ThargaTeamOptions` | Holds | Kept |
|---|---|---|---|
| `ContactMailSuppression` | `ContactMailSuppressionCollectionName` | One document per address and category opted out (`*` for all), unique on `Address_Category` | For good |
| `ContactMessage` | `ContactMessageCollectionName` | The log: each message with every distinct address listed and how it was checked; a page of the log is read without them | `UserMailRetention` after it was sent |
| `UserMail`, `UserMailBatch` | as for mail to users | One mail per address queued, `Kind` `Contact`, naming no user; the content once | `UserMailRetention` after it is done |

`UserMail` gains the index `Kind_Status_CompletedAt_Category_RecipientAddress`, which answers how often each contact
category is sent without reading a mail. Each store is registered with `TryAdd`: register your own
`IContactMailSuppressionStore` or `IContactMessageLog` first to keep them elsewhere.

## Text

The footer and the page are written outside any component and resolve through `ITextResolver`, which
`AddThargaTeamBlazor` answers from your `IThargaTextProvider`; the views resolve through it directly. Every key is in
`ThargaTextKeys.All`.

**The mail and the page** (`ContactMailText`, and `ContactMailTexts` in `Tharga.Team`):

| Key | English |
|---|---|
| `team.contactMail.footer.sender` | Sent by {0}. — `{0}` your sender line, else the product |
| `team.contactMail.footer.reason` | You receive this because {0} thinks it may be of interest to you. — `{0}` the product |
| `team.contactMail.unsubscribe.title` | Unsubscribe |
| `team.contactMail.unsubscribe.stopAll` | Stop all mail from {0} |
| `team.contactMail.unsubscribe.doneAll` | You will no longer receive mail from {0}. |
| `team.contactMail.unsubscribe.kept` | Your address is kept only to make sure this is respected. |

They share with mail to users `team.userMail.footer.unsubscribe`, `team.userMail.unsubscribe.question`, `.onlyThis`,
`.stop`, `.done`, `.invalid`, `.invalidDetail` and `team.userMail.frequency.*`, and your categories add
`contactMail.category.{key}.name` and `.description`.

**`ContactMessageComposeView`** (`ContactMessageComposeViewText`, `team.contactMessage.compose.*`): `notPermitted` You may
not write to contacts. · `explanation` Write to the people listed. Each address receives it once, as a mail with a link to
stop such mail. · `recipientList` Recipients · `noAddressees` Nobody is listed. · `reaches` Reaches {0} addresses. ·
`optedOutCount` {0} have opted out and will not receive it. · `leftOutCount` {0} are left out. · `optedOut` Opted out ·
`optedOutDetail` Has opted out of {0}. · `optedOutOfAllDetail` Has opted out of all mail to contacts. · `leftOut` Left out:
{0} · `invalidAddress` Left out: not a mail address · `moreRecipients` Show more · `listFailed` The list cannot be checked.
{0} · `category` Category · `subject` Subject · `body` Message · `send` Send · `confirmTitle` Send to contacts ·
`confirmBody` Send "{0}" to {1} addresses? · `confirmSend` Send · `confirmCancel` Cancel · `sent` The message is on its way
to {0} addresses. · `sendFailed` The message was not sent. {0}

**`ContactMessageLogView`** (`ContactMessageLogViewText`, `team.contactMessage.log.*`): `notPermitted` You may not read the
messages sent to contacts. · `empty` No message has been sent to contacts. · `showMore` Show more · `sentBy` Sent by {0} ·
`recipients` {0} recipients · `attachments` {0} files attached · `queued` Waiting · `sent` Sent · `optedOut` Opted out ·
`failed` Failed · `bounced` Bounced · `leftOut` Left out · `ofEveryCategory` Of all mail to contacts · `invalidAddress` Not
a mail address · `recipientList` Recipients · `statusFilter` Show recipients · `anyStatus` All · `noRecipients` No
recipient matches. · `moreRecipients` Show more recipients

The preview dialog's `team.mailPreview.*` are shared with messages to users.

## Options

`AddThargaContactMessages(o => …)` — `ContactMessageOptions`:

| Option | Default | |
|---|---|---|
| `AddCategory(…)` / `Categories` | none; at least one required | The contact categories |
| `FromAddress` | null | The sending address; null uses that of mail to users |
| `FromName` | null | The sending name; null uses that of mail to users |
| `ReplyTo` | null | Where replies go; null uses that of mail to users, else the sending address |
| `SenderLine` | null | Who sends the mail, as the footer says — your name and postal address; null names the product |
| `MaxAddressees` | 10 000 | The most rows one list may hold; at most 50 000, so a message's log fits in one stored document |

From `UserMailOptions`: `BaseUrl` (required), `UnsubscribePath`, `UnsubscribeTokenVersion`, `ProductName`, `PageCss`, the
SMTP server in `Mail`, the file and body limits and the retry settings.

## Worth knowing

- **You decide who may be mailed.** The toolkit honours opt-outs and says who sends the mail; whether you may write to
  an address at all — consent, a legitimate interest, the rules where the person is — is yours to decide when you list it.
- **An opt-out is kept for good**, with the address, so that it holds; it is not purged with the mail.
- **A bounce is only seen when the mail went out from a mailbox support reads**, as for mail to users; with a separate
  `FromAddress`, make that a support mailbox too, or bounces reach it and nothing here.
- **One language for every recipient**: the server's.
- **Contact mail is never part of a user's mail history**, even when a user holds the address.
- **The log keeps each message's list** for the retention, addresses and your references included.
- **There are no MCP tools** for contact mail.

## Moving to 4.2

Contact mail is new in 4.2: nothing changes until you call `AddThargaContactMessages`. Around it, mail to users changed
in ways a host with its own stores notices — see [Moving to 4.2](user-mail.md#moving-to-42) in Mail to users: the
`UserMail` kind `Contact`, `UserMailBatch.RecipientAddresses`, the outbox's `CountSentToContactsByCategoryAsync`, a new
index, and a history that leaves contact mail out.
