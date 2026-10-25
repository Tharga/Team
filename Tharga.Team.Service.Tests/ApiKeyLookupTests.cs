using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Toolkit.Password;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// What authenticating a key costs (Tharga/Team#387), counted in PBKDF2 verifications against the real
/// <c>Tharga.Toolkit</c> hasher. Every key of a team shares the legacy 8-character prefix, so a lookup that
/// narrows by it alone verifies the whole team.
/// </summary>
public class ApiKeyLookupTests
{
    private const string TeamKey = "team-1";

    private readonly InMemoryApiKeyRepository _repository = new();
    private readonly CountingApiKeyService _hasher = new(CreateRealHasher());
    private IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    [Fact]
    public async Task NewKey_FirstRequest_VerifiesOneCandidate()
    {
        var sut = CreateSut();
        var keys = await CreateTeamKeysAsync(sut, 20);
        _hasher.Reset();

        var result = await sut.GetByApiKeyAsync(keys[7].ApiKey);

        Assert.Equal(keys[7].Key, result.Key);
        Assert.Equal(1, _hasher.Verifications);
    }

    [Fact]
    public async Task NewSystemKey_FirstRequest_VerifiesOneCandidate()
    {
        var sut = CreateSut();
        var keys = new List<IApiKey>();
        for (var i = 0; i < 10; i++)
            keys.Add(await sut.CreateSystemKeyAsync($"System {i}", ["sys:read"]));
        _hasher.Reset();

        var result = await sut.GetByApiKeyAsync(keys[3].ApiKey);

        Assert.Equal(keys[3].Key, result.Key);
        Assert.Equal(1, _hasher.Verifications);
    }

    [Fact]
    public async Task RepeatRequest_WhileCached_DoesNoVerification()
    {
        var sut = CreateSut();
        var keys = await CreateTeamKeysAsync(sut, 5);
        _hasher.Reset();

        await sut.GetByApiKeyAsync(keys[2].ApiKey);
        var repeat = await CreateSut().GetByApiKeyAsync(keys[2].ApiKey);

        Assert.Equal(keys[2].Key, repeat.Key);
        Assert.Equal(1, _hasher.Verifications);
    }

    [Fact]
    public async Task CacheTurnedOff_VerifiesEveryRequest()
    {
        var sut = CreateSut(TimeSpan.Zero);
        var keys = await CreateTeamKeysAsync(sut, 5);
        _hasher.Reset();

        await sut.GetByApiKeyAsync(keys[2].ApiKey);
        await sut.GetByApiKeyAsync(keys[2].ApiKey);

        Assert.Equal(2, _hasher.Verifications);
    }

    [Fact]
    public async Task CachedKey_Refreshed_OldKeyIsRefused_NewKeyIsAccepted()
    {
        var sut = CreateSut();
        var key = (await CreateTeamKeysAsync(sut, 1))[0];
        await sut.GetByApiKeyAsync(key.ApiKey);

        var refreshed = await sut.RefreshKeyAsync(TeamKey, key.Key);

        Assert.Null(await sut.GetByApiKeyAsync(key.ApiKey));
        Assert.Equal(key.Key, (await sut.GetByApiKeyAsync(refreshed.ApiKey))?.Key);
    }

    [Fact]
    public async Task CachedKey_Disabled_IsReturnedDisabled()
    {
        var sut = CreateSut();
        var key = (await CreateTeamKeysAsync(sut, 1))[0];
        await sut.GetByApiKeyAsync(key.ApiKey);

        await sut.SetKeyDisabledAsync(TeamKey, key.Key, true, "admin");

        Assert.NotNull((await sut.GetByApiKeyAsync(key.ApiKey)).DisabledAt);
    }

    [Fact]
    public async Task CachedKey_Deleted_IsRefused()
    {
        var sut = CreateSut();
        var key = (await CreateTeamKeysAsync(sut, 1))[0];
        await sut.GetByApiKeyAsync(key.ApiKey);

        await sut.DeleteKeyAsync(TeamKey, key.Key);

        Assert.Null(await sut.GetByApiKeyAsync(key.ApiKey));
    }

    [Fact]
    public async Task CachedKey_ScopeChange_IsReadOnTheNextRequest()
    {
        var sut = CreateSut();
        var key = (await CreateTeamKeysAsync(sut, 1))[0];
        await sut.GetByApiKeyAsync(key.ApiKey);

        await sut.SetScopeOverridesAsync(TeamKey, key.Key, ["doc:read"]);

        Assert.Equal(new[] { "doc:read" }, (await sut.GetByApiKeyAsync(key.ApiKey)).ScopeOverrides);
    }

    [Fact]
    public async Task LegacyKey_StopsAtTheFirstMatch()
    {
        var keys = SeedLegacyKeys(10);
        _hasher.Reset();

        var result = await CreateSut().GetByApiKeyAsync(keys[0].RawKey);

        Assert.Equal(keys[0].Entity.Key, result.Key);
        Assert.Equal(1, _hasher.Verifications);
    }

    [Fact]
    public async Task LegacyKey_MovesToItsLookupValue_OnFirstUse()
    {
        var keys = SeedLegacyKeys(10);
        await CreateSut().GetByApiKeyAsync(keys[5].RawKey);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _hasher.Reset();

        var result = await CreateSut().GetByApiKeyAsync(keys[5].RawKey);

        Assert.Equal(LookupValueOf(keys[5].RawKey), _repository.Find(keys[5].Entity.Key).ApiKeyPrefix);
        Assert.Equal(keys[5].Entity.Key, result.Key);
        Assert.Equal(1, _hasher.Verifications);
    }

    [Fact]
    public async Task KeyWithoutPrefix_IsFound_AndMovesToItsLookupValue()
    {
        var keys = SeedLegacyKeys(1, withPrefix: false);

        var result = await CreateSut().GetByApiKeyAsync(keys[0].RawKey);

        Assert.Equal(keys[0].Entity.Key, result.Key);
        Assert.Equal(LookupValueOf(keys[0].RawKey), _repository.Find(keys[0].Entity.Key).ApiKeyPrefix);
    }

    [Fact]
    public async Task UnknownKey_VerifiesNoKeyWithALookupValue_AndNeverReadsEveryKey()
    {
        var sut = CreateSut();
        await CreateTeamKeysAsync(sut, 10);
        _hasher.Reset();

        var result = await sut.GetByApiKeyAsync(_hasher.BuildApiKey(TeamKey));

        Assert.Null(result);
        Assert.Equal(0, _hasher.Verifications);
        Assert.Equal(0, _repository.FullReads);
    }

    [Fact]
    public async Task UnknownKey_VerifiesOnlyKeysStoredWithoutAPrefix()
    {
        var sut = CreateSut();
        await CreateTeamKeysAsync(sut, 10);
        SeedLegacyKeys(1, withPrefix: false);
        _hasher.Reset();

        await sut.GetByApiKeyAsync(_hasher.BuildApiKey(TeamKey));

        Assert.Equal(1, _hasher.Verifications);
    }

    [Fact]
    public async Task DuplicateRows_ReturnTheFirst_AndLogAWarning()
    {
        var logger = new RecordingLogger();
        var sut = CreateSut(logger: logger);
        var key = (await CreateTeamKeysAsync(sut, 1))[0];
        _repository.Seed(_repository.Find(key.Key));
        _hasher.Reset();

        var result = await sut.GetByApiKeyAsync(key.ApiKey);

        Assert.Equal(key.Key, result.Key);
        Assert.Equal(1, _hasher.Verifications);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task CreateKeyAsync_StoresTheLookupValue()
    {
        var sut = CreateSut();

        var key = (await CreateTeamKeysAsync(sut, 1))[0];

        Assert.Equal(LookupValueOf(key.ApiKey), _repository.Find(key.Key).ApiKeyPrefix);
    }

    [Fact]
    public async Task RefreshKeyAsync_StoresTheNewLookupValue()
    {
        var sut = CreateSut();
        var key = (await CreateTeamKeysAsync(sut, 1))[0];

        var refreshed = await sut.RefreshKeyAsync(TeamKey, key.Key);

        Assert.Equal(LookupValueOf(refreshed.ApiKey), _repository.Find(key.Key).ApiKeyPrefix);
    }

    private ApiKeyAdministrationService CreateSut(TimeSpan? cacheDuration = null, ILogger<ApiKeyAdministrationService> logger = null)
    {
        var options = new ApiKeyOptions { AdvancedMode = true, AutoLockKeys = false };
        if (cacheDuration.HasValue) options.VerificationCacheDuration = cacheDuration.Value;
        return new ApiKeyAdministrationService(_repository, _hasher, Options.Create(options), logger, _cache);
    }

    private static async Task<IApiKey[]> CreateTeamKeysAsync(ApiKeyAdministrationService sut, int count)
    {
        var keys = new IApiKey[count];
        for (var i = 0; i < count; i++)
            keys[i] = await sut.CreateKeyAsync(TeamKey, $"Key {i}", AccessLevel.User);
        return keys;
    }

    private (string RawKey, ApiKeyEntity Entity)[] SeedLegacyKeys(int count, bool withPrefix = true)
    {
        var keys = new (string RawKey, ApiKeyEntity Entity)[count];
        for (var i = 0; i < count; i++)
        {
            var raw = _hasher.BuildApiKey(TeamKey);
            var entity = new ApiKeyEntity
            {
                Id = global::MongoDB.Bson.ObjectId.GenerateNewId(),
                Key = Guid.NewGuid().ToString(),
                Name = $"Legacy {i}",
                TeamKey = TeamKey,
                ApiKeyHash = _hasher.Encrypt(raw),
                ApiKeyPrefix = withPrefix ? raw[..8] : null,
            };
            _repository.Seed(entity);
            keys[i] = (raw, entity);
        }
        return keys;
    }

    private static string LookupValueOf(string apiKey)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)), 0, 16);
    }

    private static IApiKeyService CreateRealHasher()
    {
        var services = new ServiceCollection();
        services.RegisterApiKeyService();
        return services.BuildServiceProvider().GetRequiredService<IApiKeyService>();
    }

    private sealed class CountingApiKeyService(IApiKeyService inner) : IApiKeyService
    {
        public int Verifications { get; private set; }

        public void Reset() => Verifications = 0;

        public string BuildApiKey(string username, Func<string> passwordGenerator = null) => inner.BuildApiKey(username, passwordGenerator);
        public string Encrypt(string apiKey) => inner.Encrypt(apiKey);
        public string GetUsername(string apiKey) => inner.GetUsername(apiKey);

        public bool Verify(string apiKey, string hashedApiKey)
        {
            Verifications++;
            return inner.Verify(apiKey, hashedApiKey);
        }
    }

    private sealed class InMemoryApiKeyRepository : IApiKeyRepository
    {
        private readonly List<ApiKeyEntity> _items = [];

        public int FullReads { get; private set; }

        public void Seed(ApiKeyEntity entity) => _items.Add(entity);
        public ApiKeyEntity Find(string key) => _items.First(x => x.Key == key);

        public IAsyncEnumerable<ApiKeyEntity> GetAsync()
        {
            FullReads++;
            return _items.ToArray().ToAsyncEnumerable();
        }

        public Task<ApiKeyEntity> GetAsync(string key) => Task.FromResult(_items.FirstOrDefault(x => x.Key == key));
        public IAsyncEnumerable<ApiKeyEntity> GetByPrefixAsync(string prefix) => _items.Where(x => x.ApiKeyPrefix == prefix).ToArray().ToAsyncEnumerable();

        public Task<ApiKeyEntity> AddAsync(ApiKeyEntity apiKeyEntity)
        {
            _items.Add(apiKeyEntity);
            return Task.FromResult(apiKeyEntity);
        }

        public Task UpdateAsync(string key, ApiKeyEntity apiKeyEntity) => Replace(key, _ => apiKeyEntity with { Key = key });
        public Task LockKeyAsync(string key) => Replace(key, x => x with { ApiKey = null });
        public Task SetDisabledAsync(string key, DateTime? disabledAtUtc, string disabledBy) => Replace(key, x => x with { DisabledAt = disabledAtUtc, DisabledBy = disabledBy });
        public Task SetLastUsedAsync(string key, DateTime lastUsedAtUtc) => Replace(key, x => x with { LastUsedAt = lastUsedAtUtc });
        public Task SetPrefixAsync(string key, string prefix) => Replace(key, x => x with { ApiKeyPrefix = prefix });

        public Task DeleteAsync(string key)
        {
            _items.RemoveAll(x => x.Key == key);
            return Task.CompletedTask;
        }

        public Task PurgeExpiredAsync() => Task.CompletedTask;
        public Task<long> CleanLegacyTagsAsync() => Task.FromResult(0L);

        private Task Replace(string key, Func<ApiKeyEntity, ApiKeyEntity> change)
        {
            var index = _items.FindIndex(x => x.Key == key);
            if (index >= 0) _items[index] = change(_items[index]);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger : ILogger<ApiKeyAdministrationService>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
