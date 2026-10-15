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