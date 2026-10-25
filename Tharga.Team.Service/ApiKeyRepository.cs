using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.Service;

internal class ApiKeyRepository : IApiKeyRepository
{
    private readonly IApiKeyRepositoryCollection _collection;

    public ApiKeyRepository(IApiKeyRepositoryCollection collection)
    {
        _collection = collection;
    }

    public IAsyncEnumerable<ApiKeyEntity> GetAsync()
    {
        return _collection.GetAsync();
    }

    public Task<ApiKeyEntity> GetAsync(string key)
    {
        return _collection.GetOneAsync(x => x.Key == key);
    }

    public async Task<ApiKeyEntity> AddAsync(ApiKeyEntity apiKeyEntity)
    {
        await _collection.AddAsync(apiKeyEntity);
        return apiKeyEntity;
    }

    public Task LockKeyAsync(string key)
    {
        var filter = new FilterDefinitionBuilder<ApiKeyEntity>()
            .Eq(x => x.Key, key);
        var update = new UpdateDefinitionBuilder<ApiKeyEntity>()
            .Set(x => x.ApiKey, null);
        return _collection.UpdateOneAsync(filter, update);
    }

    public Task SetDisabledAsync(string key, DateTime? disabledAtUtc, string disabledBy)
    {
        var filter = new FilterDefinitionBuilder<ApiKeyEntity>()
            .Eq(x => x.Key, key);
        var update = new UpdateDefinitionBuilder<ApiKeyEntity>()
            .Set(x => x.DisabledAt, disabledAtUtc)
            .Set(x => x.DisabledBy, disabledBy);
        return _collection.UpdateOneAsync(filter, update);
    }

    public Task SetLastUsedAsync(string key, DateTime lastUsedAtUtc)
    {
        var filter = new FilterDefinitionBuilder<ApiKeyEntity>()
            .Eq(x => x.Key, key);
        var update = new UpdateDefinitionBuilder<ApiKeyEntity>()
            .Set(x => x.LastUsedAt, lastUsedAtUtc);
        return _collection.UpdateOneAsync(filter, update);
    }

    public Task SetPrefixAsync(string key, string prefix)
    {
        var filter = new FilterDefinitionBuilder<ApiKeyEntity>()
            .Eq(x => x.Key, key);
        var update = new UpdateDefinitionBuilder<ApiKeyEntity>()
            .Set(x => x.ApiKeyPrefix, prefix);
        return _collection.UpdateOneAsync(filter, update);
    }

    public async Task UpdateAsync(string key, ApiKeyEntity apiKeyEntity)
    {
        var item = await _collection.GetOneAsync(x => x.Key == key);
        apiKeyEntity = apiKeyEntity with
        {
            Id = item.Id,
            Key = key
        };
        await _collection.ReplaceOneAsync(apiKeyEntity);
    }

    public IAsyncEnumerable<ApiKeyEntity> GetByPrefixAsync(string prefix)
    {
        return _collection.GetAsync(x => x.ApiKeyPrefix == prefix);
    }

    public Task DeleteAsync(string key)
    {
        return _collection.DeleteOneAsync(x => x.Key == key);
    }

    public async Task PurgeExpiredAsync()
    {
        await _collection.DeleteManyAsync(x => x.ExpiryDate != null && x.ExpiryDate < DateTime.UtcNow);
    }

    public Task<long> CleanLegacyTagsAsync()
    {
        // Before #75, Tags was a Dictionary<string,string> persisted as a BSON document. Unset that
        // legacy field where it is still an object (not the new array), so those documents load again.
        // Runs server-side via the raw collection — it must not deserialize ApiKeyEntity (which would throw).
        var filter = new BsonDocument("Tags", new BsonDocument("$type", "object"));
        var update = new BsonDocument("$unset", new BsonDocument("Tags", 1));
        return _collection.ExecuteAsync(async collection =>
        {
            var result = await collection.UpdateManyAsync(filter, update);
            return result.IsModifiedCountAvailable ? result.ModifiedCount : result.MatchedCount;
        }, Operation.Update);
    }
}
