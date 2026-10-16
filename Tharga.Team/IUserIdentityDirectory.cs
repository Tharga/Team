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

    /// <summary>
    /// Returns the sign-in identities of several directory users at once, one entry per distinct id the
    /// directory knows. An id the directory does not know is left out rather than failing the whole read,
    /// and null or empty ids are ignored.
    /// </summary>
    /// <remarks>
    /// The default implementation calls <see cref="GetIdentitiesAsync(string, CancellationToken)"/> once
    /// per id, so an existing directory keeps working unchanged. A directory that can read many users in
    /// one request should override it; a list surface relies on this to keep its call count bounded.
    /// </remarks>
    async Task<DirectoryUserIdentities[]> GetIdentitiesByIdsAsync(IEnumerable<string> directoryIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directoryIds);

        var result = new List<DirectoryUserIdentities>();
        foreach (var directoryId in directoryIds.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal))
        {
            IReadOnlyList<DirectoryUserIdentity> identities;
            try
            {
                identities = await GetIdentitiesAsync(directoryId, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // The single read's contract for an unknown id. One missing user must not hide the others.
                continue;
            }

            result.Add(new DirectoryUserIdentities(directoryId, identities?.ToArray() ?? []));
        }

        return result.ToArray();
    }
}
