using Tharga.MongoDB;

namespace Tharga.Team.Service;

/// <summary>
/// Repository interface for API key persistence. Auto-registered by Tharga.MongoDB.
/// </summary>
public interface IApiKeyRepository : IRepository
{
    /// <summary>Returns all stored API key entities.</summary>
    IAsyncEnumerable<ApiKeyEntity> GetAsync();

    /// <summary>Returns the API key entity with the specified key, or <c>null</c> if not found.</summary>
    Task<ApiKeyEntity> GetAsync(string key);

    /// <summary>Persists a new API key entity and returns it.</summary>
    Task<ApiKeyEntity> AddAsync(ApiKeyEntity apiKeyEntity);

    /// <summary>Replaces the API key entity identified by the given key.</summary>
    Task UpdateAsync(string key, ApiKeyEntity apiKeyEntity);

    /// <summary>Marks the API key as locked so it cannot be used.</summary>
    Task LockKeyAsync(string key);

    /// <summary>
    /// Marks the key disabled, or clears the mark when <paramref name="disabledAtUtc"/> is null.
    /// A disabled key is refused at authentication.
    /// </summary>
    Task SetDisabledAsync(string key, DateTime? disabledAtUtc, string disabledBy);

    /// <summary>Sets the "last used" timestamp for the key via a targeted field update (no full-document replace).</summary>
    Task SetLastUsedAsync(string key, DateTime lastUsedAtUtc);

    /// <summary>Returns API key entities matching the given prefix, or those stored without one when <paramref name="prefix"/> is <c>null</c>.</summary>
    IAsyncEnumerable<ApiKeyEntity> GetByPrefixAsync(string prefix);

    /// <summary>Sets the lookup prefix for the key via a targeted field update (no full-document replace).</summary>
    Task SetPrefixAsync(string key, string prefix);

    /// <summary>Deletes the API key entity with the specified key.</summary>
    Task DeleteAsync(string key);

    /// <summary>Deletes all expired API key entities.</summary>
    Task PurgeExpiredAsync();

    /// <summary>
    /// One-time migration: removes the legacy <c>Tags</c> field from documents where it is still stored as a
    /// BSON document (the pre-#75 <c>Dictionary&lt;string,string&gt;</c> representation). Runs server-side and
    /// returns the number of documents cleaned. Safe to run repeatedly. Reading is already tolerant of the
    /// legacy shape; this just purges it permanently.
    /// </summary>
    Task<long> CleanLegacyTagsAsync();
}
