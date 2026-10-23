using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Tharga.Team;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// Decorator that wraps <see cref="IUserManagementService"/> and logs audit entries for user
/// administration: per-user verification (with outcome), bulk verification (one summary entry), and
/// deletion (with team count and directory result). The directory-only listing is a read with no side
/// effect and is not audited, consistent with team enumeration.
/// </summary>
public class AuditingUserManagementServiceDecorator : IUserManagementService
{
    private readonly IUserManagementService _inner;
    private readonly CompositeAuditLogger _auditLogger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private const string Feature = "user";

    public AuditingUserManagementServiceDecorator(IUserManagementService inner, CompositeAuditLogger auditLogger, IHttpContextAccessor httpContextAccessor)
    {
        _inner = inner;
        _auditLogger = auditLogger;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <remarks>
    /// A read that runs when a delete confirmation opens, so it is not audited — logging it would record
    /// an entry for looking at a dialog the operator may then cancel. The delete it precedes is audited.
    /// </remarks>
    public Task<IReadOnlyList<ITeam>> GetOwnedTeamsAsync(string userKey, CancellationToken cancellationToken = default)
        => _inner.GetOwnedTeamsAsync(userKey, cancellationToken);

    public async Task<DirectoryVerificationResult> VerifyUserAsync(string userKey, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.VerifyUserAsync(userKey, cancellationToken);
            sw.Stop();
            Log("verify", nameof(VerifyUserAsync), sw.ElapsedMilliseconds, true, metadata: Meta(
                (AuditMetadataKeys.UserKey, userKey),
                (AuditMetadataKeys.DirectoryStatus, result?.Status.ToString())));
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("verify", nameof(VerifyUserAsync), sw.ElapsedMilliseconds, false, ex.Message,
                metadata: Meta((AuditMetadataKeys.UserKey, userKey)));
            throw;
        }
    }

    public async IAsyncEnumerable<UserVerificationResult> VerifyAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var count = 0;
        var success = false;
        try
        {
            await foreach (var result in _inner.VerifyAllAsync(cancellationToken))
            {
                count++;
                yield return result;
            }

            success = true;
        }
        finally
        {
            sw.Stop();
            Log("verify-all", nameof(VerifyAllAsync), sw.ElapsedMilliseconds, success,
                metadata: Meta((AuditMetadataKeys.VerifiedCount, count.ToString())));
        }
    }

    public async Task<UserDeleteResult> DeleteUserAsync(string userKey, bool deleteFromDirectory = false, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.DeleteUserAsync(userKey, deleteFromDirectory, cancellationToken);
            sw.Stop();
            Log("delete", nameof(DeleteUserAsync), sw.ElapsedMilliseconds, true, metadata: Meta(
                (AuditMetadataKeys.UserKey, userKey),
                (AuditMetadataKeys.MemberTeamCount, result?.RemovedTeamCount.ToString()),
                (AuditMetadataKeys.DirectoryDeleted, deleteFromDirectory ? result?.DirectoryDeleted.ToString() : null),
                (AuditMetadataKeys.DirectoryError, result?.DirectoryError)));
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("delete", nameof(DeleteUserAsync), sw.ElapsedMilliseconds, false, ex.Message,
                metadata: Meta((AuditMetadataKeys.UserKey, userKey)));
            throw;
        }
    }

    /// <remarks>
    /// Both directions, under distinct actions — <c>disable</c> is a containment, <c>enable</c> is a
    /// decision to let the person back in. One entry keyed on a boolean would make "who re-enabled this"
    /// a query rather than a reading. The refusal to disable oneself is audited as a failure too: an
    /// administrator repeatedly trying it is worth seeing.
    /// </remarks>
    public async Task SetUserDisabledAsync(string userKey, bool disabled, CancellationToken cancellationToken = default)
    {
        var action = disabled ? "disable" : "enable";
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetUserDisabledAsync(userKey, disabled, cancellationToken);
            sw.Stop();
            Log(action, nameof(SetUserDisabledAsync), sw.ElapsedMilliseconds, true,
                metadata: Meta((AuditMetadataKeys.UserKey, userKey)));
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log(action, nameof(SetUserDisabledAsync), sw.ElapsedMilliseconds, false, ex.Message,
                metadata: Meta((AuditMetadataKeys.UserKey, userKey)));
            throw;
        }
    }

    /// <remarks>
    /// The directory outcome is recorded rather than just the local write: a rename that succeeded here
    /// and failed there is the case someone will later need to explain, and it is invisible otherwise.
    /// </remarks>
    public async Task<UserNameChangeResult> SetUserNameAsync(string userKey, string name, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.SetUserNameAsync(userKey, name, cancellationToken);
            sw.Stop();
            Log("set-user-name", nameof(SetUserNameAsync), sw.ElapsedMilliseconds, true, metadata: Meta(
                (AuditMetadataKeys.UserKey, userKey),
                (AuditMetadataKeys.UserNameNew, name),
                (AuditMetadataKeys.DirectoryError, result?.DirectoryError)));
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("set-user-name", nameof(SetUserNameAsync), sw.ElapsedMilliseconds, false, ex.Message,
                metadata: Meta((AuditMetadataKeys.UserKey, userKey)));
            throw;
        }
    }

    /// <remarks>
    /// Both the address replaced and the new one are recorded: it is where the user's mail goes and what an
    /// invitation to them matches, so "what was it changed from" is what someone investigating asks. A refusal
    /// is audited as a failure too, without the address it was given.
    /// </remarks>
    public async Task<UserEmailChangeResult> SetUserEmailAsync(string userKey, string email, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.SetUserEmailAsync(userKey, email, cancellationToken);
            sw.Stop();
            Log("set-user-email", nameof(SetUserEmailAsync), sw.ElapsedMilliseconds, true, metadata: Meta(
                (AuditMetadataKeys.UserKey, userKey),
                (AuditMetadataKeys.UserEmailOld, result?.Previous),
                (AuditMetadataKeys.UserEmailNew, email?.Trim())));
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("set-user-email", nameof(SetUserEmailAsync), sw.ElapsedMilliseconds, false, ex.Message,
                metadata: Meta((AuditMetadataKeys.UserKey, userKey)));
            throw;
        }
    }

    public IAsyncEnumerable<DirectoryUser> GetDirectoryOnlyUsersAsync(CancellationToken cancellationToken = default)
        => _inner.GetDirectoryOnlyUsersAsync(cancellationToken);

    public Task<UserSignInIdentities[]> GetSignInIdentitiesAsync(IEnumerable<string> userKeys, CancellationToken cancellationToken = default)
        => _inner.GetSignInIdentitiesAsync(userKeys, cancellationToken);

    private static Dictionary<string, string> Meta(params (string Key, string Value)[] pairs)
    {
        var metadata = new Dictionary<string, string>();
        foreach (var (key, value) in pairs)
        {
            if (value != null) metadata[key] = value;
        }
        return metadata;
    }

    private void Log(string action, string methodName, long durationMs, bool success, string errorMessage = null, IReadOnlyDictionary<string, string> metadata = null)
    {
        var entry = AuditHelper.BuildEntry(_httpContextAccessor, Feature, action, methodName, durationMs, success, errorMessage, teamKey: null, metadata);
        _auditLogger.Log(entry);
    }
}
