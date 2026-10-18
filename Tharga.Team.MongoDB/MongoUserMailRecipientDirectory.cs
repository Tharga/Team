using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>MongoDB adapter for <see cref="IUserMailRecipientDirectory"/>, over the user repository.</summary>
/// <remarks>Reads the repository rather than the user service, because the service needs a signed-in caller and the mail sender has none.</remarks>
internal sealed class MongoUserMailRecipientDirectory<TUserEntity>(IUserRepository<TUserEntity> users) : IUserMailRecipientDirectory
    where TUserEntity : EntityBase, IUser
{
    public async Task<UserMailRecipient> FindAsync(string userKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userKey)) return null;

        var user = await users.GetByKeyAsync(userKey);

        return user == null ? null : ToRecipient(user);
    }

    /// <remarks>One read for every key, rather than one per key.</remarks>
    public async Task<UserMailRecipient[]> FindManyAsync(IReadOnlyCollection<string> userKeys, CancellationToken cancellationToken = default)
    {
        string[] keys = [.. (userKeys ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal)];
        if (keys.Length == 0) return [];

        return [.. (await users.GetByKeysAsync(keys) ?? []).Where(x => x != null).DistinctBy(x => x.Key, StringComparer.Ordinal).Select(ToRecipient)];
    }

    private static UserMailRecipient ToRecipient(TUserEntity user) => new()
    {
        Key = user.Key,
        EMail = string.IsNullOrWhiteSpace(user.EMail) ? null : user.EMail.Trim(),
        Name = string.IsNullOrWhiteSpace(user.Name) ? null : user.Name
    };
}
