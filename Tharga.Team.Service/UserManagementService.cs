using System.Runtime.CompilerServices;
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

    /// <param name="userService">The user store.</param>
    /// <param name="teamService">The team store, used to remove memberships and to find owned teams.</param>
    /// <param name="directoryService">The external directory, when one is registered.</param>
    /// <param name="writeNameToDirectory">
    /// Whether an administrative rename also writes the name back to the directory. Default off: which
    /// side owns display names is a per-host decision, and a host federating from a corporate directory
    /// wants the directory authoritative.
    /// </param>
    public UserManagementService(IUserService userService, ITeamService teamService, IUserDirectoryService directoryService = null, bool writeNameToDirectory = false)
    {
        _userService = userService;
        _teamService = teamService;
        _directoryService = directoryService;
        _writeNameToDirectory = writeNameToDirectory;
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
