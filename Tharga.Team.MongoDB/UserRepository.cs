using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

internal class UserRepository<TUserEntity> : IUserRepository<TUserEntity>
    where TUserEntity : EntityBase, IUser
{
    // Interface default members (LastSeen, DirectoryId) only serialize when the entity declares the
    // property; updating an undeclared member would fail at driver render time, so those writes no-op.
    private static readonly bool _entityDeclaresLastSeen = typeof(TUserEntity).GetProperty(nameof(IUser.LastSeen)) != null;
    private static readonly bool _entityDeclaresDirectoryId = typeof(TUserEntity).GetProperty(nameof(IUser.DirectoryId)) != null;
    private static readonly bool _entityDeclaresIcon = typeof(TUserEntity).GetProperty(nameof(IUser.Icon)) != null;

    private readonly IUserRepositoryCollection<TUserEntity> _collection;

    public UserRepository(IUserRepositoryCollection<TUserEntity> collection)
    {
        _collection = collection;
    }

    public virtual IAsyncEnumerable<TUserEntity> GetAsync()
    {
        return _collection.GetAsync();
    }

    public virtual Task<TUserEntity> GetAsync(string identity)
    {
        return _collection.GetOneAsync(x => x.Identity == identity);
    }

    public virtual Task<TUserEntity> GetByKeyAsync(string userKey)
    {
        return _collection.GetOneAsync(x => x.Key == userKey);
    }

    public virtual async Task<TUserEntity[]> GetByKeysAsync(IReadOnlyCollection<string> userKeys)
    {
        string[] keys = [.. (userKeys ?? []).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal)];
        if (keys.Length == 0) return [];

        var filter = new FilterDefinitionBuilder<TUserEntity>().In(x => x.Key, keys);

        return await _collection.GetAsync(filter).ToArrayAsync();
    }

    /// <remarks>
    /// An anchored, escaped, case-insensitive pattern rather than an equality, because addresses are stored as
    /// the sign-in provider spelled them. It cannot use an index, which is acceptable for a lookup made once per
    /// new support mail.
    /// </remarks>
    public virtual async Task<TUserEntity[]> GetByEMailAsync(string email)
    {
        var wanted = email?.Trim();
        if (string.IsNullOrEmpty(wanted)) return [];

        var filter = new FilterDefinitionBuilder<TUserEntity>()
            .Regex(x => x.EMail, EMailPattern(wanted));

        return await _collection.GetAsync(filter).ToArrayAsync();
    }

    /// <remarks>An escaped, case-insensitive pattern on the name and the address, read as a stream so the caller stops when it has enough; a disabled user is left out as it is read.</remarks>
    public virtual async IAsyncEnumerable<TUserEntity> SearchAsync(string text)
    {
        var wanted = text?.Trim();
        if (string.IsNullOrEmpty(wanted)) yield break;

        await foreach (var user in _collection.GetAsync(SearchFilter(wanted)))
        {
            if (user.DisabledAt == null) yield return user;
        }
    }

    internal static FilterDefinition<TUserEntity> SearchFilter(string text)
    {
        var pattern = new BsonRegularExpression(Regex.Escape(text), "i");
        var builder = new FilterDefinitionBuilder<TUserEntity>();

        return builder.Or(builder.Regex(x => x.Name, pattern), builder.Regex(x => x.EMail, pattern));
    }

    internal static BsonRegularExpression EMailPattern(string email)
        => new($@"^\s*{Regex.Escape(email)}\s*$", "i");

    public virtual Task AddAsync(TUserEntity user)
    {
        return _collection.AddAsync(user);
    }

    public virtual Task SetNameAsync(string userKey, string name)
    {
        var filter = new FilterDefinitionBuilder<TUserEntity>()
            .Eq(x => x.Key, userKey);
        var update = new UpdateDefinitionBuilder<TUserEntity>()
            .Set(x => x.Name, name);

        return _collection.UpdateOneAsync(filter, update);
    }

    public virtual Task SetEMailAsync(string userKey, string email)
    {
        var filter = new FilterDefinitionBuilder<TUserEntity>()
            .Eq(x => x.Key, userKey);
        var update = new UpdateDefinitionBuilder<TUserEntity>()
            .Set(x => x.EMail, email);

        return _collection.UpdateOneAsync(filter, update);
    }

    public virtual Task SetLastSeenAsync(string userKey, DateTime lastSeen)
    {
        if (!_entityDeclaresLastSeen) return Task.CompletedTask;

        var filter = new FilterDefinitionBuilder<TUserEntity>()
            .Eq(x => x.Key, userKey);
        var update = new UpdateDefinitionBuilder<TUserEntity>()
            .Set(x => x.LastSeen, lastSeen);

        return _collection.UpdateOneAsync(filter, update);
    }

    public virtual Task SetDirectoryIdAsync(string userKey, string directoryId)
    {
        if (!_entityDeclaresDirectoryId) return Task.CompletedTask;

        var filter = new FilterDefinitionBuilder<TUserEntity>()
            .Eq(x => x.Key, userKey);
        var update = new UpdateDefinitionBuilder<TUserEntity>()
            .Set(x => x.DirectoryId, directoryId);

        return _collection.UpdateOneAsync(filter, update);
    }

    public virtual Task SetIconAsync(string userKey, string reference)
    {
        if (!_entityDeclaresIcon) return Task.CompletedTask;

        var filter = new FilterDefinitionBuilder<TUserEntity>()
            .Eq(x => x.Key, userKey);
        var update = new UpdateDefinitionBuilder<TUserEntity>()
            .Set(x => x.Icon, reference);

        return _collection.UpdateOneAsync(filter, update);
    }

    public virtual Task DeleteAsync(string userKey)
    {
        return _collection.DeleteOneAsync(x => x.Key == userKey);
    }
}