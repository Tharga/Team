using System.Net.Mail;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// Default <see cref="IUserManagementService"/> implementation. Directory operations require a registered
/// <see cref="IUserDirectoryService"/>; deletion composes the storage seams — remove from all teams, then
/// delete the user record — and only then attempts the (opt-in) directory delete, so a directory failure
/// never leaves the local store half-deleted. Authorization and audit are applied by decorators.
/// </summary>
public class UserManagementService : IUserManagementService
{
    private readonly IUserService _userService;
    private readonly ITeamService _teamService;
    private readonly IUserDirectoryService _directoryService;
    private readonly bool _writeNameToDirectory;
    private readonly IUserIdentityDirectory _identityDirectory;
    private readonly IApiKeyAdministrationService _apiKeys;
    private readonly PrivateKeyOwnerRemovedAction _ownerRemovedAction;
    private readonly ILogger<UserManagementService> _logger;

    /// <param name="userService">The user store.</param>
    /// <param name="teamService">The team store, used to remove memberships and to find owned teams.</param>
    /// <param name="directoryService">The external directory, when one is registered.</param>
    /// <param name="writeNameToDirectory">
    /// Whether an administrative rename also writes the name back to the directory. Default off: which
    /// side owns display names is a per-host decision, and a host federating from a corporate directory
    /// wants the directory authoritative.
    /// </param>
    /// <param name="identityDirectory">The directory's sign-in identity reader, when one is registered.</param>
    /// <param name="apiKeys">The API key store, when API keys are registered, used to clean up a deleted user's personal system keys.</param>
    /// <param name="ownerRemovedAction">Whether those keys are deleted or disabled; see <see cref="ApiKeyOptions.OwnerRemovedAction"/>.</param>
    /// <param name="logger">Where a failed key cleanup is reported, since it never fails the deletion.</param>
    public UserManagementService(IUserService userService, ITeamService teamService, IUserDirectoryService directoryService = null, bool writeNameToDirectory = false, IUserIdentityDirectory identityDirectory = null, IApiKeyAdministrationService apiKeys = null, PrivateKeyOwnerRemovedAction ownerRemovedAction = PrivateKeyOwnerRemovedAction.Delete, ILogger<UserManagementService> logger = null)
    {
        _logger = logger;
        _userService = userService;
        _teamService = teamService;
        _directoryService = directoryService;
        _writeNameToDirectory = writeNameToDirectory;
        _identityDirectory = identityDirectory;
        _apiKeys = apiKeys;
        _ownerRemovedAction = ownerRemovedAction;
    }

    public async Task<UserSignInIdentities[]> GetSignInIdentitiesAsync(IEnumerable<string> userKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userKeys);
        if (_identityDirectory == null) return [];

        // Directory id back to user key. Unlinked users are not asked about: an empty directory id is not a
        // user the directory can answer for, and "not linked" is already known to the caller.
        var userKeysByDirectoryId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var userKey in userKeys.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal))
        {
            var user = await _userService.GetUserByKeyAsync(userKey);
            if (!string.IsNullOrEmpty(user?.DirectoryId)) userKeysByDirectoryId.TryAdd(user.DirectoryId, user.Key);
        }

        if (userKeysByDirectoryId.Count == 0) return [];

        var identities = await _identityDirectory.GetIdentitiesByIdsAsync(userKeysByDirectoryId.Keys, cancellationToken);
        return identities
            .Where(x => userKeysByDirectoryId.ContainsKey(x.DirectoryId))
            .Select(x => new UserSignInIdentities(userKeysByDirectoryId[x.DirectoryId], x.Identities))
            .ToArray();
    }

    public async Task<UserNameChangeResult> SetUserNameAsync(string userKey, string name, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userKey);

        // Local first and unconditionally: this application is the system of record for the name, so a
        // directory that cannot be reached must not stop it being corrected here.
        await _userService.SetUserNameAsync(user.Key, name);

        if (!_writeNameToDirectory || _directoryService == null) return new UserNameChangeResult();

        // Not an error: an unlinked user has no directory account to write to. Reporting it as a failure
        // would make the ordinary case look broken.
        if (string.IsNullOrEmpty(user.DirectoryId)) return new UserNameChangeResult();

        try
        {
            await _directoryService.SetUserNameAsync(user.DirectoryId, name, cancellationToken);
            return new UserNameChangeResult(DirectoryUpdated: true);
        }
        catch (Exception ex)
        {
            return new UserNameChangeResult(DirectoryError: ex.Message);
        }
    }

    public async Task<UserEmailChangeResult> SetUserEmailAsync(string userKey, string email, CancellationToken cancellationToken = default)
    {
        var address = PlainAddress(email);
        var user = await RequireUserAsync(userKey);

        await _userService.SetUserEmailAsync(user.Key, address);

        return new UserEmailChangeResult(user.EMail);
    }

    /// <summary>The address trimmed, when it is one plain address: not empty, not a list, and without a display name or comment around it.</summary>
    private static string PlainAddress(string email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException("An email address is required.", nameof(email));

        if (!MailAddress.TryCreate(trimmed, out var parsed) || parsed.Address != trimmed)
            throw new ArgumentException("The email address is not a single, plain address.", nameof(email));

        return trimmed;
    }

    public async Task<string> UseDirectoryNameAsync(string userKey, CancellationToken cancellationToken = default)
    {
        var directory = RequireDirectoryService();
        var user = await RequireUserAsync(userKey);

        var result = await directory.VerifyUserAsync(user, cancellationToken);
        await RelinkAsync(user, result);

        if (result.Status is not (DirectoryUserStatus.Found or DirectoryUserStatus.Disabled))
            throw new InvalidOperationException($"User '{userKey}' was not found in the directory.");
        if (DirectoryNames.IsPlaceholder(result.DirectoryName))
            throw new InvalidOperationException($"The directory holds no real name for user '{userKey}'.");

        var name = result.DirectoryName.Trim();
        await _userService.SetUserNameAsync(user.Key, name);
        return name;
    }

    public async Task WriteNameToDirectoryAsync(string userKey, CancellationToken cancellationToken = default)
    {
        var directory = RequireDirectoryService();
        if (!_writeNameToDirectory)
            throw new InvalidOperationException("Writing names to the directory is not enabled. Set o.Blazor.WriteNameToDirectory to allow it.");

        var user = await RequireUserAsync(userKey);
        if (string.IsNullOrEmpty(user.DirectoryId))
            throw new InvalidOperationException($"User '{userKey}' is not linked to a directory account. Verify the user first.");
        if (DirectoryNames.IsPlaceholder(user.Name))
            throw new InvalidOperationException($"User '{userKey}' has no name to write to the directory.");

        await directory.SetUserNameAsync(user.DirectoryId, user.Name.Trim(), cancellationToken);
    }

    public async Task<DirectoryVerificationResult> VerifyUserAsync(string userKey, CancellationToken cancellationToken = default)
    {
        var directory = RequireDirectoryService();
        var user = await RequireUserAsync(userKey);

        var result = await directory.VerifyUserAsync(user, cancellationToken);
        await RelinkAsync(user, result);

        return result;
    }

    public async IAsyncEnumerable<UserVerificationResult> VerifyAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var directory = RequireDirectoryService();

        await foreach (var user in _userService.GetAsync().WithCancellation(cancellationToken))
        {
            var result = await directory.VerifyUserAsync(user, cancellationToken);
            await RelinkAsync(user, result);
            yield return new UserVerificationResult(user.Key, result);
        }
    }

    public async Task<UserDeleteResult> DeleteUserAsync(string userKey, bool deleteFromDirectory = false, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userKey);

        // The directory id must be resolved before the local record is gone (email fallback reads it).
        string directoryId = null;
        string directoryError = null;
        if (deleteFromDirectory)
        {
            if (_directoryService == null)
            {
                directoryError = "No user directory service is registered.";
            }
            else
            {
                directoryId = user.DirectoryId;
                if (string.IsNullOrEmpty(directoryId))
                {
                    var verification = await _directoryService.VerifyUserAsync(user, cancellationToken);
                    directoryId = verification?.DirectoryId;
                }

                if (string.IsNullOrEmpty(directoryId))
                {
                    directoryError = "The user could not be matched to a directory user.";
                }
            }
        }

        // Deliberately proceeds when the user is a team's sole owner, rather than refusing. The warning
        // belongs at the confirmation, where GetOwnedTeamsAsync surfaces it and ownership can still be
        // transferred; refusing here would block legitimate cases such as winding up a one-person team,
        // and the state is now repairable through SystemTeamScopes.SetOwner.
        var removedTeamCount = await _teamService.RemoveUserFromAllTeamsAsync(user.Key);
        await _userService.DeleteUserAsync(user.Key);
        await RemovePersonalSystemKeysAsync(user.Key);

        var directoryDeleted = false;
        if (deleteFromDirectory && directoryError == null)
        {
            try
            {
                await _directoryService.DeleteUserAsync(directoryId, cancellationToken);
                directoryDeleted = true;
            }
            catch (Exception ex)
            {
                directoryError = ex.Message;
            }
        }

        return new UserDeleteResult(directoryDeleted, directoryError, removedTeamCount);
    }

    /// <summary>
    /// Deletes, or disables, a deleted user's personal system keys, per <see cref="ApiKeyOptions.OwnerRemovedAction"/>.
    /// </summary>
    /// <remarks>
    /// <b>Hygiene, not the security fix.</b> Authentication already refuses a personal system key whose owner
    /// no longer exists. This keeps the keys from being listed once their owner is gone. The user's personal
    /// team keys are handled by the member-removal participants, as each membership ends above.
    /// <para>
    /// <b>Never throws</b>, like <see cref="TeamMemberRemovalCascade"/>: it runs after the user record is gone,
    /// so a failure here cannot be reported as a failed deletion without telling the caller something untrue.
    /// It is logged instead.
    /// </para>
    /// </remarks>
    private async Task RemovePersonalSystemKeysAsync(string userKey)
    {
        if (_apiKeys == null) return;

        try
        {
            // Collected first: acting while enumerating would change the sequence being read.
            var owned = new List<IApiKey>();
            await foreach (var key in _apiKeys.GetSystemKeysAsync())
            {
                if (string.Equals(key.OwnerMemberKey, userKey, StringComparison.Ordinal)) owned.Add(key);
            }

            foreach (var key in owned)
            {
                if (_ownerRemovedAction == PrivateKeyOwnerRemovedAction.Disable)
                {
                    if (key.DisabledAt == null) await _apiKeys.SetSystemKeyDisabledAsync(key.Key, true, "Owner deleted");
                }
                else
                {
                    await _apiKeys.DeleteSystemKeyAsync(key.Key);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Deleting user {UserKey} could not clean up their personal system API keys. The deletion stands.", userKey);
        }
    }

    /// <remarks>
    /// The self-disable refusal lives here, not only in <c>UserAdminGate</c>. A host can inject its own
    /// entry through <c>ActionItems</c> and dispatch straight to this method, so a UI-only guard is one
    /// the server never applies — the same lesson the delete guard learned.
    /// </remarks>
    public async Task SetUserDisabledAsync(string userKey, bool disabled, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userKey);
        var caller = await _userService.GetCurrentUserAsync();

        if (disabled && caller != null && string.Equals(caller.Key, user.Key, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "You cannot disable your own account. Ask another administrator to do it.");
        }

        await _userService.SetUserDisabledAsync(
            user.Key,
            disabled ? DateTime.UtcNow : null,
            disabled ? caller?.Key : null);
    }

    public Task<IReadOnlyList<ITeam>> GetOwnedTeamsAsync(string userKey, CancellationToken cancellationToken = default)
        => _teamService.GetTeamsForUserWithAccessLevelAsync(userKey, AccessLevel.Owner);

    public async IAsyncEnumerable<DirectoryUser> GetDirectoryOnlyUsersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var directory = RequireDirectoryService();

        var directoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var user in _userService.GetAsync().WithCancellation(cancellationToken))
        {
            if (!string.IsNullOrEmpty(user.DirectoryId)) directoryIds.Add(user.DirectoryId);
            if (!string.IsNullOrEmpty(user.EMail)) emails.Add(user.EMail);
        }

        await foreach (var directoryUser in directory.GetUsersAsync(cancellationToken))
        {
            if (!string.IsNullOrEmpty(directoryUser.DirectoryId) && directoryIds.Contains(directoryUser.DirectoryId)) continue;
            if (!string.IsNullOrEmpty(directoryUser.EMail) && emails.Contains(directoryUser.EMail)) continue;
            yield return directoryUser;
        }
    }

    private async Task RelinkAsync(IUser user, DirectoryVerificationResult result)
    {
        if (string.IsNullOrEmpty(result?.DirectoryId)) return;
        if (string.Equals(result.DirectoryId, user.DirectoryId, StringComparison.OrdinalIgnoreCase)) return;

        await _userService.SetUserDirectoryIdAsync(user.Key, result.DirectoryId);
    }

    private IUserDirectoryService RequireDirectoryService()
        => _directoryService ?? throw new NotSupportedException(
            $"No {nameof(IUserDirectoryService)} is registered. Register one to use directory features.");

    private async Task<IUser> RequireUserAsync(string userKey)
    {
        var user = await _userService.GetUserByKeyAsync(userKey);
        return user ?? throw new InvalidOperationException($"User '{userKey}' was not found.");
    }
}
