using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

public interface IUserRepository<TUserEntity> : IRepository
    where TUserEntity : EntityBase, IUser
{
    IAsyncEnumerable<TUserEntity> GetAsync();
    Task<TUserEntity> GetAsync(string identity);
    Task<TUserEntity> GetByKeyAsync(string userKey);
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