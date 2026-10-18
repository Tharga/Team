using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>One user's choice for one notice, each part stored only where it differs from a default in force for them.</summary>
public record UserNoticePreferenceEntity : EntityBase
{
    /// <summary>The user's key.</summary>
    public required string UserKey { get; init; }

    /// <summary>The notice's key.</summary>
    public required string NoticeKey { get; init; }

    /// <summary>Whether the user receives the notice, or null to follow the default.</summary>
    [BsonIgnoreIfNull]
    public bool? Enabled { get; init; }

    /// <summary>How the user receives it, stored by name, or null to follow the default.</summary>
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public UserNoticeDelivery? Delivery { get; init; }

    /// <summary>When the choice was last made, in UTC.</summary>
    public DateTime UpdatedAt { get; init; }
}

/// <summary>A team's default for one notice, each part stored only where it differs from the catalogue's.</summary>
public record UserNoticeTeamDefaultEntity : EntityBase
{
    /// <summary>The team's key.</summary>
    public required string TeamKey { get; init; }

    /// <summary>The notice's key.</summary>
    public required string NoticeKey { get; init; }

    /// <summary>Whether members receive the notice, or null to follow the catalogue.</summary>
    [BsonIgnoreIfNull]
    public bool? Enabled { get; init; }

    /// <summary>How members receive it, stored by name, or null to follow the catalogue.</summary>
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public UserNoticeDelivery? Delivery { get; init; }

    /// <summary>When the default was last set, in UTC.</summary>
    public DateTime UpdatedAt { get; init; }
}

/// <summary>The collection of users' notice choices.</summary>
public interface IUserNoticePreferenceCollection : IDiskRepositoryCollection<UserNoticePreferenceEntity>;

/// <summary>The collection of teams' notice defaults.</summary>
public interface IUserNoticeTeamDefaultCollection : IDiskRepositoryCollection<UserNoticeTeamDefaultEntity>;

/// <summary>Users' notice choices, one document per user and notice, default collection name <c>UserNoticePreference</c>.</summary>
public class UserNoticePreferenceCollection : DiskRepositoryCollectionBase<UserNoticePreferenceEntity>, IUserNoticePreferenceCollection
{
    private readonly string _collectionName;

    /// <summary>Creates the collection under the configured name.</summary>
    public UserNoticePreferenceCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<UserNoticePreferenceCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.UserNoticePreferenceCollectionName ?? ThargaTeamOptions.DefaultUserNoticePreferenceCollectionName;
    }

    /// <inheritdoc />
    public override string CollectionName => _collectionName;

    /// <inheritdoc />
    public override IEnumerable<CreateIndexModel<UserNoticePreferenceEntity>> Indices =>
    [
        new(Builders<UserNoticePreferenceEntity>.IndexKeys.Ascending(x => x.UserKey).Ascending(x => x.NoticeKey),
            new CreateIndexOptions { Unique = true, Name = "UserKey_NoticeKey" }),
        new(Builders<UserNoticePreferenceEntity>.IndexKeys.Ascending(x => x.NoticeKey).Ascending(x => x.UserKey),
            new CreateIndexOptions { Name = "NoticeKey_UserKey" })
    ];
}

/// <summary>Teams' notice defaults, one document per team and notice, default collection name <c>UserNoticeTeamDefault</c>.</summary>
public class UserNoticeTeamDefaultCollection : DiskRepositoryCollectionBase<UserNoticeTeamDefaultEntity>, IUserNoticeTeamDefaultCollection
{
    private readonly string _collectionName;

    /// <summary>Creates the collection under the configured name.</summary>
    public UserNoticeTeamDefaultCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<UserNoticeTeamDefaultCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.UserNoticeTeamDefaultCollectionName ?? ThargaTeamOptions.DefaultUserNoticeTeamDefaultCollectionName;
    }

    /// <inheritdoc />
    public override string CollectionName => _collectionName;

    /// <inheritdoc />
    public override IEnumerable<CreateIndexModel<UserNoticeTeamDefaultEntity>> Indices =>
    [
        new(Builders<UserNoticeTeamDefaultEntity>.IndexKeys.Ascending(x => x.TeamKey).Ascending(x => x.NoticeKey),
            new CreateIndexOptions { Unique = true, Name = "TeamKey_NoticeKey" })
    ];
}
