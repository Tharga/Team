using MongoDB.Bson;

namespace Tharga.Team.MongoDB;

/// <summary>
/// Built-in <see cref="IIconSettingsStore"/> backed by MongoDB — the zero-configuration default, registered by
/// <c>AddThargaTeamRepository</c> via <c>TryAdd</c> so a consumer-supplied store takes precedence.
/// </summary>
public class MongoIconSettingsStore : IIconSettingsStore
{
    private readonly IIconSettingsRepositoryCollection _collection;

    public MongoIconSettingsStore(IIconSettingsRepositoryCollection collection)
    {
        _collection = collection;
    }

    public async Task<IconSettingsState> LoadAsync(CancellationToken cancellationToken = default)
    {
        var entity = await _collection.GetOneAsync(x => x.Key == IconSettingsEntity.SingletonKey);
        if (entity == null) return null;

        return new IconSettingsState
        {
            GravatarEnabled = entity.GravatarEnabled,
            GravatarStyle = entity.GravatarStyle,
            DefaultUserIconUrl = entity.DefaultUserIconUrl,
            AllowUserUpload = entity.AllowUserUpload,
            AllowAdminUpload = entity.AllowAdminUpload
        };
    }

    /// <remarks>
    /// <b>The document id is fixed rather than generated</b>, so this is a true upsert: the first save creates
    /// it and every later one replaces the same document, with no read first and no race between two saves
    /// creating two "only" documents. The unique index on <see cref="IconSettingsEntity.Key"/> is the second
    /// half of that guarantee, enforced by the store rather than by whoever writes to it.
    /// </remarks>
    public Task SaveAsync(IconSettingsState settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return _collection.AddOrReplaceAsync(new IconSettingsEntity
        {
            Id = SingletonId,
            Key = IconSettingsEntity.SingletonKey,
            GravatarEnabled = settings.GravatarEnabled,
            GravatarStyle = string.IsNullOrWhiteSpace(settings.GravatarStyle) ? GravatarStyles.Default : settings.GravatarStyle,
            DefaultUserIconUrl = settings.DefaultUserIconUrl,
            AllowUserUpload = settings.AllowUserUpload,
            AllowAdminUpload = settings.AllowAdminUpload,
            ChangedUtc = DateTime.UtcNow
        });
    }

    /// <summary>The id of the one icon-settings document. Fixed, because there is only ever one.</summary>
    internal static readonly ObjectId SingletonId = ObjectId.Parse("1c0e5e771e651e0000000001");
}
