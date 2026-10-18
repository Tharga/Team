# Automatic notices

Something happens — somebody joins a team, a key is about to expire, a case gets an answer — and the people it
concerns are told by mail. The host keeps a **catalogue** of the notices it can raise; each says who may
receive it, which [mail category](user-mail.md#categories) it belongs to and how one reads. Server code
**raises** an occurrence and returns at once; in the background, on whichever instance claims it, the
recipients are found, their choices applied and the mail queued.

Each user chooses per notice on their profile whether they receive it and how — **at once**, a burst collected
into one mail, or in **one daily summary** — over their team's default, which a team administrator sets.

## Enabling it

```csharp
builder.Services.AddThargaUserMail(o =>
{
    // sending address, BaseUrl …
    o.AddCategory(new UserMailCategory { Key = "work", Name = "Notices about my work" });
});

builder.Services.AddThargaUserNotices(o =>
{
    o.DigestTime = new TimeOnly(7, 0);
    o.TimeZoneId = "Europe/Stockholm";

    o.Add(new UserNoticeType
    {
        Key = "member.joined",
        Name = "Someone joins a team I administer",
        Description = "When an invited person accepts and becomes a member.",
        Category = "work",
        Audience = UserNoticeAudience.AtLeast(AccessLevel.Administrator),
        Deliveries = [UserNoticeDelivery.AtOnce, UserNoticeDelivery.Digest],
        Subject = "{name} joined {subject}",
        Body = "<p><b>{name}</b> accepted the invitation and is now a member of {subject}.</p>",
        Placeholders = ["name"]
    });
});
```

**Call it after `AddThargaUserMail`**: notices are mail to users, so they go out through its queue, from its
address, in its categories, and their links are built on its `BaseUrl`, which must be set once any notice is in
the catalogue. Storage and the member directory come from `AddThargaTeamRepository` with both the user and the
team repository registered.

The catalogue is checked when the call runs, and anything wrong throws: a bad or repeated key, a missing name or
subject, a category that is not registered, no `Audience`, no delivery allowed, a `DefaultDelivery` it does not
allow, a malformed template, or a template using a placeholder it does not declare.

## The catalogue

| `UserNoticeType` | Default | |
|---|---|---|
| `Key` | — (required) | Stable: stored on every choice, team default and occurrence. 1–64 lowercase letters, digits, dots, dashes or underscores |
| `Name`, `Description` | — / null | Shown to users, in English |
| `Category` | — (required) | A registered mail category; turning it off stops the notice too |
| `Audience` | — (required) | Who may receive it, see below |
| `OnByDefault` | `true` | Whether a user receives it when neither they nor their team chose |
| `Deliveries` | `[AtOnce]` | What a user may choose between |
| `DefaultDelivery` | `AtOnce` | One of `Deliveries` |
| `Subject` | — (required) | The subject line template |
| `Body` | null | The HTML body template; null sends the subject and the link |
| `Placeholders` | `[]` | The names of the values every occurrence supplies |
| `NameKey`, `DescriptionKey`, `SubjectKey`, `BodyKey` | `userNotice.{Key}.name` … `.body` | The text keys a translation is registered under |

**Templates** use `{name}` for a declared placeholder and `{subject}` for the occurrence's subject text, which
every notice has; `{{` and `}}` write a brace. Values are HTML-encoded in the body, which is HTML, and inserted
as they are into the subject line.

### Who may receive it

| `UserNoticeAudience` | Admits, among the active members of the occurrence's team |
|---|---|
| `WithScope("case:read")` | members holding the team scope, through their level, roles or overrides |
| `WithRole("Registrar")` | members holding the tenant role |
| `AtLeast(AccessLevel.Administrator)` | members at that level or a more privileged one (`Custom` admits only itself) |
| `EveryMember` | everyone |
| `NamedOnly` | nobody by rule — only those the raiser names |
| `ResolvedBy((occurrence, services, ct) => …)` | the user keys a callback of yours returns per occurrence |

**Recipients the raiser names are added** to whoever the rule admits, and every recipient must be an **active
member of the occurrence's team**: accepted, not suspended, with a user record that is not disabled. A rule is
evaluated per team, against what each member holds in that team.

**A member with no access level** holds nothing since 4.0: no scope, not even through a role or an override, as
their own claims carry none. `WithScope`, `WithRole` and `AtLeast` never admit them; `EveryMember` does, and so does
naming them or returning them from `ResolvedBy`. Their profile offers only the notices that could still reach them.

A notice whose recipients are named or resolved per occurrence is offered on the profile of every active member,
because any of them could be the one it reaches.

## Raising one

```csharp
public class MembershipEvents(IUserNoticeRaiser notices)
{
    public Task<string> JoinedAsync(string teamKey, string teamName, string memberName, string memberKey, CancellationToken cancellationToken)
        => notices.RaiseAsync(new UserNoticeOccurrence
        {
            NoticeKey = "member.joined",
            TeamKey = teamKey,
            SubjectId = memberKey,                    // what it is about
            SubjectText = teamName,                   // {subject}
            Values = new Dictionary<string, string> { ["name"] = memberName },
            LinkPath = "/team"                        // made absolute on BaseUrl
        }, cancellationToken);
}
```

**`IUserNoticeRaiser` is unchecked**, marked `[EditorBrowsable(Never)]`, for server code acting on its own
authority. Never inject it into a component, a controller or an MCP provider.

- **Raising is cheap and never waits on mail.** The occurrence is checked and stored, and its id returned;
  everything else happens in the background, surviving a restart.
- **What is refused** (`ArgumentException`): a notice not in the catalogue, a missing team or subject id, a value
  missing for a declared placeholder or given for an undeclared one, a link that is not a site path starting with
  one `/`, ids over 200 characters, values over 1 000, more than 1 000 named recipients.
- **The same occurrence reaches the same person once.** An occurrence is identified by notice, `SubjectId` and
  `Discriminator`; raising it again — on this instance or another — reaches only recipients it has not reached
  before. `Discriminator` tells two occurrences about one thing apart (`"14-days-before"`, `"7-days-before"`);
  null means once per subject. It is remembered for `UserNoticeRetention` (90 days).
- **`Recipients`** names users by key, added to whoever the rule admits.
- **`GetAsync(id)`** returns where an occurrence stands: `Pending`, `Distributed` (with how many it reached) or
  `Failed` (with why).
- **Raising is not audited** — there is no caller to attribute it to. Choices and team defaults are.

## How it is delivered

For each user it reaches, in this order of precedence: **the user's own choice, else their team's default, else
the catalogue's**. Then:

- **At once** — mailed `BurstWindow` (two minutes) after the raise; anything more of the same notice to the same
  user within the window joins that mail, so a burst is one mail.
- **Daily summary** — listed in one mail per user and mail category at `DigestTime` (07:00) in `TimeZoneId`
  (UTC). A second summary the same day keeps what arrives for the next.
- **At most `MaxItemsPerMail` (100)** notices in one mail; the rest wait for the next.
- **A notice the user turns off while it waits is not sent.** Turning the notice's mail category off stops it at
  send time, as for any user mail.
- **Every instance shares the work** under leases; the mail started for a stream is recorded on it, so an
  instance taking over queues the same mail once.

The mail goes through the [mail to users](user-mail.md) queue, so its sending address, reply address, retries,
outbound policy and history apply. When the notice's category is optional, the mail carries its unsubscribe
link.

## Choices and team defaults

**A user's choice is global**, not per team: one person has one inbox, and the team shows in the mail. Until they
choose, each occurrence uses its own team's default. Only a choice that differs from a default in force for them
is stored.

`IUserNoticePreferenceService` — the user's own, or anyone's with `users:manage`:

| Member | |
|---|---|
| `GetChoicesAsync(userKey)` | Every notice the user may receive in any of their teams, in catalogue order |
| `SetChoiceAsync(userKey, noticeKey, enabled, delivery)` | Records a choice; a delivery the notice does not allow is refused |

Each `UserNoticeChoice` says whether it is `Enabled` and how it is delivered, whether each is the user's own
(`EnabledChosen`, `DeliveryChosen`), `VariesByTeam` when the user has not chosen and their teams' defaults
disagree, and `CategoryReceives` — false when the user turned the notice's category off, so nothing is sent
whatever is chosen here.

`IUserNoticeTeamService` — a team service, gated by the team scope **`notices:manage`** (`NoticeScopes.Manage`):

| Member | |
|---|---|
| `GetTeamDefaultsAsync(teamKey)` | The team's default for every notice, saying which the team set |
| `SetTeamDefaultAsync(teamKey, noticeKey, enabled, delivery)` | Sets what members who have not chosen receive, and how |

`notices:manage` is granted to Administrator and above by default. Set `ManageAccessLevel` to change that, or to
`null` to make it grant-only, held through a role or an override. Only deviations from the catalogue are stored,
and purging a team removes its defaults.

Both are audited under the feature `notices`: action `choice` for a user's choice, `default` for a team default
(with the team key), carrying `notices.user`, `notices.notice`, `notices.enabled` and `notices.delivery`.
Refusals are audited too.

## Components

```razor
@* On the profile: which notices I receive, and how. UserKey shows another user's to staff holding users:manage *@
<UserNoticePreferencesView />

@* In team settings: what the team's members receive until they choose. TeamKey defaults to the selected team *@
<UserNoticeTeamDefaultsView />
```

The team view needs `notices:manage` on the team and renders a note without it. Texts are
`team.userNotice.preferences.*`, `team.userNotice.teamDefaults.*`, `team.userNotice.delivery.*` and, in the mail,
`team.userNotice.mail.*` (open, severalSubject, digestSubject, digestIntro) — all in `ThargaTextKeys.All` — plus
your catalogue's own `userNotice.{key}.*`.

## Over REST

```csharp
[ApiController]
[Route("api/notices")]
public class NoticesController(IUserNoticePreferenceService choices, IUserNoticeTeamService teams) : ControllerBase
{
    [HttpGet("users/{userKey}")]
    public Task<UserNoticeChoice[]> ChoicesAsync(string userKey) => choices.GetChoicesAsync(userKey, HttpContext.RequestAborted);

    [HttpPut("users/{userKey}/{noticeKey}")]
    public Task<UserNoticeChoice> ChooseAsync(string userKey, string noticeKey, bool enabled, UserNoticeDelivery delivery)
        => choices.SetChoiceAsync(userKey, noticeKey, enabled, delivery, HttpContext.RequestAborted);

    [HttpGet("teams/{teamKey}")]
    public Task<UserNoticeTeamDefault[]> DefaultsAsync(string teamKey) => teams.GetTeamDefaultsAsync(teamKey, HttpContext.RequestAborted);

    [HttpPut("teams/{teamKey}/{noticeKey}")]
    public Task<UserNoticeTeamDefault> SetDefaultAsync(string teamKey, string noticeKey, bool enabled, UserNoticeDelivery delivery)
        => teams.SetTeamDefaultAsync(teamKey, noticeKey, enabled, delivery, HttpContext.RequestAborted);
}
```

`UserNoticeType.Audience` holds a callback for `ResolvedBy`, so it is never serialized.

## Options

`AddThargaUserNotices(o => …)` — `UserNoticeOptions`:

| Option | Default | |
|---|---|---|
| `DigestTime` | 07:00 | When the daily summary goes, in `TimeZoneId` |
| `TimeZoneId` | `UTC` | A system time zone id; an unknown one throws at registration |
| `BurstWindow` | 2 minutes | How long a notice delivered at once waits for others of the same notice |
| `MaxItemsPerMail` | 100 | Notices in one mail |
| `PollInterval` | 15 seconds | How often occurrences and due mail are looked for when none were found |
| `Lease` | 5 minutes | How long an instance holds what it claimed |
| `BatchSize` | 50 | Occurrences and mails one pass handles of each |
| `MaxAttempts` | 5 | Attempts at finding an occurrence's recipients before it is given up |
| `ManageAccessLevel` | `Administrator` | The least privileged level granted `notices:manage`; null makes it grant-only |

`ThargaTeamOptions` names the five collections — `UserNotice`, `UserNoticeItem`, `UserNoticeStream`,
`UserNoticePreference`, `UserNoticeTeamDefault` — and sets `UserNoticeRetention` (90 days), which is also how long
an occurrence raised again is recognised.

## Worth knowing

- **One language** for every recipient, the server's.
- **A waiting notice is not re-checked against membership**, so somebody who leaves a team may still find it in
  that day's summary.
- **A changed delivery applies to later notices**; what already waits keeps its course.
- **The daily summary does not name the team** a notice came from.

## Moving to 4.2

Notices are new in 4.2: nothing changes until you call `AddThargaUserNotices`. `AddThargaTeamRepository`
registers their stores either way, with `TryAdd`.

### If you built against the 4.2 source before its release

- **A member with no access level is admitted only by `EveryMember`, by name or by `ResolvedBy`**, since 4.0 made
  an unset level grant nothing; see *Who may receive it*. `UserNoticeAudience.MayReceive` takes `AccessLevel?`, as
  `UserMessageMember.AccessLevel` now is (see [Messages to users](user-messages.md#moving-to-42)).
