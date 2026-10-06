namespace Tharga.Team;

/// <summary>
/// Reads the sign-in identities a directory holds for a user, so a host can link a directory user to an
/// identity in another system. Unchecked, like <see cref="IUserDirectoryService"/>: call it from
/// server-side code that has already authorized the caller.
/// </summary>
public interface IUserIdentityDirectory
{
    /// <summary>
    /// Returns every sign-in identity the directory holds for the user, in directory order, or an empty
    /// list when it holds none.
    /// </summary>
    Task<IReadOnlyList<DirectoryUserIdentity>> GetIdentitiesAsync(string directoryId, CancellationToken cancellationToken = default);
}
