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
- Team mailbox stores - `ITeamMailKeyStore` (each team's mail keys in one document, so rotation is one write) and `ITeamMailStore` (received mail, kept once per delivery), registered with the team repository. Collections `TeamMailKey` and `InboundMail`, renamable through `ThargaTeamOptions`.

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
