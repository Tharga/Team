# Tharga.Team.Email

E-mail for [Tharga.Team](https://www.nuget.org/packages/Tharga.Team): reading a mailbox over IMAP, sending over SMTP, and the MIME handling both need.

It is the mail layer shared by [Tharga.Team.Support](https://www.nuget.org/packages/Tharga.Team.Support) (support cases by e-mail), **team mailboxes** (an address per team that receives real mail), **invitation email** and **mail to users** (categories, unsubscribe links, a persisted queue, staff messages and automatic notices). The package exists so that MailKit reaches only consumers who use mail.

The guides are [Email, team mailboxes & the postmaster](https://team.tharga.net/articles/team-email.html), [Mail to users](https://team.tharga.net/articles/user-mail.html), [Messages to users](https://team.tharga.net/articles/user-messages.html), [Messages to contacts](https://team.tharga.net/articles/contact-messages.html) and [Automatic notices](https://team.tharga.net/articles/user-notices.html).

## What is in it

- **`IMailClient`** — send a message and fetch new mail from a folder, never throwing: every outcome is a result. The mailbox is only ever read; nothing is marked, moved or deleted.
- **`MailOptions` / `MailServerOptions`** — IMAP and SMTP servers, the sending address, the folder, poll interval and time-outs, `FirstReadSince` for a mailbox that already holds mail, and the file limits `MaxAttachmentBytes` (25 MB), `MaxAttachmentTotalBytes` (50 MB) and `MaxAttachmentCount` (20).
- **`InboundMail`, `OutboundMail`, `MailAttachment`, `InboundAttachment`, `MailSendResult`, `MailFetchPosition`, `MailFetchResult`** — the mail contracts. An inbound message carries its envelope recipients (`Delivered-To`, `X-Original-To`, `Envelope-To`, most trustworthy first), so a host can route it by the address it was actually delivered to.
  - **Files both ways.** `InboundMail.Attachments` holds a mail's attachments, the images its body shows (with their Content-ID) and forwarded mail as `.eml`, with file names sanitized. `OutboundMail.Attachments` sends files; a `MailAttachment` with a `ContentId` is sent beside the HTML that shows it.
  - **HTML both ways.** `InboundMail.BodyHtml` is the HTML **exactly as the sender wrote it — not sanitized**; sanitize it before showing it. `OutboundMail.BodyHtml` is sent as `multipart/alternative` beside the text, and must be sanitized by the caller.
  - **Bounces.** A delivery status notification fills `InboundMail.BouncedMessageId` and `BounceReason`.
  - **Unsubscribing.** `OutboundMail.UnsubscribeUrl` is sent as `List-Unsubscribe`, with `List-Unsubscribe-Post` for one-click unsubscribing (RFC 8058) when it is https.
  - **A mailbox that could not be read** says why in `MailFetchResult.Error`, never with the password.
- **DKIM sender check.** Every mail read is verified over DNS: `InboundMail.SenderVerified` is true when a signature verifies, signs `From` and the whole body, and its domain is the `From` domain or a parent of it, which `SenderDomain` names. Keys are cached ten minutes (a miss one minute) and a lookup is cut off after five seconds; anything that fails means not verified. **It vouches for the domain's mail system, not for a person**: a large provider signs only its own account holder's mail, a company domain whatever its servers relay.
- **`HtmlText`** — the plain-text rendering of HTML the toolkit uses: paragraphs kept, list items as `- ` lines, quotes as `> ` lines, links as `text (address)`.
- **`IMailBounceHandler` / `MailBounce`** — receives a bounce the support mail poller read that names no support answer; register your own to mark mail you sent as undelivered. Make it idempotent, and do not log the reason, which often quotes an address.
- **`RecipientFilter`** — accept mail only for chosen domains or addresses.
- **`AddThargaMailTemplate(o => o.Html = …)` / `MailTemplateOptions`** — the host's HTML frame around every HTML mail the toolkit sends (support answers, mail to users, invitations) and the previews of them, with `SupportHtml` an optional frame of its own for support answers, with `{{content}}` (required), `{{subject}}`, `{{footer}}` and `{{preheader}}`. The template is trusted markup; what goes into it is sanitized. With a template, a mail with text alone also gets an HTML part. See [the mail template](https://team.tharga.net/articles/user-mail.html#the-mail-template).

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
- **One catch-all mailbox**, each mail routed by the address it was delivered to; what no team owns goes to the **postmaster** (`IPostmasterService`, system scopes), never dropped — routed to a team, or archived without routing and restored, and read with its files through `GetBodyAsync` and `OpenAttachmentAsync`.
- **Each delivery kept once**, durably, however many instances read; a mail that cannot be kept is read again rather than skipped.
- **The raw message goes to the host's `IInboundMailContentStore`**; the toolkit keeps the envelope. Storage for keys and mail comes from `Tharga.Team.MongoDB`.

## Mail to users

```csharp
builder.Services.AddThargaUserMail(o =>
{
    o.Mail.Smtp.Host = "smtp.example.com";
    o.Mail.FromAddress = "hello@example.com";             // an address a reply reaches
    o.BaseUrl = "https://app.example.com";                // unsubscribe links are built on it
    o.AddCategory(new UserMailCategory { Key = "news", Name = "Product news" });
});
builder.Services.AddThargaUserMessages();                  // staff write to users: the messages:send system scope
builder.Services.AddThargaUserNotices(o => o.Add(/* your catalogue */));

app.MapThargaMailUnsubscribe();                            // anonymous; allow it in a deny-by-default host
```

- **Categories** each user turns on or off on their profile — two built-ins, `account` (always required) and `service` (required unless `ServiceRequired` is false), plus your own — with an unsubscribe link and the RFC 8058 headers on every mail they could have turned off, how often each is sent, and a required one linking to your terms (`TermsUrl`).
- **An unsubscribe page** that names the category and the product, says it turns off only that, and shows the signed-in user the link is for every category and its state (`PreferencesPath`), or anyone else a way to sign in (`SignInPath`); `PageCss` styles it.
- **A persisted queue** any instance sends from, reading the user's choice and address when it sends, retrying only what may pass, under the outbound mail policy.
- **Messages** (`IUserMessageService`) staff write to chosen users of chosen teams, and **notices** (`IUserNoticeRaiser`) raised by events and delivered at once or in a daily summary.
- **A mail history per user** (`IUserMailHistoryService`), and mail marked **bounced** when the receiving server returns it — seen when it was sent from a support mailbox's address.
- **Invitations** go through it once it is registered: recorded, one immediate attempt, from its address. A sender of your own is kept.
- **Messages to contacts** (`AddThargaContactMessages`, `IContactMessageService`, the `contacts:send` system scope) — staff write to people who are not users, listed by the host with its own references: each address once, in contact categories of their own, from `ContactMessageOptions.FromAddress` or the address of mail to users, with a footer naming the sender (`SenderLine`), why it came and an unsubscribe link that opts the address out of the category or of all contact mail, for good. The same unsubscribe route answers it.

Storage comes from `Tharga.Team.MongoDB`, views from `Tharga.Team.Blazor`. Every instance must share one persisted Data Protection key ring, or unsubscribe links made on one are refused by another.

## Invitation email

```csharp
builder.Services.AddThargaEmailInvitations();   // settings from o.Email, or pass them here
```

The SMTP invitation sender, through the same transport and the outbound mail policy. **New in 3.29** — it replaces `SmtpTeamEmailSender`, which lived in `Tharga.Team.Service`; `o.Email` alone no longer registers a sender. With `AddThargaUserMail` registered it is replaced by mail to users, and becomes unnecessary.

## Dependencies

- [MailKit](https://www.nuget.org/packages/MailKit) — IMAP, SMTP and MIME.
- [DnsClient](https://www.nuget.org/packages/DnsClient) — the DNS lookups for DKIM keys.
- [AngleSharp](https://www.nuget.org/packages/AngleSharp) — parsing mail HTML for its text, its quoted thread and its images.
- The ASP.NET Core shared framework, privately — Data Protection and the unsubscribe route.
- [Tharga.Team.Service](https://www.nuget.org/packages/Tharga.Team.Service) — the outbound mail policy, audit logging and the HTML sanitizer.

## Moving to 4.2

- **`OutboundMail`, `InboundMail` and `MailFetchResult` gained trailing positional parameters** (`OutboundMail`: `Attachments`, `BodyHtml`, `UnsubscribeUrl`; `InboundMail`: `BouncedMessageId`, `BounceReason`, `SenderVerified`, `SenderDomain`, `Attachments`, `BodyHtml`; `MailFetchResult`: `Error`). Recompile; code that deconstructs them adds the new elements.
- **`InboundMail.BodyHtml` is raw**; sanitize it before showing it. `HadAttachments` is now true whenever files were present, carried or not.
- **Every mail read is DKIM-checked**, so a host reading mail needs outbound DNS; without it nothing is verified and nothing breaks.
- **Dependencies changed:** `DnsClient` and `AngleSharp` added, the ASP.NET Core framework referenced privately, and `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.Options` no longer listed, since the framework provides them.
- **With `AddThargaUserMail`, invitations go through mail to users**, from `UserMailOptions.Mail` rather than `EmailOptions`; a withheld or failed invitation says "could not send".
- **`UserMailOptions` gained `ServiceRequired`, `TermsUrl`, `ProductName`, `PreferencesPath`, `SignInPath` and `PageCss`, and `MailTemplateOptions` `SupportHtml`.** Their defaults change nothing but the unsubscribe page's wording; see [Moving to 4.2](https://team.tharga.net/articles/user-mail.html#moving-to-42) for the text keys added and removed.
- **Messages to contacts are new** (`AddThargaContactMessages`); nothing changes until it is called. They share the user mail queue, so a host with its own outbox reads [Moving to 4.2](https://team.tharga.net/articles/user-mail.html#moving-to-42): `UserMailKind.Contact`, `UserMailBatch.RecipientAddresses` and `IUserMailOutbox.CountSentToContactsByCategoryAsync`.

## Moving from Tharga.Team.Support

These types used to live in `Tharga.Team.Support.Email`. They are now in the namespace `Tharga.Team.Email`, and two were renamed:

| Before | Now |
|---|---|
| `Tharga.Team.Support.Email.ISupportMailClient` | `Tharga.Team.Email.IMailClient` |
| `Tharga.Team.Support.Email.SupportMailClient` | `Tharga.Team.Email.MailClient` (internal) |
| `Tharga.Team.Support.Email.*` | `Tharga.Team.Email.*` |

`Tharga.Team.Support` references this package, so support configuration (`SupportCaseOptions.Email`) is unchanged apart from the namespace of `MailOptions`.
