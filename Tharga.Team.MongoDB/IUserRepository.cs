using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

public interface IUserRepository<TUserEntity> : IRepository
    where TUserEntity : EntityBase, IUser
{
    IAsyncEnumerable<TUserEntity> GetAsync();
    Task<TUserEntity> GetAsync(string identity);
    Task<TUserEntity> GetByKeyAsync(string userKey);

    /// <summary>The users with these keys, each once; a key no user has is left out.</summary>
    /// <remarks>A default interface method that asks for one key at a time, so a host's own repository keeps compiling; the built-in one asks the database once.</remarks>
    async Task<TUserEntity[]> GetByKeysAsync(IReadOnlyCollection<string> userKeys)
    {
        var found = new List<TUserEntity>();

        foreach (var userKey in (userKeys ?? []).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal))
        {
            var user = await GetByKeyAsync(userKey);
            if (user != null) found.Add(user);
        }

        return [.. found];
    }

    /// <summary>
    /// Every user whose <see cref="IUser.EMail"/> is <paramref name="email"/>, compared trimmed and ignoring
    /// case; empty for a blank address.
    /// </summary>
    /// <remarks>
    /// <b>Every match, not the first.</b> Nothing makes an address unique — the built-in collection indexes
    /// only <c>Identity</c> — and a caller deciding whose mail this is has to know when two accounts claim it.
    /// <para>
    /// A default interface method that reads every user, so a host's own repository keeps compiling and
    /// answers correctly, if slowly; the built-in one asks the database.
    /// </para>
    /// </remarks>
    async Task<TUserEntity[]> GetByEMailAsync(string email)
    {
        var wanted = email?.Trim();
        if (string.IsNullOrEmpty(wanted)) return [];

        var found = new List<TUserEntity>();
        await foreach (var user in GetAsync())
        {
            if (string.Equals(user.EMail?.Trim(), wanted, StringComparison.OrdinalIgnoreCase)) found.Add(user);
        }

        return [.. found];
    }

    /// <summary>The users whose name or address holds <paramref name="text"/>, ignoring case, that are not disabled, as they are read; nothing for empty text.</summary>
    /// <remarks>
    /// A stream, so a caller filtering further stops reading when it has enough. A default interface method that reads
    /// every user, so a host's own repository keeps compiling and answers correctly, if slowly; the built-in one asks
    /// the database.
    /// </remarks>
    async IAsyncEnumerable<TUserEntity> SearchAsync(string text)
    {
        var wanted = text?.Trim();
        if (string.IsNullOrEmpty(wanted)) yield break;

        await foreach (var user in GetAsync())
        {
            if (user.DisabledAt != null) continue;
            if (user.Name?.Contains(wanted, StringComparison.OrdinalIgnoreCase) == true || user.EMail?.Contains(wanted, StringComparison.OrdinalIgnoreCase) == true) yield return user;
        }
    }

    Task AddAsync(TUserEntity user);
    Task SetNameAsync(string userKey, string name);

    /// <summary>
    /// Stamps <see cref="IUser.LastSeen"/> on the user document. Called automatically by the throttled
    /// resolve path, so the default (and the built-in repository, when <typeparamref name="TUserEntity"/>
    /// does not declare a <c>LastSeen</c> property) is a no-op — activity tracking is opted into by
    /// declaring the property on the entity.
    /// </summary>
    Task SetLastSeenAsync(string userKey, DateTime lastSeen) => Task.CompletedTask;

    /// <summary>
    /// Sets <see cref="IUser.DirectoryId"/> on the user document. Same opt-in-by-entity-shape contract
    /// as <see cref="SetLastSeenAsync"/>: a no-op unless the entity declares a <c>DirectoryId</c> property.
    /// </summary>
    Task SetDirectoryIdAsync(string userKey, string directoryId) => Task.CompletedTask;

    /// <summary>
    /// Sets <see cref="IUser.Icon"/> (the icon reference) on the user document. Same opt-in-by-entity-shape
    /// contract as <see cref="SetDirectoryIdAsync"/>: a no-op unless the entity declares an <c>Icon</c> property.
    /// </summary>
    Task SetIconAsync(string userKey, string reference) => Task.CompletedTask;

    /// <summary>
    /// Sets <see cref="IUser.EMail"/> on the user document.
    /// </summary>
    /// <remarks>
    /// Declared with a default implementation so existing custom repositories keep compiling. The default
    /// throws rather than no-opping, for the reason <see cref="DeleteAsync"/> gives: an address that was asked
    /// to change and silently did not is a missing implementation dressed as success.
    /// </remarks>
    Task SetEMailAsync(string userKey, string email)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(SetEMailAsync)}. Implement it to support " +
            "changing a user's email address.");

    /// <summary>
    /// Deletes the user document.
    /// </summary>
    /// <remarks>
    /// Declared with a default implementation so existing custom repositories keep compiling. The default
    /// throws rather than no-opping: silently skipping a requested deletion would hide the missing
    /// implementation behind an apparently successful call.
    /// </remarks>
    Task DeleteAsync(string userKey)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(DeleteAsync)}. Implement it to support " +
            $"user deletion (the '{SystemUserScopes.Manage}' system scope).");
}