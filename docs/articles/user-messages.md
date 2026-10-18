# Messages to users

Your own staff write one message to chosen users of chosen teams — or of every team — see how many it reaches
before it goes, and read afterwards what became of each mail. It is sent as [mail to users](user-mail.md), so
every recipient's choices, the retries and the outbound mail policy apply, and each mail is in the recipient's
history.

**Only staff write, and only across teams.** A team never writes to its users by hand; the grant is a system
scope, a decision about the whole product. Mail a team should receive about its own work is an
[automatic notice](user-notices.md).

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
  category reaches everyone selected; writing in an optional one leaves out whoever turned it off.
- **The team directory and the sent log come from `AddThargaTeamRepository`**, which needs both the user and the
  team repository registered.
- **Grant `messages:send`** (`SystemMessageScopes.Send`) through a system role, as above, or to a system API key.
  It is the only scope, and every operation needs it.

## Who a message reaches

`UserMessageRecipients` chooses **the teams** — named, or `AllTeams` — and **within them** any of:

| | Reaches |
|---|---|
| `UserKeys` | those users, where they are an active member of one of the teams |
| `Roles` | everyone holding any of these tenant roles |
| `AccessLevels` | everyone at any of these access levels |
| `EveryMember` | everyone |

The parts add up, and **each user is reached once**, however many parts and teams name them.

**Only active members count:** accepted, not suspended, with a user record that is not disabled, in a team that is
not deleted. Invited members, suspended members, disabled accounts and deleted teams are left out.

## The service

`IUserMessageService` is registered as a gated system service: a call without `messages:send` is refused before it
reaches the implementation, and the refusal is audited.

| Member | |
|---|---|
| `GetChoicesAsync()` | The teams, the roles and access levels members hold, the categories staff may write in, and the limits |
| `SearchUsersAsync(UserMessageUserSearch)` | Up to `Limit` (default 20, at most 50) active members of the chosen teams, or of every team, whose **name or address** contains the text |
| `PreviewAsync(recipients, category)` | How many users the selection reaches, and how many of them turned the category off — counts only, never who |
| `SendAsync(UserMessage)` | Queues one mail per user and returns at once with the message as the log records it |
| `GetSentAsync(cursor, pageSize)` | The sent log, newest first, each with its outcome counts |
| `GetSentMessageAsync(id)` | One message with its counts, why each mail given up was, and the users it named by key |
| `GetRecipientsAsync(id, status, cursor, pageSize)` | One message's recipients, see [What became of each mail](#what-became-of-each-mail) |

- **Users are found by search, not listed.** A product may have tens of thousands, so the choices carry teams,
  roles and levels and nothing more. A search matches the name and address **on the user record** — not a
  name a team gave its member — reads users first and only their teams, and gives up after a thousand matching
  users of whom too few are in the chosen teams. Empty text finds nobody.
- **Sending** sanitizes `BodyHtml` and derives the text from it, stores the content and any files **once** however
  many it reaches, and queues one small mail per recipient that names it. It goes out from the host's address
  with its reply address. A selection that reaches nobody, a category staff may not write in, or a message with
  no subject or body or beyond the limits is refused with `ArgumentException`.
- **Limits** are mail to users' own: `MaxAttachmentBytes` (10 MB), `MaxAttachmentCount` (10) and `MaxBodyLength`
  (200 000), returned in `UserMessageChoices.Limits` so a form offers what the service accepts.
- **The message is queued before it is logged**, so a refused message leaves no record, and one that cannot be
  logged is still sent, with its id in the log.

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

- **`UserMessageComposeView`** — choose teams or all teams, then who within them (everyone, access levels, roles,
  users found by typing a name or address), the category, subject, formatted body and files; it shows how many
  the message reaches and how many turned the category off, and asks before sending. `MessageSent` is raised
  with the logged message; `UserSearchLimit` (20) caps one search.
- **`UserMessageLogView`** — the sent log as expandable cards, each with its category, how many it reached, how
  many failed or bounced, who sent it and when; a card's details are read when it opens. `TimeTemplate`,
  `PageSize` (20), and `RefreshAsync()` for a host that wants to read it again.
- **`UserMessageDetails`** — what the log shows inside a card: the selection, who sent it, and the recipients
  with a status filter and paging (`Message`, `Choices`, `Text`, `TimeTemplate`, `RecipientPageSize` = 20).

Both views render a note rather than throwing when the reader does not hold `messages:send`; gate the page on the
scope as well, so nobody is shown a page they cannot use. Their texts are `team.userMessage.compose.*` and
`team.userMessage.log.*`, all in `ThargaTextKeys.All`.

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

- **`UserMessageChoices.Users` is removed.** Find users with `SearchUsersAsync`; a host REST surface drops users
  from its choices endpoint and adds a search endpoint, as above.
- **`IUserMessageService` gained `GetRecipientsAsync` and `SearchUsersAsync`.** Implementers and decorators add
  them.
- **`IUserMessageMemberDirectory` gained default members** — `GetActiveMembershipsOfUserAsync`,
  `GetRolesAndLevelsAsync` and `SearchActiveMembersAsync` — that read every team; a host's own directory keeps
  compiling and implements them to avoid that. `UserMessageMember` gained `EMail` and `ScopeOverrides` (fill the
  second so rules by scope see overrides).
- **`UserMessageCandidate` gained `EMail`, `SentUserMessage` gained `ChosenUsers`**, and `UserMessageAudit` gained
  `RecipientsAction`, `Status` and `Bounced`.
- **`UserMessageDetails` lists recipients instead of failures**, and gained `TimeTemplate` and
  `RecipientPageSize`; `UserMessageComposeView` gained `UserSearchLimit`.
- **`SupportAttachmentPicker` resolves the support service only when no `Limits` is given**, so the compose view
  needs no support registration.
- **Text keys removed:** `team.userMessage.log.failures`, `team.userMessage.compose.chooseUsers`. **Added:**
  `team.userMessage.log.bounced|recipientList|statusFilter|anyStatus|noRecipients|moreRecipients` and
  `team.userMessage.compose.searchUsers|noUserMatches|removeUser`.
