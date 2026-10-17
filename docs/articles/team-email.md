# Email: team mailboxes, the postmaster and invitations

`Tharga.Team.Email` is the mail layer of the toolkit: reading a mailbox over IMAP, sending over SMTP, and the
MIME handling both need. Three things use it:

- **Support cases by email**, in `Tharga.Team.Support`, which now depends on this package.
- **Team mailboxes** — an address per team that receives real mail, described below.
- **Invitation email**, which moved here from `Tharga.Team.Service` in 3.29.

The package exists so that MailKit reaches only hosts that use mail. `Tharga.Team.Blazor` never references it:
the views below depend on contracts in `Tharga.Team` alone.

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

`ITeamMailNotifier` raises `MailUpdated` for every mail received, handled or routed. It is a singleton raised in
this process only, outside any circuit: a component marshals with `InvokeAsync`, and a host with several instances
relays it over its own backplane. It carries no content — a handler reads the mail back through the service, where
access is checked.

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
| `mailbox:postmaster:read` | System | List and open mail no team owns |
| `mailbox:postmaster:manage` | System | Route such a mail to a team |

Three team scopes, because three different people use a mailbox: someone handling mail every day should not
rotate the address every sender knows. Their access levels are the host's — `ReadAccessLevel` and
`HandleAccessLevel` default to User, `ManageAccessLevel` to Administrator, and `null` registers a scope grant-only,
held only through a role.

## The postmaster

Mail no team owns. `IPostmasterService` is a system service: no team scope reaches it, because unrouted mail may
hold any team's material.

- **The listing is deliberately thin** — sender, subject, time and reason — enough to decide what to open.
- **Opening is the audited act.** `OpenAsync` and `GetContentAsync` are recorded every time.
- **`RouteAsync(mailId, toTeamKey)` delivers a mail to a team**, marked `Rerouted`. It refuses a team that does not
  exist, and a mail someone else already routed returns `null`. The parameter is not `teamKey` because the team is
  where the mail goes, not who is asking: routing is authorized by the system scope alone.
- **Retention:** mail the postmaster holds longer than `PostmasterRetention` (default 90 days) is deleted with its
  content, and every deletion is recorded.

When a team is removed, its mailbox goes with it: its mail, the content kept for it, and its keys.

## Views

Both are in `Tharga.Team.Blazor` and take a `TimeTemplate` to render moments the host's way.

- **`PostmasterView`** — the postmaster's list with a count, the envelope of an opened mail, and routing to a team
  chosen from a list (when the reader may list teams) or typed.
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
    o.FromAddress = "no-reply@example.com";
});
```

It sends through the same transport as everything else, obeys the outbound mail policy, and throws when the server
refuses, so the invite dialog can tell the inviter. A sender the host registered itself — `AddEmailService<T>()` —
still wins, whichever is registered first.

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
