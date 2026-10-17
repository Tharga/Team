using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// Decorator that wraps <see cref="IApiKeyAdministrationService"/> and notifies the registered
/// <see cref="IApiKeyLifecycleHandler"/>(s) after a key is created, recycled, or deleted. The raw
/// private token (available on the create/refresh result) is forwarded on create/recycle; delete is
/// signalled tokenless. Read/lock/scope/role operations pass through without notification.
/// <para>
/// Handlers run after the underlying mutation succeeds; a handler exception propagates out of the
/// originating operation (capture failures are not swallowed). The token is never logged.
/// </para>
/// </summary>
public class ApiKeyLifecycleDecorator : IApiKeyAdministrationService
{
    private readonly IApiKeyAdministrationService _inner;
    private readonly IReadOnlyList<IApiKeyLifecycleHandler> _handlers;

    public ApiKeyLifecycleDecorator(IApiKeyAdministrationService inner, IEnumerable<IApiKeyLifecycleHandler> handlers)
    {
        _inner = inner;
        _handlers = handlers?.ToArray() ?? [];
    }

    // Read operations — pass through

    public Task<IApiKey> GetByApiKeyAsync(string apiKey) => _inner.GetByApiKeyAsync(apiKey);
    public IAsyncEnumerable<IApiKey> GetKeysAsync(string teamKey) => _inner.GetKeysAsync(teamKey);
    public IAsyncEnumerable<IApiKey> GetSystemKeysAsync() => _inner.GetSystemKeysAsync();

    // Pass-through mutations (no token change, no lifecycle signal)

    public Task LockKeyAsync(string teamKey, string key) => _inner.LockKeyAsync(teamKey, key);
    public Task LockSystemKeyAsync(string key) => _inner.LockSystemKeyAsync(key);

    // No lifecycle signal: disabling neither mints nor destroys a secret, so a handler capturing tokens
    // has nothing to capture and a handler cleaning up after deletion has nothing to clean up.
    public Task SetKeyDisabledAsync(string teamKey, string key, bool disabled, string actor = null) => _inner.SetKeyDisabledAsync(teamKey, key, disabled, actor);
    public Task SetSystemKeyDisabledAsync(string key, bool disabled, string actor = null) => _inner.SetSystemKeyDisabledAsync(key, disabled, actor);
    public Task SetScopeOverridesAsync(string teamKey, string key, string[] scopes) => _inner.SetScopeOverridesAsync(teamKey, key, scopes);
    public Task SetRolesAsync(string teamKey, string key, string[] roles) => _inner.SetRolesAsync(teamKey, key, roles);

    // Create / recycle — forward the private token

    public async Task<IApiKey> CreateKeyAsync(string teamKey, string name, AccessLevel accessLevel, string[] roles = null, string[] scopeOverrides = null, DateTime? expiryDate = null, IReadOnlyList<Tag> tags = null, string createdBy = null, string ownerMemberKey = null)
    {
        var result = await _inner.CreateKeyAsync(teamKey, name, accessLevel, roles, scopeOverrides, expiryDate, tags, createdBy, ownerMemberKey);
        await NotifyAsync(ApiKeyLifecycleReason.Created, result);
        return result;
    }

    public async Task<IApiKey> RefreshKeyAsync(string teamKey, string key)
    {
        var result = await _inner.RefreshKeyAsync(teamKey, key);
        await NotifyAsync(ApiKeyLifecycleReason.Recycled, result);
        return result;
    }

    public async Task<IApiKey> CreateSystemKeyAsync(string name, string[] scopes, DateTime? expiryDate = null, string createdBy = null)
    {
        var result = await _inner.CreateSystemKeyAsync(name, scopes, expiryDate, createdBy);
        await NotifyAsync(ApiKeyLifecycleReason.Created, result);
        return result;
    }

    public async Task<IApiKey> CreatePersonalSystemKeyAsync(string name, string[] scopes, string ownerUserKey, DateTime? expiryDate = null, string createdBy = null)
    {
        var result = await _inner.CreatePersonalSystemKeyAsync(name, scopes, ownerUserKey, expiryDate, createdBy);
        await NotifyAsync(ApiKeyLifecycleReason.Created, result);
        return result;
    }

    public async Task<IApiKey> RefreshSystemKeyAsync(string key)
    {
        var result = await _inner.RefreshSystemKeyAsync(key);
        await NotifyAsync(ApiKeyLifecycleReason.Recycled, result);
        return result;
    }

    // Delete — tokenless signal

    public async Task DeleteKeyAsync(string teamKey, string key)
    {
        await _inner.DeleteKeyAsync(teamKey, key);
        await NotifyDeletedAsync(key, teamKey, isSystemKey: false);
    }

    public async Task DeleteSystemKeyAsync(string key)
    {
        await _inner.DeleteSystemKeyAsync(key);
        await NotifyDeletedAsync(key, teamKey: null, isSystemKey: true);
    }

    private async Task NotifyAsync(ApiKeyLifecycleReason reason, IApiKey key)
    {
        if (_handlers.Count == 0 || key == null) return;

        var context = new ApiKeyLifecycleContext(
            reason,
            key.Key,
            key.ApiKey,
            key.TeamKey,
            key.TeamKey == null,
            key.Name,
            key.Tags ?? []);

        foreach (var handler in _handlers)
            await handler.OnApiKeyLifecycleAsync(context);
    }

    private async Task NotifyDeletedAsync(string apiKeyId, string teamKey, bool isSystemKey)
    {
        if (_handlers.Count == 0) return;

        var context = new ApiKeyLifecycleContext(
            ApiKeyLifecycleReason.Deleted,
            apiKeyId,
            PrivateToken: null,
            teamKey,
            isSystemKey,
            Name: null,
            Tags: []);

        foreach (var handler in _handlers)
            await handler.OnApiKeyLifecycleAsync(context);
    }
}
