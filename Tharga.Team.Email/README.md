# Tharga.Team.Email

E-mail for [Tharga.Team](https://www.nuget.org/packages/Tharga.Team): reading a mailbox over IMAP, sending over SMTP, and the MIME handling both need.

It is the mail layer shared by [Tharga.Team.Support](https://www.nuget.org/packages/Tharga.Team.Support) (support cases by e-mail) and any other mailbox a host keeps, such as an organisation's own inbox. The package exists so that MailKit reaches only consumers who use mail.

## What is in it

- **`IMailClient`** — send a message and fetch new mail from a folder, never throwing: every outcome is a result. The mailbox is only ever read; nothing is marked, moved or deleted.
- **`MailOptions` / `MailServerOptions`** — IMAP and SMTP servers, the sending address, the folder, poll interval and time-outs.
- **`InboundMail`, `OutboundMail`, `MailSendResult`, `MailFetchPosition`, `MailFetchResult`** — the mail contracts. An inbound message carries its envelope recipients (`Delivered-To`, `X-Original-To`, `Envelope-To`, most trustworthy first), so a host can route it by the address it was actually delivered to.
- **`RecipientFilter`** — accept mail only for chosen domains or addresses.

The transport knows nothing about teams, users or audit entries; a test guards that.

## Moving from Tharga.Team.Support

These types used to live in `Tharga.Team.Support.Email`. They are now in the namespace `Tharga.Team.Email`, and two were renamed:

| Before | Now |
|---|---|
| `Tharga.Team.Support.Email.ISupportMailClient` | `Tharga.Team.Email.IMailClient` |
| `Tharga.Team.Support.Email.SupportMailClient` | `Tharga.Team.Email.MailClient` (internal) |
| `Tharga.Team.Support.Email.*` | `Tharga.Team.Email.*` |

`Tharga.Team.Support` references this package, so support configuration (`SupportCaseOptions.Email`) is unchanged apart from the namespace of `MailOptions`.
