# Tharga.Team.Email

E-mail for [Tharga.Team](https://www.nuget.org/packages/Tharga.Team): reading a mailbox over IMAP, sending over SMTP, and the MIME handling both need.

It is the mail layer shared by [Tharga.Team.Support](https://www.nuget.org/packages/Tharga.Team.Support) (support cases by e-mail), **team mailboxes** (an address per team that receives real mail) and **invitation email**. The package exists so that MailKit reaches only consumers who use mail.

The full guide is the [Email, team mailboxes & the postmaster](https://team.tharga.net/articles/team-email.html) article.

## What is in it

- **`IMailClient`** — send a message and fetch new mail from a folder, never throwing: every outcome is a result. The mailbox is only ever read; nothing is marked, moved or deleted.
- **`MailOptions` / `MailServerOptions`** — IMAP and SMTP servers, the sending address, the folder, poll interval and time-outs.
- **`InboundMail`, `OutboundMail`, `MailSendResult`, `MailFetchPosition`, `MailFetchResult`** — the mail contracts. An inbound message carries its envelope recipients (`Delivered-To`, `X-Original-To`, `Envelope-To`, most trustworthy first), so a host can route it by the address it was actually delivered to.
- **`RecipientFilter`** — accept mail only for chosen domains or addresses.

The transport knows nothing about teams, users or audit entries; a test guards that.

## Team mailboxes

```csharp
builder.Services.AddThargaTeamMailbox(o =>
{
    o.Domain = "example.app";
    o.Suffix = "-case";                       // {key}-case@example.app, {key}-case+{item}@example.app
    o.Mail.Imap.Host = "imap.example.app";    // the catch-all mailbox; leave empty to read nothing
});
builder.Services.AddScoped<IInboundMailContentStore, MyBlobContentStore>();
```

- **An opaque, rotatable key per team** — a primary, and a secondary still accepted until it retires.
- **One catch-all mailbox**, each mail routed by the address it was delivered to; what no team owns goes to the **postmaster** (`IPostmasterService`, system scopes), never dropped.
- **Each delivery kept once**, durably, however many instances read; a mail that cannot be kept is read again rather than skipped.
- **The raw message goes to the host's `IInboundMailContentStore`**; the toolkit keeps the envelope. Storage for keys and mail comes from `Tharga.Team.MongoDB`.

## Invitation email

```csharp
builder.Services.AddThargaEmailInvitations();   // settings from o.Email, or pass them here
```

The SMTP invitation sender, through the same transport and the outbound mail policy. **New in 3.27** — it replaces `SmtpTeamEmailSender`, which lived in `Tharga.Team.Service`; `o.Email` alone no longer registers a sender.

## Moving from Tharga.Team.Support

These types used to live in `Tharga.Team.Support.Email`. They are now in the namespace `Tharga.Team.Email`, and two were renamed:

| Before | Now |
|---|---|
| `Tharga.Team.Support.Email.ISupportMailClient` | `Tharga.Team.Email.IMailClient` |
| `Tharga.Team.Support.Email.SupportMailClient` | `Tharga.Team.Email.MailClient` (internal) |
| `Tharga.Team.Support.Email.*` | `Tharga.Team.Email.*` |

`Tharga.Team.Support` references this package, so support configuration (`SupportCaseOptions.Email`) is unchanged apart from the namespace of `MailOptions`.
