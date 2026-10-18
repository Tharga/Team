using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>One user's choice for one mail category, stored only when it differs from the category's default.</summary>
public record UserMailPreferenceEntity : EntityBase
{
    /// <summary>The user's key.</summary>
    public required string UserKey { get; init; }

    /// <summary>The category's key.</summary>
    public required string Category { get; init; }

    /// <summary>Whether the user receives the category.</summary>
    public required bool Receives { get; init; }

    /// <summary>When the choice was last made, in UTC.</summary>
    public required DateTime UpdatedAt { get; init; }
}

/// <summary>The collection of users' mail choices.</summary>
public interface IUserMailPreferenceCollection : IDiskRepositoryCollection<UserMailPreferenceEntity>;

/// <summary>Users' mail choices, one document per user and category, default collection name <c>UserMailPreference</c>.</summary>
/// <remarks>The unique index is what keeps one choice per user and category when two changes race.</remarks>
public class UserMailPreferenceCollection : DiskRepositoryCollectionBase<UserMailPreferenceEntity>, IUserMailPreferenceCollection
{
    private readonly string _collectionName;

    /// <summary>Creates the collection under the configured name.</summary>
    public UserMailPreferenceCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<UserMailPreferenceCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.UserMailPreferenceCollectionName ?? ThargaTeamOptions.DefaultUserMailPreferenceCollectionName;
    }

    /// <inheritdoc />
    public override string CollectionName => _collectionName;

    /// <inheritdoc />
    public override IEnumerable<CreateIndexModel<UserMailPreferenceEntity>> Indices =>
    [
        new(Builders<UserMailPreferenceEntity>.IndexKeys.Ascending(x => x.UserKey).Ascending(x => x.Category),
            new CreateIndexOptions { Unique = true, Name = "UserKey_Category" })
    ];
}
