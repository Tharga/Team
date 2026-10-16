namespace Tharga.Team;

/// <summary>
/// The directory's sign-in identities for one local user, as returned by
/// <see cref="IUserManagementService.GetSignInIdentitiesAsync"/>.
/// </summary>
/// <param name="UserKey">The local user's key.</param>
/// <param name="Identities">Every sign-in identity the directory holds for the user, in directory order.</param>
public sealed record UserSignInIdentities(string UserKey, DirectoryUserIdentity[] Identities);
