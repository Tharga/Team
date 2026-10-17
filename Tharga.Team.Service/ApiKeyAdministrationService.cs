using MongoDB.Bson;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Toolkit;
using Tharga.Toolkit.Password;

namespace Tharga.Team.Service;

/// <summary>
/// Default implementation of <see cref="IApiKeyAdministrationService"/> using MongoDB storage.
/// </summary>
public class ApiKeyAdministrationService : IApiKeyAdministrationService
{
    private readonly IApiKeyRepository _repository;
    private readonly IApiKeyService _apiKeyService;
    private readonly ILogger<ApiKeyAdministrationService> _logger;
    private readonly Tharga.Team.ApiKeyOptions _options;

    /// <summary>
    /// Creates a new instance using the specified repository and key hashing service.
    /// </summary>
    public ApiKeyAdministrationService(IApiKeyRepository repository, IApiKeyService apiKeyService, IOptions<Tharga.Team.ApiKeyOptions> options = null, ILogger<ApiKeyAdministrationService> logger = null)
    {
        _repository = repository;
        _apiKeyService = apiKeyService;
        _logger = logger;
        _options = options?.Value ?? new Tharga.Team.ApiKeyOptions();
    }

    /// <inheritdoc />
    public async Task<IApiKey> GetByApiKeyAsync(string apiKey)
    {
        var prefix = GetPrefix(apiKey);
        ApiKeyEntity item = null;

        if (prefix != null)
        {
            var candidates = await _repository.GetByPrefixAsync(prefix).ToArrayAsync();
            item = candidates.PickOneOrDefault(x => _apiKeyService.Verify(apiKey, x.ApiKeyHash), _logger, $"ApiKey verify prefix={prefix}");
        }

        if (item == null)
        {
            var allItems = await _repository.GetAsync().ToArrayAsync();
            item = allItems.PickOneOrDefault(x => _apiKeyService.Verify(apiKey, x.ApiKeyHash), _logger, "ApiKey verify full-scan");
        }

        if (item?.ExpiryDate != null && item.ExpiryDate < DateTime.UtcNow)
            return null;

        if (item != null)
            await TouchLastUsedAsync(item);

        return item;
    }

    /// <summary>
    /// Stamps the key's "last used" timestamp, throttled by <see cref="ApiKeyOptions.LastUsedThrottle"/>
    /// so a busy key isn't written on every request. Best-effort: a failure here must never break
    /// authentication, so it is swallowed (and logged) rather than propagated.
    /// </summary>
    private async Task TouchLastUsedAsync(ApiKeyEntity item)
    {
        var now = DateTime.UtcNow;
        var throttle = _options.LastUsedThrottle;
        if (throttle > TimeSpan.Zero && item.LastUsedAt.HasValue && now - item.LastUsedAt.Value < throttle)
            return;

        try
        {
            await _repository.SetLastUsedAsync(item.Key, now);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to update LastUsedAt for API key {Key}.", item.Key);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IApiKey> GetKeysAsync(string teamKey)
    {
        await _repository.PurgeExpiredAsync();

        var count = 0;
        await foreach (var item in _repository.GetAsync())
        {
            if (item.TeamKey != teamKey) continue;
            count++;
            yield return item;
        }

        if (_options.AdvancedMode) yield break;

        for (var i = count; i < _options.AutoKeyCount; i++)
        {
            var name = IntegerExtensions.GetNameForNumber(i + 1);
            var expiryDate = GetDefaultExpiryDate();
            var entity = BuildKey(teamKey, name, [], AccessLevel.User, null, null, expiryDate, createdBy: null);
            var created = await _repository.AddAsync(entity);

            if (_options.AutoLockKeys)
            {
                await _repository.LockKeyAsync(created.Key);
                created = created with { ApiKey = null };
            }

            yield return created;
        }
    }

    /// <inheritdoc />
    public async Task<IApiKey> CreateKeyAsync(string teamKey, string name, AccessLevel accessLevel, string[] roles = null, string[] scopeOverrides = null, DateTime? expiryDate = null, IReadOnlyList<Tag> tags = null, string createdBy = null, string ownerMemberKey = null)
    {
        expiryDate ??= GetDefaultExpiryDate();

        if (_options.MaxExpiryDays.HasValue && expiryDate.HasValue)
        {
            var maxDate = DateTime.UtcNow.AddDays(_options.MaxExpiryDays.Value);
            if (expiryDate > maxDate)
                throw new InvalidOperationException($"Expiry date cannot exceed {_options.MaxExpiryDays} days from now.");
        }

        var entity = BuildKey(teamKey, name, tags, accessLevel, roles, scopeOverrides, expiryDate, createdBy, ownerMemberKey);
        var created = await _repository.AddAsync(entity);

        if (_options.AutoLockKeys)
            await _repository.LockKeyAsync(created.Key);

        return created;
    }

    /// <inheritdoc />
    public async Task<IApiKey> RefreshKeyAsync(string teamKey, string key)
    {
        var item = await _repository.GetAsync(key);
        VerifyTeamOwnership(item, teamKey);
        var refreshed = BuildKey(teamKey, item.Name, item.Tags, item.AccessLevel ?? AccessLevel.Administrator, item.Roles, item.ScopeOverrides, item.ExpiryDate, item.CreatedBy, item.OwnerMemberKey, item.DisabledAt, item.DisabledBy);
        await _repository.UpdateAsync(key, refreshed);

        if (_options.AutoLockKeys)
            await _repository.LockKeyAsync(key);

        return refreshed;
    }

    /// <inheritdoc />
    public async Task LockKeyAsync(string teamKey, string key)
    {
        var item = await _repository.GetAsync(key);
        VerifyTeamOwnership(item, teamKey);
        await _repository.LockKeyAsync(key);
    }

    /// <inheritdoc />
    public async Task SetKeyDisabledAsync(string teamKey, string key, bool disabled, string actor = null)
    {
        var item = await _repository.GetAsync(key);
        VerifyTeamOwnership(item, teamKey);
        await _repository.SetDisabledAsync(key, disabled ? DateTime.UtcNow : null, disabled ? actor : null);
    }

    /// <inheritdoc />
    public async Task DeleteKeyAsync(string teamKey, string key)
    {
        var item = await _repository.GetAsync(key);
        VerifyTeamOwnership(item, teamKey);
        await _repository.DeleteAsync(key);
    }

    /// <inheritdoc />
    public async Task SetScopeOverridesAsync(string teamKey, string key, string[] scopes)
    {
        var item = await _repository.GetAsync(key);
        VerifyTeamOwnership(item, teamKey);
        var normalised = scopes is { Length: > 0 } ? scopes : null;
        var updated = item with { ScopeOverrides = normalised };
        await _repository.UpdateAsync(key, updated);
    }

    public async Task SetRolesAsync(string teamKey, string key, string[] roles)
    {
        var item = await _repository.GetAsync(key);
        VerifyTeamOwnership(item, teamKey);
        var normalised = roles is { Length: > 0 } ? roles : null;
        var updated = item with { Roles = normalised };
        await _repository.UpdateAsync(key, updated);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IApiKey> GetSystemKeysAsync()
    {
        await _repository.PurgeExpiredAsync();

        await foreach (var item in _repository.GetAsync())
        {
            if (item.TeamKey != null) continue;
            yield return item;
        }
    }

    /// <inheritdoc />
    public async Task<IApiKey> CreateSystemKeyAsync(string name, string[] scopes, DateTime? expiryDate = null, string createdBy = null)
    {
        expiryDate ??= GetDefaultExpiryDate();

        if (_options.MaxExpiryDays.HasValue && expiryDate.HasValue)
        {
            var maxDate = DateTime.UtcNow.AddDays(_options.MaxExpiryDays.Value);
            if (expiryDate > maxDate)
                throw new InvalidOperationException($"Expiry date cannot exceed {_options.MaxExpiryDays} days from now.");
        }

        var entity = BuildSystemKey(name, scopes ?? Array.Empty<string>(), expiryDate, createdBy);
        var created = await _repository.AddAsync(entity);

        if (_options.AutoLockKeys)
            await _repository.LockKeyAsync(created.Key);

        return created;
    }

    /// <inheritdoc />
    public async Task<IApiKey> RefreshSystemKeyAsync(string key)
    {
        var item = await _repository.GetAsync(key);
        VerifySystemKey(item);
        var refreshed = BuildSystemKey(item.Name, item.SystemScopes ?? Array.Empty<string>(), item.ExpiryDate, item.CreatedBy, item.DisabledAt, item.DisabledBy);
        await _repository.UpdateAsync(key, refreshed);

        if (_options.AutoLockKeys)
            await _repository.LockKeyAsync(key);

        return refreshed;
    }

    /// <inheritdoc />
    public async Task LockSystemKeyAsync(string key)
    {
        var item = await _repository.GetAsync(key);
        VerifySystemKey(item);
        await _repository.LockKeyAsync(key);
    }

    /// <inheritdoc />
    public async Task SetSystemKeyDisabledAsync(string key, bool disabled, string actor = null)
    {
        var item = await _repository.GetAsync(key);
        VerifySystemKey(item);
        await _repository.SetDisabledAsync(key, disabled ? DateTime.UtcNow : null, disabled ? actor : null);
    }

    /// <inheritdoc />
    public async Task DeleteSystemKeyAsync(string key)
    {
        var item = await _repository.GetAsync(key);
        VerifySystemKey(item);
        await _repository.DeleteAsync(key);
    }

    private static void VerifyTeamOwnership(ApiKeyEntity item, string teamKey)
    {
        if (item.TeamKey == null)
            throw new UnauthorizedAccessException("This is a system key; use the system-key methods.");
        if (item.TeamKey != teamKey)
            throw new UnauthorizedAccessException($"API key does not belong to team '{teamKey}'.");
    }

    private static void VerifySystemKey(ApiKeyEntity item)
    {
        if (item.TeamKey != null)
            throw new UnauthorizedAccessException("This is a team key; use the team-scoped methods.");
    }

    private DateTime? GetDefaultExpiryDate()
    {
        return _options.MaxExpiryDays.HasValue
            ? DateTime.UtcNow.AddDays(_options.MaxExpiryDays.Value)
            : null;
    }

    private string GenerateSecret()
    {
        var min = _options.MinKeyLength;
        var max = _options.MaxKeyLength ?? min; // null = fixed length at MinKeyLength
        if (max < min)
            throw new InvalidOperationException($"ApiKeyOptions.MaxKeyLength ({max}) must be greater than or equal to MinKeyLength ({min}).");
        return StringExtension.GetRandomString(min, max);
    }

    // disabledAt/disabledBy are carried forward on a refresh. A refresh mints a new secret; it is not a
    // decision to trust the key again, so a disabled key stays disabled until someone explicitly enables
    // it. Dropping them here is how the remedy for a compromise would silently undo the containment —
    // and this builder rebuilds from a fixed field list, so any state not named is lost without a word.
    private ApiKeyEntity BuildKey(string teamKey, string name, IReadOnlyList<Tag> tags, AccessLevel accessLevel, string[] roles, string[] scopeOverrides, DateTime? expiryDate, string createdBy, string ownerMemberKey = null, DateTime? disabledAt = null, string disabledBy = null)
    {
        var apiKey = _apiKeyService.BuildApiKey(teamKey, GenerateSecret);
        var encryptedApiKey = _apiKeyService.Encrypt(apiKey);
        return new ApiKeyEntity
        {
            Id = ObjectId.GenerateNewId(),
            Key = Guid.NewGuid().ToString(),
            Name = name,
            ApiKey = apiKey,
            ApiKeyPrefix = GetPrefix(apiKey),
            TeamKey = teamKey,
            Tags = tags,
            ApiKeyHash = encryptedApiKey,
            AccessLevel = accessLevel,
            Roles = roles,
            ScopeOverrides = scopeOverrides,
            ExpiryDate = expiryDate,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
            OwnerMemberKey = ownerMemberKey,
            DisabledAt = disabledAt,
            DisabledBy = disabledBy,
        };
    }

    private ApiKeyEntity BuildSystemKey(string name, string[] scopes, DateTime? expiryDate, string createdBy, DateTime? disabledAt = null, string disabledBy = null)
    {
        var apiKey = _apiKeyService.BuildApiKey("system", GenerateSecret);
        var encryptedApiKey = _apiKeyService.Encrypt(apiKey);
        return new ApiKeyEntity
        {
            Id = ObjectId.GenerateNewId(),
            Key = Guid.NewGuid().ToString(),
            Name = name,
            ApiKey = apiKey,
            ApiKeyPrefix = GetPrefix(apiKey),
            TeamKey = null,
            ApiKeyHash = encryptedApiKey,
            SystemScopes = scopes,
            ExpiryDate = expiryDate,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
            DisabledAt = disabledAt,
            DisabledBy = disabledBy,
        };
    }

    private static string GetPrefix(string apiKey)
    {
        return apiKey?.Length >= 8 ? apiKey[..8] : null;
    }
}
