using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// Decorator that wraps <see cref="IApiKeyAdministrationService"/> and logs audit entries
/// for all mutation operations via <see cref="CompositeAuditLogger"/>.
/// Read operations are passed through without logging.
/// </summary>
public class AuditingApiKeyServiceDecorator : IApiKeyAdministrationService
{
    private readonly IApiKeyAdministrationService _inner;
    private readonly CompositeAuditLogger _auditLogger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private const string Feature = "apikey";

    public AuditingApiKeyServiceDecorator(IApiKeyAdministrationService inner, CompositeAuditLogger auditLogger, IHttpContextAccessor httpContextAccessor)
    {
        _inner = inner;
        _auditLogger = auditLogger;
        _httpContextAccessor = httpContextAccessor;
    }

    // Read operations — pass through

    public Task<IApiKey> GetByApiKeyAsync(string apiKey) => _inner.GetByApiKeyAsync(apiKey);
    public IAsyncEnumerable<IApiKey> GetKeysAsync(string teamKey) => _inner.GetKeysAsync(teamKey);

    // Mutation operations — log audit entries

    public async Task<IApiKey> CreateKeyAsync(string teamKey, string name, AccessLevel accessLevel, string[] roles = null, string[] scopeOverrides = null, DateTime? expiryDate = null, IReadOnlyList<Tag> tags = null, string createdBy = null, string ownerMemberKey = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.CreateKeyAsync(teamKey, name, accessLevel, roles, scopeOverrides, expiryDate, tags, createdBy, ownerMemberKey);
            sw.Stop();
            Log("create", nameof(CreateKeyAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("create", nameof(CreateKeyAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey);
            throw;
        }
    }

    public async Task<IApiKey> RefreshKeyAsync(string teamKey, string key)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.RefreshKeyAsync(teamKey, key);
            sw.Stop();
            Log("refresh", nameof(RefreshKeyAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("refresh", nameof(RefreshKeyAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey);
            throw;
        }
    }

    public async Task LockKeyAsync(string teamKey, string key)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.LockKeyAsync(teamKey, key);
            sw.Stop();
            Log("lock", nameof(LockKeyAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("lock", nameof(LockKeyAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey);
            throw;
        }
    }

    /// <remarks>
    /// Both directions are audited, and under distinct actions: <c>disable</c> is a containment and
    /// <c>enable</c> is a decision to trust the key again. Rolling them into one entry keyed on a boolean
    /// would make "who re-enabled this, and when" a query rather than a reading.
    /// </remarks>
    public async Task SetKeyDisabledAsync(string teamKey, string key, bool disabled, string actor = null)
    {
        var action = disabled ? "disable" : "enable";
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetKeyDisabledAsync(teamKey, key, disabled, actor);
            sw.Stop();
            Log(action, nameof(SetKeyDisabledAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log(action, nameof(SetKeyDisabledAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey);
            throw;
        }
    }

    /// <inheritdoc cref="SetKeyDisabledAsync"/>
    public async Task SetSystemKeyDisabledAsync(string key, bool disabled, string actor = null)
    {
        var action = disabled ? "disable-system" : "enable-system";
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetSystemKeyDisabledAsync(key, disabled, actor);
            sw.Stop();
            Log(action, nameof(SetSystemKeyDisabledAsync), sw.ElapsedMilliseconds, true);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log(action, nameof(SetSystemKeyDisabledAsync), sw.ElapsedMilliseconds, false, ex.Message);
            throw;
        }
    }

    public async Task DeleteKeyAsync(string teamKey, string key)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.DeleteKeyAsync(teamKey, key);
            sw.Stop();
            Log("delete", nameof(DeleteKeyAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("delete", nameof(DeleteKeyAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey);
            throw;
        }
    }

    public async Task SetScopeOverridesAsync(string teamKey, string key, string[] scopes)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetScopeOverridesAsync(teamKey, key, scopes);
            sw.Stop();
            Log("set-scope-overrides", nameof(SetScopeOverridesAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("set-scope-overrides", nameof(SetScopeOverridesAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey);
            throw;
        }
    }

    public async Task SetRolesAsync(string teamKey, string key, string[] roles)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetRolesAsync(teamKey, key, roles);
            sw.Stop();
            Log("set-roles", nameof(SetRolesAsync), sw.ElapsedMilliseconds, true, teamKey: teamKey);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("set-roles", nameof(SetRolesAsync), sw.ElapsedMilliseconds, false, ex.Message, teamKey);
            throw;
        }
    }

    // System key operations

    public IAsyncEnumerable<IApiKey> GetSystemKeysAsync() => _inner.GetSystemKeysAsync();

    public async Task<IApiKey> CreateSystemKeyAsync(string name, string[] scopes, DateTime? expiryDate = null, string createdBy = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.CreateSystemKeyAsync(name, scopes, expiryDate, createdBy);
            sw.Stop();
            LogSystem("create", nameof(CreateSystemKeyAsync), sw.ElapsedMilliseconds, true);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogSystem("create", nameof(CreateSystemKeyAsync), sw.ElapsedMilliseconds, false, ex.Message);
            throw;
        }
    }

    public async Task<IApiKey> CreatePersonalSystemKeyAsync(string name, string[] scopes, string ownerUserKey, DateTime? expiryDate = null, string createdBy = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.CreatePersonalSystemKeyAsync(name, scopes, ownerUserKey, expiryDate, createdBy);
            sw.Stop();
            LogSystem("create", nameof(CreatePersonalSystemKeyAsync), sw.ElapsedMilliseconds, true);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogSystem("create", nameof(CreatePersonalSystemKeyAsync), sw.ElapsedMilliseconds, false, ex.Message);
            throw;
        }
    }

    public async Task<IApiKey> RefreshSystemKeyAsync(string key)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.RefreshSystemKeyAsync(key);
            sw.Stop();
            LogSystem("refresh", nameof(RefreshSystemKeyAsync), sw.ElapsedMilliseconds, true);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogSystem("refresh", nameof(RefreshSystemKeyAsync), sw.ElapsedMilliseconds, false, ex.Message);
            throw;
        }
    }

    public async Task LockSystemKeyAsync(string key)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.LockSystemKeyAsync(key);
            sw.Stop();
            LogSystem("lock", nameof(LockSystemKeyAsync), sw.ElapsedMilliseconds, true);
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogSystem("lock", nameof(LockSystemKeyAsync), sw.ElapsedMilliseconds, false, ex.Message);
            throw;
        }
    }

    public async Task DeleteSystemKeyAsync(string key)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.DeleteSystemKeyAsync(key);
            sw.Stop();
            LogSystem("delete", nameof(DeleteSystemKeyAsync), sw.ElapsedMilliseconds, true);
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogSystem("delete", nameof(DeleteSystemKeyAsync), sw.ElapsedMilliseconds, false, ex.Message);
            throw;
        }
    }

    private void Log(string action, string methodName, long durationMs, bool success, string errorMessage = null, string teamKey = null)
    {
        var entry = AuditHelper.BuildEntry(_httpContextAccessor, Feature, action, methodName, durationMs, success, errorMessage, teamKey);
        _auditLogger.Log(entry);
    }

    private void LogSystem(string action, string methodName, long durationMs, bool success, string errorMessage = null)
    {
        var entry = AuditHelper.BuildEntry(_httpContextAccessor, Feature, action, methodName, durationMs, success, errorMessage, teamKey: null);
        entry = entry with
        {
            Metadata = new Dictionary<string, string> { { "ApiKeyType", "System" } }
        };
        _auditLogger.Log(entry);
    }
}
