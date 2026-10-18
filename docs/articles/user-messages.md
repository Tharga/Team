# Messages to users

Your own staff write one message to chosen users of chosen teams — or of every team — and to users they add by
name, see exactly who it reaches and what each of them will receive before it goes, and read afterwards what became
of each mail. It is sent as [mail to users](user-mail.md), so
every recipient's choices, the retries and the outbound mail policy apply, and each mail is in the recipient's
history.

**Only staff write, and only across teams.** A team never writes to its users by hand; the grant is a system
scope, a decision about the whole product. Mail a team should receive about its own work is an
[automatic notice](user-notices.md). Mail to people who are not users is a
[message to contacts](contact-messages.md).

## Enabling it

```csharp
builder.Services.AddThargaUserMail(o => { /* sending address, categories — see Mail to users */ });

builder.Services.AddThargaUserMessages(o =>
{
    o.StaffMessageCategories = [UserMailCategories.ServiceKey, "news"];   // default: service only
});

builder.AddThargaTeam(o =>
{
    o.ConfigureSystemRoles = roles => roles.Map("Support", SystemMessageScopes.Send);
});
```

- **Call it after `AddThargaUserMail`**, whose categories it checks; it throws otherwise.
- **`StaffMessageCategories`** are the categories staff may write in, default the built-in `service`. Every key
  must be a registered category and at least one must be named, or registration throws. Writing in a required
  category reaches everyone selected; writing in an optional one leaves out whoever turned it off. The built-in
  `account` may be listed too, for what every user must get whatever they chose; with
  [`ServiceRequired`](user-mail.md#categories) false, `service` is optional and respects each user's choice.
- **The team directory and the sent log come from `AddThargaTeamRepository`**, which needs both the user and the
  team repository registered.
- **Grant `messages:send`** (`SystemMessageScopes.Send`) through a system role, as above, or to a system API key.
  It is the only scope, and every operation needs it.

## Who a message reaches

`UserMessageRecipients` has two parts, and **either is enough**.

**The filter** chooses the teams — named in `TeamKeys`, or `AllTeams` — and **within them** any of:

| | Reaches |
|---|---|
| `Roles` | everyone holding any of these tenant roles |
| `AccessLevels` | everyone at any of these access levels |
| `EveryMember` | everyone |

**Users added by key**, `UserKeys`, are reached **whatever the filter says**: with no team chosen, outside the
teams chosen, or beside a filter that reaches nobody. An added user is reached in every team they are an active
member of.

The parts add up, and **each user is reached once**, however many parts and teams name them. A selection naming
neither teams nor users is refused with `ArgumentException`; one with teams but nobody chosen within them, and no
users added, reaches nobody.

**Only active members count:** accepted, not suspended, with a user record that is not disabled, in a team that is
not deleted. Invited members, suspended members, disabled accounts and deleted teams are left out, and so is an
added user who is an active member of no team.

**A member with no access level** holds nothing since 4.0, roles included, so `AccessLevels` and `Roles` never
reach them; `EveryMember` does, and so does adding them in `UserKeys`. The choices offer no level, and no role that
only such members hold.

**One set of rules.** The counts, the list of who is reached and the mail queued on sending are all worked out by
the same code, so the list a person reads before sending is the list that is sent to.

## The service

`IUserMessageService` is registered as a gated system service: a call without `messages:send` is refused before it
reaches the implementation, and the refusal is audited.

| Member | |
|---|---|
| `GetChoicesAsync()` | The teams, the roles and access levels members hold, the categories staff may write in, and the limits |
| `SearchUsersAsync(UserMessageUserSearch)` | Up to `Limit` (default 20, at most 50) active members of the chosen teams, or of every team, whose **name or address** contains the text |
| `PreviewAsync(recipients, category)` | How many users the selection reaches, and how many of them turned the category off — counts only |
| `GetAddresseesAsync(recipients, category, skip, take)` | One page of who the selection reaches, see [The list before sending](#the-list-before-sending) |
| `PreviewMailAsync(UserMessage)` | The mail as a recipient would receive it, see [Preview](#preview) |
| `SendAsync(UserMessage)` | Queues one mail per user and returns at once with the message as the log records it |
| `GetSentAsync(cursor, pageSize)` | The sent log, newest first, each with its outcome counts |
| `GetSentMessageAsync(id)` | One message with its counts, why each mail given up was, and the users it named by key |
| `GetRecipientsAsync(id, status, cursor, pageSize)` | One message's recipients, see [What became of each mail](#what-became-of-each-mail) |

- **Users are found by search, not listed.** A product may have tens of thousands, so the choices carry teams,
  roles and levels and nothing more. A search matches the name and address **on the user record** — not a
  name a team gave its member — reads users first and only their teams, and gives up after a thousand matching
  users of whom too few are in the teams searched. Empty text finds nobody. The compose view searches every team,
  since a user added is reached whatever the filter says.
- **Sending** sanitizes `BodyHtml` and derives the text from it, stores the content and any files **once** however
  many it reaches, and queues one small mail per recipient that names it. It goes out from the host's address
  with its reply address. A selection that reaches nobody, a category staff may not write in, or a message with
  no subject or body or beyond the limits is refused with `ArgumentException`.
- **Limits** are mail to users' own: `MaxAttachmentBytes` (10 MB), `MaxAttachmentCount` (10) and `MaxBodyLength`
  (200 000), returned in `UserMessageChoices.Limits` so a form offers what the service accepts.
- **The message is queued before it is logged**, so a refused message leaves no record, and one that cannot be
  logged is still sent, with its id in the log.

## The list before sending

`GetAddresseesAsync` returns one page of the users the selection reaches, **ordered by name**, with `Total` and
`LeftOut` — the same numbers `PreviewAsync` gives. Each `UserMessageAddressee` carries:

| | |
|---|---|
| `UserKey`, `Name` | who it is |
| `EMail` | the address the mail would go to now, from the user record that sending reads |
| `TeamKeys` | the filter's teams they matched in, and for a user added every team they are active in |
| `ByFilter`, `Added` | how they came in — both when the filter matched someone also added |
| `OptedOut` | they turned the category off and will not receive it; never for a required category |

`take` is at most 100; `skip` past the end returns an empty page with the totals. Reading the list is not audited,
as counting and searching are not — sending is.

## Preview

`PreviewMailAsync` renders the draft as a recipient would receive it, **without sending or storing anything**:
`From` and `ReplyTo` as mail to users sends them, the subject, the HTML **inside the host's
[mail template](user-mail.md#the-mail-template)** with the footer, and the text alternative. For an optional
category the footer carries the unsubscribe line; its link in a preview unsubscribes nobody. The selection is not
read and files are not rendered. It is composed by the very code that composes each mail sent, so the preview and
the mail differ only in the recipient's own unsubscribe link. A category staff may not write in is refused with
`ArgumentException`.

## What became of each mail

`GetRecipientsAsync` pages a message's recipients in the order they were queued, filtered by status if you pass
one. Each `UserMessageRecipient` carries the user, their name, **the address the mail went to** as their record
had it when it was sent, the status (`Queued`, `Sent`, `Skipped`, `Failed`, `Bounced`), the reason, when it last
changed and the `Message-ID`. It returns null for an unknown message.

The counts on a sent message are read when it is read, so they move as mails are sent, skipped, given up or
reported bounced. A bounce is counted in `Bounced` and no longer in `Sent`; see [Bounces](user-mail.md#bounces) for
when one can be seen.

## Auditing

The proxy's per-call trace is off for this service; it writes its own entries, under the feature `messages`,
**with counts and never an address**:

| Action | When | Metadata |
|---|---|---|
| `send` | A message sent, or refused | `messages.id`, `messages.category`, `messages.subject`, `messages.recipients`, `messages.attachments`, `messages.teams` (a count, or `all`) |
| `log` | A page of the log read | `messages.count` |
| `open` | One message read | `messages.id`, `messages.recipients`, `messages.queued`, `.sent`, `.skipped`, `.failed`, `.bounced` |
| `recipients` | A page of recipients read | `messages.id`, `messages.status`, `messages.count` |

## Components

```razor
@attribute [Authorize]

<UserMessageComposeView MessageSent="@(_ => _log.RefreshAsync())" />
<UserMessageLogView @ref="_log" PageSize="20" />

@code {
    private UserMessageLogView _log;
}
```

- **`UserMessageComposeView`** — a framed **filter** (teams or all teams, then everyone, access levels or roles
  within them) and, beneath it, **users added** by typing a name or address, whatever the filter says. Beside them
  — below them on a narrow screen — **the list of recipients**: name, address, teams, whether the filter selected
  them or they were added (an added user has a button that removes them), and a mark on whoever turned the
  category off, with how many the message reaches and how many it leaves out. Then the category, subject,
  formatted body and files, a **Preview** button that opens the mail as the service renders it, and Send, which
  asks first. `MessageSent` is raised with the logged message; `UserSearchLimit` (20) caps one search and
  `RecipientPageSize` (25, at most 100) is how many recipients the list reads at a time, more on request.
- **`MailPreviewDialog`** — what Preview opens: the headers the recipient sees, then the body in an `iframe`
  given the HTML as `srcdoc` and sandboxed with no permissions, so the template's styles stay inside it and nothing
  in it runs. `MailPreviewDialog.ShowAsync(dialogService, title, preview, fileNames, recipientCount)` opens it from a
  host's own page.
- **`UserMessageLogView`** — the sent log as expandable cards, each with its category, how many it reached, how
  many failed or bounced, who sent it and when; a card's details are read when it opens. `TimeTemplate`,
  `PageSize` (20), and `RefreshAsync()` for a host that wants to read it again.
- **`UserMessageDetails`** — what the log shows inside a card: the selection, who sent it, and the recipients
  with a status filter and paging (`Message`, `Choices`, `Text`, `TimeTemplate`, `RecipientPageSize` = 20).

Both views render a note rather than throwing when the reader does not hold `messages:send`; gate the page on the
scope as well, so nobody is shown a page they cannot use. Their texts are `team.userMessage.compose.*`,
`team.userMessage.log.*` and the preview's `team.mailPreview.*`, all in `ThargaTextKeys.All`.

## Over REST

The toolkit ships no endpoints for this. A host calls the service and nothing else:

```csharp
[ApiController]
[Route("api/messages")]
public class MessagesController(IUserMessageService messages) : ControllerBase
{
    [HttpGet("choices")]
    public Task<UserMessageChoices> ChoicesAsync() => messages.GetChoicesAsync(HttpContext.RequestAborted);

    [HttpPost("users/search")]
    public Task<UserMessageCandidate[]> SearchAsync(UserMessageUserSearch search) => messages.SearchUsersAsync(search, HttpContext.RequestAborted);

    [HttpPost("preview")]
    public Task<UserMessagePreview> PreviewAsync([FromBody] UserMessageRecipients recipients, [FromQuery] string category)
        => messages.PreviewAsync(recipients, category, HttpContext.RequestAborted);

    [HttpPost("addressees")]
    public Task<UserMessageAddresseePage> AddresseesAsync([FromBody] UserMessageRecipients recipients, [FromQuery] string category, int skip = 0, int take = 50)
        => messages.GetAddresseesAsync(recipients, category, skip, take, HttpContext.RequestAborted);

    [HttpPost("render")]
    public Task<MailPreview> RenderAsync(UserMessage message) => messages.PreviewMailAsync(message, HttpContext.RequestAborted);

    [HttpPost]
    public Task<SentUserMessage> SendAsync(UserMessage message) => messages.SendAsync(message, HttpContext.RequestAborted);

    [HttpGet]
    public Task<SentUserMessagePage> LogAsync(string cursor = null, int pageSize = 20)
        => messages.GetSentAsync(cursor, pageSize, HttpContext.RequestAborted);

    [HttpGet("{id}")]
    public Task<SentUserMessage> OneAsync(string id) => messages.GetSentMessageAsync(id, HttpContext.RequestAborted);

    [HttpGet("{id}/recipients")]
    public Task<UserMessageRecipientPage> RecipientsAsync(string id, UserMailStatus? status = null, string cursor = null, int pageSize = 50)
        => messages.GetRecipientsAsync(id, status, cursor, pageSize, HttpContext.RequestAborted);
}
```

## Storage

The sent log is the `UserMessage` collection, the shared content the `UserMailBatch` collection, each recipient's
mail the `UserMail` collection; all three are kept for `ThargaTeamOptions.UserMailRetention` (90 days). Their
names are options on `ThargaTeamOptions`.

## Worth knowing

- **The log does not show a message's body**, only its subject, selection and outcomes.
- **One language for every recipient**: the server's.
- **A batch whose mails were only partly queued never completes**, and its files stay until removed by hand.
- **Roles offered in the choices may include roles only disabled users hold.**
- **There are no MCP tools** for messages.

## Moving to 4.2

Messages are new in 4.2: nothing changes until you call `AddThargaUserMessages`.

### If you built against the 4.2 source before its release

- **`UserKeys` are reached whatever the filter says.** They used to be reached only where they were active in one
  of the chosen teams, and a selection of users alone was refused; now users alone are a selection, an added user
  is reached in every team they are active in, and only a selection naming neither teams nor users is refused.
  A REST client that relied on the old narrowing removes users it did not mean to add.
- **`UserMessageChoices.Users` is removed.** Find users with `SearchUsersAsync`; a host REST surface drops users
  from its choices endpoint and adds a search endpoint, as above.
- **`IUserMessageService` gained `GetRecipientsAsync`, `SearchUsersAsync`, `GetAddresseesAsync` and
  `PreviewMailAsync`.** Implementers and decorators add them; a host REST surface adds the two endpoints above to
  offer the list and the preview.
- **`IUserMessageMemberDirectory` gained default members** — `GetActiveMembershipsOfUserAsync`,
  `GetActiveMembershipsOfUsersAsync`, `GetRolesAndLevelsAsync` and `SearchActiveMembersAsync` — that read every team; a host's own directory keeps
  compiling and implements them to avoid that. `UserMessageMember` gained `EMail` and `ScopeOverrides` (fill the
  second so rules by scope see overrides).
- **`UserMessageMember.AccessLevel` is `AccessLevel?`**, following `ITeamMember` in 4.0. A host's own directory
  passes the stored level through, null included, and its `GetRolesAndLevelsAsync` leaves out members with none.
  `UserNoticeAudience.MayReceive` takes `AccessLevel?` too.
- **`UserMessageCandidate` gained `EMail`, `SentUserMessage` gained `ChosenUsers`**, and `UserMessageAudit` gained
  `RecipientsAction`, `Status` and `Bounced`.
- **`UserMessageDetails` lists recipients instead of failures**, and gained `TimeTemplate` and
  `RecipientPageSize`; `UserMessageComposeView` gained `UserSearchLimit` and `RecipientPageSize`, and is laid out
  as a framed filter, users added, and the list of recipients beside them, with a Preview button.
- **`SupportAttachmentPicker` resolves the support service only when no `Limits` is given**, so the compose view
  needs no support registration.
- **Text keys removed:** `team.userMessage.log.failures`, `team.userMessage.compose.chooseUsers|users|noTeams`. **Added:**
  `team.userMessage.log.bounced|recipientList|statusFilter|anyStatus|noRecipients|moreRecipients`,
  `team.userMessage.compose.addUsers|nothingChosen|searchUsers|noUserMatches|removeUser|filter|filterExplanation|addedExplanation|recipientList|byFilter|added|optedOut|optedOutDetail|noRecipients|moreRecipients`
  and `team.mailPreview.open|title|from|replyTo|to|toMany|subject|files|textOnly|frame|close|failed`. The heading
  over the users added and the prompt shown before anything is chosen say something new, so they moved to
  `addUsers` and `nothingChosen` rather than keep a key whose stored translation says the old thing.
