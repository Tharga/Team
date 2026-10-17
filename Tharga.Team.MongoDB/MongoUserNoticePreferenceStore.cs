using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>MongoDB adapter for <see cref="IUserNoticePreferenceStore"/>: one document per user and notice, and per team and notice, that differs from a default.</summary>
internal sealed class MongoUserNoticePreferenceStore(IUserNoticePreferenceCollection choices, IUserNoticeTeamDefaultCollection teamDefaults, TimeProvider timeProvider) : IUserNoticePreferenceStore
{
    internal const int KeysPerQuery = 1000;

    public async Task<IReadOnlyDictionary<string, UserNoticeSetting>> GetChoicesAsync(string userKey, CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, UserNoticeSetting>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(userKey)) return found;

        await foreach (var choice in choices.GetAsync(x => x.UserKey == userKey, cancellationToken: cancellationToken))
            found[choice.NoticeKey] = new UserNoticeSetting { Enabled = choice.Enabled, Delivery = choice.Delivery };

        return found;
    }

    /// <remarks>Asked in slices of <see cref="KeysPerQuery"/> keys, so each query stays small and uses the notice and user index.</remarks>
    public async Task<IReadOnlyDictionary<string, UserNoticeSetting>> GetChoicesForNoticeAsync(string noticeKey, IReadOnlyCollection<string> userKeys, CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, UserNoticeSetting>(StringComparer.Ordinal);
        string[] keys = [.. (userKeys ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal)];

        foreach (var slice in keys.Chunk(KeysPerQuery))
        {
            await foreach (var choice in choices.GetAsync(OfNotice(noticeKey, slice), cancellationToken: cancellationToken))
                found[choice.UserKey] = new UserNoticeSetting { Enabled = choice.Enabled, Delivery = choice.Delivery };
        }

        return found;
    }

    /// <remarks>Replaced whole rather than merged, so a part the user no longer deviates on is removed with it; the unique index turns a racing second insert into a replacement.</remarks>
    public async Task SetChoiceAsync(string userKey, string noticeKey, UserNoticeSetting setting, CancellationToken cancellationToken = default)
    {
        if (setting == null || setting.IsEmpty)
        {
            await choices.DeleteManyAsync(OfUser(userKey, noticeKey));
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var update = Builders<UserNoticePreferenceEntity>.Update.Set(x => x.Enabled, setting.Enabled).Set(x => x.Delivery, setting.Delivery).Set(x => x.UpdatedAt, now);

        if ((await choices.UpdateOneAsync(OfUser(userKey, noticeKey), update, OneOption<UserNoticePreferenceEntity>.FirstOrDefault))?.Before != null) return;

        try
        {
            await choices.AddAsync(new UserNoticePreferenceEntity { UserKey = userKey, NoticeKey = noticeKey, Enabled = setting.Enabled, Delivery = setting.Delivery, UpdatedAt = now });
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            await choices.UpdateOneAsync(OfUser(userKey, noticeKey), update, OneOption<UserNoticePreferenceEntity>.FirstOrDefault);
        }
    }

    public async Task<IReadOnlyDictionary<string, UserNoticeSetting>> GetTeamDefaultsAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, UserNoticeSetting>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(teamKey)) return found;

        await foreach (var value in teamDefaults.GetAsync(x => x.TeamKey == teamKey, cancellationToken: cancellationToken))
            found[value.NoticeKey] = new UserNoticeSetting { Enabled = value.Enabled, Delivery = value.Delivery };

        return found;
    }

    public async Task SetTeamDefaultAsync(string teamKey, string noticeKey, UserNoticeSetting setting, CancellationToken cancellationToken = default)
    {
        if (setting == null || setting.IsEmpty)
        {
            await teamDefaults.DeleteManyAsync(OfTeam(teamKey, noticeKey));
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var update = Builders<UserNoticeTeamDefaultEntity>.Update.Set(x => x.Enabled, setting.Enabled).Set(x => x.Delivery, setting.Delivery).Set(x => x.UpdatedAt, now);

        if ((await teamDefaults.UpdateOneAsync(OfTeam(teamKey, noticeKey), update, OneOption<UserNoticeTeamDefaultEntity>.FirstOrDefault))?.Before != null) return;

        try
        {
            await teamDefaults.AddAsync(new UserNoticeTeamDefaultEntity { TeamKey = teamKey, NoticeKey = noticeKey, Enabled = setting.Enabled, Delivery = setting.Delivery, UpdatedAt = now });
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            await teamDefaults.UpdateOneAsync(OfTeam(teamKey, noticeKey), update, OneOption<UserNoticeTeamDefaultEntity>.FirstOrDefault);
        }
    }

    public async Task<int> RemoveTeamDefaultsAsync(string teamKey, CancellationToken cancellationToken = default)
        => string.IsNullOrWhiteSpace(teamKey) ? 0 : (int)await teamDefaults.DeleteManyAsync(Builders<UserNoticeTeamDefaultEntity>.Filter.Eq(x => x.TeamKey, teamKey));

    internal static FilterDefinition<UserNoticePreferenceEntity> OfUser(string userKey, string noticeKey)
        => Builders<UserNoticePreferenceEntity>.Filter.Eq(x => x.UserKey, userKey) & Builders<UserNoticePreferenceEntity>.Filter.Eq(x => x.NoticeKey, noticeKey);

    internal static FilterDefinition<UserNoticePreferenceEntity> OfNotice(string noticeKey, IEnumerable<string> userKeys)
        => Builders<UserNoticePreferenceEntity>.Filter.Eq(x => x.NoticeKey, noticeKey) & Builders<UserNoticePreferenceEntity>.Filter.In(x => x.UserKey, userKeys);

    internal static FilterDefinition<UserNoticeTeamDefaultEntity> OfTeam(string teamKey, string noticeKey)
        => Builders<UserNoticeTeamDefaultEntity>.Filter.Eq(x => x.TeamKey, teamKey) & Builders<UserNoticeTeamDefaultEntity>.Filter.Eq(x => x.NoticeKey, noticeKey);
}
