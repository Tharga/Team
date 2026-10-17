using MongoDB.Driver;

namespace Tharga.Team.MongoDB;

/// <summary>MongoDB adapter for <see cref="IUserMailPreferenceStore"/>: one document per user and category that differs from its default.</summary>
internal sealed class MongoUserMailPreferenceStore(IUserMailPreferenceCollection collection, TimeProvider timeProvider) : IUserMailPreferenceStore
{
    public async Task<IReadOnlyDictionary<string, bool>> GetChoicesAsync(string userKey, CancellationToken cancellationToken = default)
    {
        var choices = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(userKey)) return choices;

        await foreach (var choice in collection.GetAsync(x => x.UserKey == userKey, cancellationToken: cancellationToken))
            choices[choice.Category] = choice.Receives;

        return choices;
    }

    /// <remarks>An update first and an insert when there was nothing to update; the unique index turns a racing second insert into an update.</remarks>
    public async Task SetChoiceAsync(string userKey, string categoryKey, bool receives, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var update = Builders<UserMailPreferenceEntity>.Update.Set(x => x.Receives, receives).Set(x => x.UpdatedAt, now);

        if ((await collection.UpdateOneAsync(Of(userKey, categoryKey), update))?.Before != null) return;

        try
        {
            await collection.AddAsync(new UserMailPreferenceEntity { UserKey = userKey, Category = categoryKey, Receives = receives, UpdatedAt = now });
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            await collection.UpdateOneAsync(Of(userKey, categoryKey), update);
        }
    }

    public Task ClearChoiceAsync(string userKey, string categoryKey, CancellationToken cancellationToken = default)
        => collection.DeleteManyAsync(Of(userKey, categoryKey));

    /// <remarks>Asked in slices of <see cref="KeysPerQuery"/> keys, so each query stays small and uses the user and category index.</remarks>
    public async Task<IReadOnlyDictionary<string, bool>> GetChoicesForCategoryAsync(string categoryKey, IReadOnlyCollection<string> userKeys, CancellationToken cancellationToken = default)
    {
        var choices = new Dictionary<string, bool>(StringComparer.Ordinal);
        string[] keys = [.. (userKeys ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal)];

        foreach (var slice in keys.Chunk(KeysPerQuery))
        {
            await foreach (var choice in collection.GetAsync(OfCategory(categoryKey, slice), cancellationToken: cancellationToken))
                choices[choice.UserKey] = choice.Receives;
        }

        return choices;
    }

    internal const int KeysPerQuery = 1000;

    internal static FilterDefinition<UserMailPreferenceEntity> Of(string userKey, string categoryKey)
        => Builders<UserMailPreferenceEntity>.Filter.Eq(x => x.UserKey, userKey) & Builders<UserMailPreferenceEntity>.Filter.Eq(x => x.Category, categoryKey);

    internal static FilterDefinition<UserMailPreferenceEntity> OfCategory(string categoryKey, IEnumerable<string> userKeys)
        => Builders<UserMailPreferenceEntity>.Filter.In(x => x.UserKey, userKeys) & Builders<UserMailPreferenceEntity>.Filter.Eq(x => x.Category, categoryKey);
}
