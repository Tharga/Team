namespace Tharga.Team;

/// <summary>
/// The sign-in identities the directory holds for one directory user, as returned by
/// <see cref="IUserIdentityDirectory.GetIdentitiesByIdsAsync"/>.
/// </summary>
/// <param name="DirectoryId">The directory's id for the user.</param>
/// <param name="Identities">Every sign-in identity, in directory order; empty when the user has none.</param>
public sealed record DirectoryUserIdentities(string DirectoryId, DirectoryUserIdentity[] Identities);
