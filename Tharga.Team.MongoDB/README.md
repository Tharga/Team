# Tharga Team MongoDB
[![NuGet](https://img.shields.io/nuget/v/Tharga.Team.MongoDB)](https://www.nuget.org/packages/Tharga.Team.MongoDB)
![Nuget](https://img.shields.io/nuget/dt/Tharga.Team.MongoDB)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

MongoDB persistence layer for [Tharga.Team](https://www.nuget.org/packages/Tharga.Team). Provides repository base classes for storing teams and users in MongoDB.

## What's included

- `TeamRepository` / `ITeamRepository` - MongoDB-backed team storage.
- `UserRepository` / `IUserRepository` - MongoDB-backed user storage.
- `TeamRepositoryCollection` / `UserRepositoryCollection` - MongoDB collection definitions with indexes.
- `TeamMemberBase` - Base record for team member entities.
- `TeamEntityBase` - Base record for team entities.
- `UserServiceRepositoryBase` - User service base class with MongoDB persistence.
- Team mailbox stores - `ITeamMailKeyStore` (each team's mail keys in one document, so rotation is one write) and `ITeamMailStore` (received mail, kept once per delivery; the postmaster's held and archived mail, archiving and restoring each one conditional write), registered with the team repository. Collections `TeamMailKey` and `InboundMail`, renamable through `ThargaTeamOptions`; `InboundMail` is indexed on `MailId` (unique), `TeamKey_ReceivedAt`, `TeamKey_Outcome_ReceivedAt` (the postmaster's held and archived lists and counts, and a team's unhandled mail) and `ReceivedAt`.
- Support files - `ISupportAttachmentStore` over a GridFS bucket beside the cases (`SupportAttachment`, `ThargaTeamOptions.SupportAttachmentBucketName`), registered with the team repository; purging a team removes its files first. Register another before `AddThargaTeamRepository` — blob storage, say — to keep files elsewhere.
- Support sender directory - `ISupportSenderDirectory`, which says which user accounts hold a support mail's address and which teams each is an active member of; registered when both the user and the team repository are. Register one that finds nobody first if your sign-in provider issues unverified addresses.
- Mail to users, messages and notices - the stores behind `Tharga.Team.Email`'s mail to users and mail to contacts, listed below.

## Mail to users, messages and notices

`AddThargaTeamRepository` registers the stores for [mail to users](https://team.tharga.net/articles/user-mail.html),
[messages](https://team.tharga.net/articles/user-messages.html) and [notices](https://team.tharga.net/articles/user-notices.html)
with `TryAdd`, so registering your own `IUserMailOutbox`, `IUserMailPreferenceStore`, `IUserMessageLog`,
`IUserNoticeOutbox`, `IUserNoticePreferenceStore`, `IContactMailSuppressionStore` or `IContactMessageLog` first keeps
that data elsewhere. The directories that read
users and teams — `IUserMailRecipientDirectory` (with a user repository) and `IUserMessageMemberDirectory` (with
both) — read through the repositories.

| Collection (default name) | `ThargaTeamOptions` | Holds | Kept |
|---|---|---|---|
| `UserMailPreference` | `UserMailPreferenceCollectionName` | Users' mail choices that differ from a category's default | until changed back |
| `UserMail` | `UserMailCollectionName` | Every queued mail and what became of it | `UserMailRetention` (90 days) after it completed |
| `UserMailBatch` | `UserMailBatchCollectionName` | The content one mail to many users shares, stored once | `UserMailRetention` after its last mail completed |
| `UserMessage` | `UserMessageCollectionName` | The log of messages staff sent | `UserMailRetention` after it was sent |
| `UserNotice` | `UserNoticeCollectionName` | Raised notice occurrences until distributed | `UserNoticeRetention` (90 days) after |
| `UserNoticeItem` | `UserNoticeItemCollectionName` | Each user's notices, until and after they are mailed | `UserNoticeRetention` after |
| `UserNoticeStream` | `UserNoticeStreamCollectionName` | When each user's next notice mail is due | kept |
| `UserNoticePreference` | `UserNoticePreferenceCollectionName` | Users' notice choices that differ from a default | until changed back |
| `UserNoticeTeamDefault` | `UserNoticeTeamDefaultCollectionName` | Teams' notice defaults that differ from the catalogue | until changed back, or the team is purged |
| `ContactMailSuppression` | `ContactMailSuppressionCollectionName` | Addresses that opted out of contact mail, one per address and category (`*` for all), unique on `Address_Category` | for good |
| `ContactMessage` | `ContactMessageCollectionName` | The log of messages staff sent to contacts, each with the list it was sent to | `UserMailRetention` after it was sent |

`UserMail` stores its `Kind` by name, the address a mail went to (`RecipientAddress`, in lower case), the name of
an address-only recipient (`RecipientName`) and `BouncedAt`, under the indexes `Status_NextAttemptAt`,
`DeduplicationKey`, `BatchId_Status`, `RecipientUserKey_Id`, `RecipientAddress_Id`, a sparse `MessageId`, which
is how a bounce finds its mail, and `Status_CompletedAt_Category_RecipientUserKey`, from which how often each
category was sent is counted without reading a mail (`IUserMailCollection.CountSentByCategoryAsync`), and
`Kind_Status_CompletedAt_Category_RecipientAddress`, which does the same for contact categories per address
(`CountSentToContactsByCategoryAsync`). A contact mail names no user, carries its address and is of kind `Contact`; a
user's history leaves it out. A batch's recorded outcomes count `Bounced` apart from `Sent`.
`UserNoticeRetention` is also how long an occurrence raised again is recognised and not sent twice.

## Finding users and teams in one read

`IUserRepository` gained `GetByKeysAsync`, `GetByEMailAsync` and `SearchAsync`, and `ITeamRepository` gained
`GetTeamsByUsersAsync`. They are **default interface members that read one key at a time, or every record**, so a
repository of your own keeps compiling and answers correctly; the built-in ones ask the database. Implement them
in your own to avoid the full reads. `SearchAsync` matches a name or address containing the text, ignoring case,
and leaves out disabled users.

## Moving to 4.2

- **Nothing to migrate.** Support case documents gain optional fields — a message's `BodyHtml`, `DeliveryError`
  and `Attachments`, a case's `SuggestedTeams` and `SuggestionReason`, a binding's `MessageSequence`, `Mailbox`
  and `MailboxAddress` — which documents without them read as empty.
- **Mail attachments land in the `SupportAttachment` GridFS bucket**, from the first mail read after upgrading.
- **`AddThargaTeamRepository` registers more**, every one with `TryAdd`: the support file store and sender
  directory, and the mail to users stores and directories above. A host substituting any of them registers its
  own first.
- **A repository of your own** keeps compiling; see the default members above.
- **`UserMail` gains the index `Status_CompletedAt_Category_RecipientUserKey`**, created as the others are, and
  `IUserMailCollection` the default members `CountSentByCategoryAsync` and `Retention`.
- **Contact mail adds** the collections `ContactMailSuppression` and `ContactMessage`, the `UserMail` index
  `Kind_Status_CompletedAt_Category_RecipientAddress` and the default member
  `IUserMailCollection.CountSentToContactsByCategoryAsync`; `ThargaTeamOptions` gained `ContactMailSuppressionCollectionName`
  and `ContactMessageCollectionName`.

## Quick start

```csharp
builder.Services.AddThargaTeamRepository(o =>
{
    o.RegisterUserRepository<UserEntity>();
    o.RegisterTeamRepository<TeamEntity, TeamMember>();
});
```

Requires [Tharga.MongoDB](https://www.nuget.org/packages/Tharga.MongoDB) to be configured with a connection string.

## Adding per-deployment User indices

The built-in `UserRepositoryCollection<TUserEntity>` declares a unique index on `Identity`. To add additional indices (e.g. on a custom email column), subclass the collection and register the subclass via the `RegisterUserRepository<TUserEntity, TCollection>` overload:

```csharp
public class MyUserRepositoryCollection : UserRepositoryCollection<MyUserEntity>
{
    public MyUserRepositoryCollection(IMongoDbServiceFactory factory, ILogger<UserRepositoryCollection<MyUserEntity>> logger, IOptions<ThargaTeamOptions> options = null)
        : base(factory, logger, options) { }

    public override IEnumerable<CreateIndexModel<MyUserEntity>> Indices =>
    [
        // Keep the base Identity index
        ..base.Indices,
        // Plus your own
        new(Builders<MyUserEntity>.IndexKeys.Ascending(x => x.EMail),
            new CreateIndexOptions { Unique = true, Name = "EMail" })
    ];
}

builder.Services.AddThargaTeamRepository(o =>
{
    o.RegisterUserRepository<MyUserEntity, MyUserRepositoryCollection>();
});
```

## How enums are stored

Every enum this package persists is stored **by name**, not by ordinal — `"Viewer"` rather than `3`. The
name is the meaning; the ordinal is an accident of declaration order, and a stored ordinal silently comes
to mean something else the moment a member is inserted or reordered.

If you add your own entity, declare it:

```csharp
[BsonRepresentation(BsonType.String)]
public MyEnum Kind { get; init; }
```

The driver's default is `Int32`, so **omitting the attribute selects the ordinal** without looking like a
choice. A test in this package sweeps every persisted entity and fails if one is missing it.

### Changed

`TeamEntityBase.ConsentAccessLevel` and `ApiKeyEntity.AccessLevel` previously stored ordinals and now store
names. **No migration is required** — the driver reads both, so existing documents keep working and convert
the next time each is written.

One thing to check: if you query these two fields directly against the collection — a script, a dashboard,
a report — a numeric comparison such as `{ ConsentAccessLevel: 3 }` will match only the documents not yet
rewritten. Match on the name instead, or on both while the conversion settles. Nothing inside the toolkit
queries them, so this affects external tooling only.

## Dependencies

- [Tharga.Team](https://www.nuget.org/packages/Tharga.Team) - Domain models and service abstractions.
- [Tharga.MongoDB](https://www.nuget.org/packages/Tharga.MongoDB) - MongoDB repository infrastructure.

## Related packages

| Package | Description |
|---------|-------------|
| [Tharga.Team](https://www.nuget.org/packages/Tharga.Team) | Domain models and authorization primitives |
| [Tharga.Team.Blazor](https://www.nuget.org/packages/Tharga.Team.Blazor) | Team management Blazor UI components, authentication |
| [Tharga.Team.Service](https://www.nuget.org/packages/Tharga.Team.Service) | Server-side API key auth, Swagger, audit logging |
| [Tharga.Blazor](https://www.nuget.org/packages/Tharga.Blazor) | Generic Blazor UI components |
