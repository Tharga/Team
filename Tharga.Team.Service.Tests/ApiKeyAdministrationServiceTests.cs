using Microsoft.Extensions.Options;
using Tharga.Toolkit.Password;

namespace Tharga.Team.Service.Tests;

public class ApiKeyAdministrationServiceTests
{
    private readonly IApiKeyRepository _repository = Substitute.For<IApiKeyRepository>();
    private readonly IApiKeyService _apiKeyService = Substitute.For<IApiKeyService>();
    private readonly ApiKeyAdministrationService _sut;

    public ApiKeyAdministrationServiceTests()
    {
        _sut = new ApiKeyAdministrationService(_repository, _apiKeyService);
    }

    [Fact]
    public async Task GetByApiKeyAsync_With_Matching_Hash_Returns_Key()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1");
        _repository.GetByPrefixAsync(null).Returns(ToAsyncEnumerable(entity));
        _apiKeyService.Verify("raw-key", "hash-1").Returns(true);

        var result = await _sut.GetByApiKeyAsync("raw-key");

        Assert.NotNull(result);
        Assert.Equal("team-1", result.TeamKey);
    }

    [Fact]
    public async Task GetByApiKeyAsync_With_TwoMatchingHashes_ReturnsFirst_DoesNotThrow()
    {
        // Hash-collision scenario: two stored keys both verify true against the raw key.
        // Previously: .SingleOrDefault threw InvalidOperationException ("Sequence contains more than one element").
        // After Tharga/Team#64 fix: resilient pick returns the first match and logs a warning.
        var entity1 = CreateEntity("key-1", "hash-1", "team-1");
        var entity2 = CreateEntity("key-2", "hash-2", "team-2");
        _repository.GetByPrefixAsync(null).Returns(ToAsyncEnumerable(entity1, entity2));
        _apiKeyService.Verify("raw-key", "hash-1").Returns(true);
        _apiKeyService.Verify("raw-key", "hash-2").Returns(true);

        var result = await _sut.GetByApiKeyAsync("raw-key");

        Assert.NotNull(result);
        Assert.Equal("team-1", result.TeamKey);
    }

    [Fact]
    public async Task GetByApiKeyAsync_With_No_Match_Returns_Null()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1");
        _repository.GetByPrefixAsync(null).Returns(ToAsyncEnumerable(entity));
        _apiKeyService.Verify("wrong-key", "hash-1").Returns(false);

        var result = await _sut.GetByApiKeyAsync("wrong-key");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByApiKeyAsync_StampsLastUsed_WhenNeverUsed()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1");
        _repository.GetByPrefixAsync(null).Returns(ToAsyncEnumerable(entity));
        _apiKeyService.Verify("raw-key", "hash-1").Returns(true);

        await _sut.GetByApiKeyAsync("raw-key");

        await _repository.Received(1).SetLastUsedAsync("key-1", Arg.Any<DateTime>());
    }

    [Fact]
    public async Task GetByApiKeyAsync_DoesNotStampLastUsed_WithinThrottleWindow()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1") with { LastUsedAt = DateTime.UtcNow };
        _repository.GetByPrefixAsync(null).Returns(ToAsyncEnumerable(entity));
        _apiKeyService.Verify("raw-key", "hash-1").Returns(true);

        await _sut.GetByApiKeyAsync("raw-key");

        await _repository.DidNotReceive().SetLastUsedAsync(Arg.Any<string>(), Arg.Any<DateTime>());
    }

    [Fact]
    public async Task GetByApiKeyAsync_StampsLastUsed_AfterThrottleWindow()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1") with { LastUsedAt = DateTime.UtcNow.AddMinutes(-5) };
        _repository.GetByPrefixAsync(null).Returns(ToAsyncEnumerable(entity));
        _apiKeyService.Verify("raw-key", "hash-1").Returns(true);

        await _sut.GetByApiKeyAsync("raw-key");

        await _repository.Received(1).SetLastUsedAsync("key-1", Arg.Any<DateTime>());
    }

    [Fact]
    public async Task GetByApiKeyAsync_LastUsedWriteFailure_DoesNotBreakAuthentication()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1");
        _repository.GetByPrefixAsync(null).Returns(ToAsyncEnumerable(entity));
        _apiKeyService.Verify("raw-key", "hash-1").Returns(true);
        _repository.SetLastUsedAsync(Arg.Any<string>(), Arg.Any<DateTime>()).Returns(Task.FromException(new Exception("db down")));

        var result = await _sut.GetByApiKeyAsync("raw-key");

        Assert.NotNull(result);
        Assert.Equal("team-1", result.TeamKey);
    }

    [Fact]
    public async Task RefreshKeyAsync_ResetsCreatedAtAndLastUsedAt()
    {
        var existing = CreateEntity("key-1", "old-hash", "team-1") with
        {
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            LastUsedAt = DateTime.UtcNow.AddDays(-1),
        };
        _repository.GetAsync("key-1").Returns(Task.FromResult(existing));
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("refreshed-key");
        _apiKeyService.Encrypt("refreshed-key").Returns("refreshed-hash");

        var before = DateTime.UtcNow;
        await _sut.RefreshKeyAsync("team-1", "key-1");

        await _repository.Received(1).UpdateAsync("key-1", Arg.Is<ApiKeyEntity>(e =>
            e.LastUsedAt == null && e.CreatedAt != null && e.CreatedAt >= before));
    }

    [Fact]
    public async Task GetKeysAsync_Returns_Existing_Keys()
    {
        var entity1 = CreateEntity("key-1", "hash-1", "team-1");
        var entity2 = CreateEntity("key-2", "hash-2", "team-1");
        _repository.GetAsync().Returns(ToAsyncEnumerable(entity1, entity2));

        var keys = await _sut.GetKeysAsync("team-1").ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, keys.Length);
    }

    [Fact]
    public async Task GetKeysAsync_Filters_By_TeamKey()
    {
        var entity1 = CreateEntity("key-1", "hash-1", "team-1");
        var entity2 = CreateEntity("key-2", "hash-2", "team-2");
        var entity3 = CreateEntity("key-3", "hash-3", "team-1");
        _repository.GetAsync().Returns(ToAsyncEnumerable(entity1, entity2, entity3));

        var keys = await _sut.GetKeysAsync("team-1").ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, keys.Length);
        Assert.All(keys, k => Assert.Equal("team-1", k.TeamKey));
    }

    [Fact]
    public async Task GetKeysAsync_AutoCreates_When_Fewer_Than_Two()
    {
        _repository.GetAsync().Returns(ToAsyncEnumerable<ApiKeyEntity>());
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("new-key");
        _apiKeyService.Encrypt("new-key").Returns("new-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));

        var keys = await _sut.GetKeysAsync("team-1").ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, keys.Length);
        await _repository.Received(2).AddAsync(Arg.Any<ApiKeyEntity>());
    }

    [Fact]
    public async Task RefreshKeyAsync_Updates_Repository()
    {
        var existing = CreateEntity("key-1", "old-hash", "team-1");
        _repository.GetAsync("key-1").Returns(Task.FromResult(existing));
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("refreshed-key");
        _apiKeyService.Encrypt("refreshed-key").Returns("refreshed-hash");

        await _sut.RefreshKeyAsync("team-1", "key-1");

        await _repository.Received(1).UpdateAsync("key-1", Arg.Is<ApiKeyEntity>(e => e.ApiKeyHash == "refreshed-hash"));
    }

    [Fact]
    public async Task LockKeyAsync_Verifies_Team_And_Delegates_To_Repository()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1");
        _repository.GetAsync("key-1").Returns(Task.FromResult(entity));

        await _sut.LockKeyAsync("team-1", "key-1");

        await _repository.Received(1).LockKeyAsync("key-1");
    }

    [Fact]
    public async Task LockKeyAsync_Wrong_Team_Throws()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1");
        _repository.GetAsync("key-1").Returns(Task.FromResult(entity));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.LockKeyAsync("team-2", "key-1"));
    }

    [Fact]
    public async Task DeleteKeyAsync_Wrong_Team_Throws()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1");
        _repository.GetAsync("key-1").Returns(Task.FromResult(entity));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.DeleteKeyAsync("team-2", "key-1"));
    }

    [Fact]
    public async Task SetScopeOverridesAsync_Updates_Entity()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1");
        _repository.GetAsync("key-1").Returns(Task.FromResult(entity));

        await _sut.SetScopeOverridesAsync("team-1", "key-1", new[] { "valuegroup:read", "valuegroup:write" });

        await _repository.Received(1).UpdateAsync("key-1", Arg.Is<ApiKeyEntity>(e =>
            e.ScopeOverrides != null
            && e.ScopeOverrides.Length == 2
            && e.ScopeOverrides[0] == "valuegroup:read"
            && e.ScopeOverrides[1] == "valuegroup:write"));
    }

    [Fact]
    public async Task SetScopeOverridesAsync_EmptyArray_ClearsOverridesToNull()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1") with { ScopeOverrides = new[] { "stale:scope" } };
        _repository.GetAsync("key-1").Returns(Task.FromResult(entity));

        await _sut.SetScopeOverridesAsync("team-1", "key-1", Array.Empty<string>());

        await _repository.Received(1).UpdateAsync("key-1", Arg.Is<ApiKeyEntity>(e => e.ScopeOverrides == null));
    }

    [Fact]
    public async Task SetScopeOverridesAsync_WrongTeam_Throws()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1");
        _repository.GetAsync("key-1").Returns(Task.FromResult(entity));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.SetScopeOverridesAsync("team-2", "key-1", new[] { "x" }));
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<string>(), Arg.Any<ApiKeyEntity>());
    }

    [Fact]
    public async Task SetRolesAsync_Updates_Entity()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1");
        _repository.GetAsync("key-1").Returns(Task.FromResult(entity));

        await _sut.SetRolesAsync("team-1", "key-1", new[] { "Editor", "Support" });

        await _repository.Received(1).UpdateAsync("key-1", Arg.Is<ApiKeyEntity>(e =>
            e.Roles != null && e.Roles.Length == 2 && e.Roles[0] == "Editor" && e.Roles[1] == "Support"));
    }

    [Fact]
    public async Task SetRolesAsync_EmptyArray_ClearsRolesToNull()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1") with { Roles = new[] { "Stale" } };
        _repository.GetAsync("key-1").Returns(Task.FromResult(entity));

        await _sut.SetRolesAsync("team-1", "key-1", Array.Empty<string>());

        await _repository.Received(1).UpdateAsync("key-1", Arg.Is<ApiKeyEntity>(e => e.Roles == null));
    }

    [Fact]
    public async Task SetRolesAsync_WrongTeam_Throws()
    {
        var entity = CreateEntity("key-1", "hash-1", "team-1");
        _repository.GetAsync("key-1").Returns(Task.FromResult(entity));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.SetRolesAsync("team-2", "key-1", new[] { "Editor" }));
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<string>(), Arg.Any<ApiKeyEntity>());
    }

    [Fact]
    public async Task CreateKeyAsync_With_ScopeOverrides_Sets_Field_On_Entity()
    {
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("new-key");
        _apiKeyService.Encrypt("new-key").Returns("new-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));

        var scopes = new[] { "doc:read", "doc:write" };
        await _sut.CreateKeyAsync("team-1", "My Key", AccessLevel.User, roles: null, scopeOverrides: scopes);

        await _repository.Received(1).AddAsync(Arg.Is<ApiKeyEntity>(e =>
            e.ScopeOverrides != null
            && e.ScopeOverrides.Length == 2
            && e.ScopeOverrides[0] == "doc:read"
            && e.ScopeOverrides[1] == "doc:write"));
    }

    [Fact]
    public async Task CreateKeyAsync_Without_ScopeOverrides_LeavesFieldNull()
    {
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("new-key");
        _apiKeyService.Encrypt("new-key").Returns("new-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));

        await _sut.CreateKeyAsync("team-1", "My Key", AccessLevel.User);

        await _repository.Received(1).AddAsync(Arg.Is<ApiKeyEntity>(e => e.ScopeOverrides == null));
    }

    [Fact]
    public async Task CreateKeyAsync_With_Tags_Persists_Them()
    {
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("new-key");
        _apiKeyService.Encrypt("new-key").Returns("new-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));

        var tags = new[] { new Tag("Type", "firewall"), new Tag("firewall.groupId", "ABC123") };
        await _sut.CreateKeyAsync("team-1", "My Key", AccessLevel.Custom, tags: tags);

        await _repository.Received(1).AddAsync(Arg.Is<ApiKeyEntity>(e =>
            e.Tags != null
            && e.Tags.Count == 2
            && e.Tags[0].Key == "Type" && e.Tags[0].Value == "firewall"
            && e.Tags[1].Key == "firewall.groupId" && e.Tags[1].Value == "ABC123"));
    }

    [Fact]
    public async Task RefreshKeyAsync_Preserves_Tags()
    {
        var existing = CreateEntity("key-1", "old-hash", "team-1") with
        {
            Tags = new[] { new Tag("Type", "firewall") },
        };
        _repository.GetAsync("key-1").Returns(Task.FromResult(existing));
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("refreshed-key");
        _apiKeyService.Encrypt("refreshed-key").Returns("refreshed-hash");

        await _sut.RefreshKeyAsync("team-1", "key-1");

        await _repository.Received(1).UpdateAsync("key-1", Arg.Is<ApiKeyEntity>(e =>
            e.Tags != null && e.Tags.Count == 1 && e.Tags[0].Key == "Type" && e.Tags[0].Value == "firewall"));
    }

    [Fact]
    public async Task CreateKeyAsync_Persists_CreatedBy()
    {
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("new-key");
        _apiKeyService.Encrypt("new-key").Returns("new-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));

        await _sut.CreateKeyAsync("team-1", "My Key", AccessLevel.User, createdBy: "alice@example.com");

        await _repository.Received(1).AddAsync(Arg.Is<ApiKeyEntity>(e => e.CreatedBy == "alice@example.com"));
    }

    [Fact]
    public async Task CreateKeyAsync_Without_CreatedBy_LeavesFieldNull()
    {
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("new-key");
        _apiKeyService.Encrypt("new-key").Returns("new-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));

        await _sut.CreateKeyAsync("team-1", "My Key", AccessLevel.User);

        await _repository.Received(1).AddAsync(Arg.Is<ApiKeyEntity>(e => e.CreatedBy == null));
    }

    [Fact]
    public async Task RefreshKeyAsync_Preserves_CreatedBy()
    {
        var existing = CreateEntity("key-1", "old-hash", "team-1") with { CreatedBy = "alice@example.com" };
        _repository.GetAsync("key-1").Returns(Task.FromResult(existing));
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("refreshed-key");
        _apiKeyService.Encrypt("refreshed-key").Returns("refreshed-hash");

        await _sut.RefreshKeyAsync("team-1", "key-1");

        await _repository.Received(1).UpdateAsync("key-1", Arg.Is<ApiKeyEntity>(e => e.CreatedBy == "alice@example.com"));
    }

    [Fact]
    public async Task CreateKeyAsync_Persists_OwnerMemberKey()
    {
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("new-key");
        _apiKeyService.Encrypt("new-key").Returns("new-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));

        await _sut.CreateKeyAsync("team-1", "My Key", AccessLevel.User, ownerMemberKey: "member-1");

        await _repository.Received(1).AddAsync(Arg.Is<ApiKeyEntity>(e => e.OwnerMemberKey == "member-1"));
    }

    [Fact]
    public async Task RefreshKeyAsync_Preserves_OwnerMemberKey()
    {
        var existing = CreateEntity("key-1", "old-hash", "team-1") with { OwnerMemberKey = "member-1" };
        _repository.GetAsync("key-1").Returns(Task.FromResult(existing));
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("refreshed-key");
        _apiKeyService.Encrypt("refreshed-key").Returns("refreshed-hash");

        await _sut.RefreshKeyAsync("team-1", "key-1");

        await _repository.Received(1).UpdateAsync("key-1", Arg.Is<ApiKeyEntity>(e => e.OwnerMemberKey == "member-1"));
    }

    [Fact]
    public async Task CreateKeyAsync_Uses_Default_Fixed_Length_Of_32()
    {
        Func<string> generator = null;
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Do<Func<string>>(f => generator = f)).Returns("new-key");
        _apiKeyService.Encrypt("new-key").Returns("new-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));

        await _sut.CreateKeyAsync("team-1", "My Key", AccessLevel.User);

        // MaxKeyLength is null by default → fixed length at MinKeyLength (32).
        Assert.NotNull(generator);
        Assert.Equal(32, generator().Length);
    }

    [Fact]
    public async Task CreateKeyAsync_Null_Max_Uses_Min_As_Fixed_Length()
    {
        var sut = new ApiKeyAdministrationService(_repository, _apiKeyService, Options.Create(new ApiKeyOptions { MinKeyLength = 48 }));
        Func<string> generator = null;
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Do<Func<string>>(f => generator = f)).Returns("new-key");
        _apiKeyService.Encrypt("new-key").Returns("new-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));

        await sut.CreateKeyAsync("team-1", "My Key", AccessLevel.User);

        Assert.NotNull(generator);
        Assert.Equal(48, generator().Length);
    }

    [Fact]
    public async Task CreateKeyAsync_With_Max_Uses_Random_Range()
    {
        var sut = new ApiKeyAdministrationService(_repository, _apiKeyService, Options.Create(new ApiKeyOptions { MinKeyLength = 40, MaxKeyLength = 64 }));
        Func<string> generator = null;
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Do<Func<string>>(f => generator = f)).Returns("new-key");
        _apiKeyService.Encrypt("new-key").Returns("new-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));

        await sut.CreateKeyAsync("team-1", "My Key", AccessLevel.User);

        Assert.NotNull(generator);
        Assert.InRange(generator().Length, 40, 64);
    }

    [Fact]
    public async Task CreateSystemKeyAsync_Uses_Configured_Length()
    {
        var sut = new ApiKeyAdministrationService(_repository, _apiKeyService, Options.Create(new ApiKeyOptions { MinKeyLength = 40 }));
        Func<string> generator = null;
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Do<Func<string>>(f => generator = f)).Returns("new-key");
        _apiKeyService.Encrypt("new-key").Returns("new-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));

        await sut.CreateSystemKeyAsync("Sys Key", new[] { "sys:read" });

        Assert.NotNull(generator);
        Assert.Equal(40, generator().Length);
    }

    [Fact]
    public async Task CreateKeyAsync_Throws_When_Max_Below_Min()
    {
        var sut = new ApiKeyAdministrationService(_repository, _apiKeyService, Options.Create(new ApiKeyOptions { MinKeyLength = 40, MaxKeyLength = 32 }));
        Func<string> generator = null;
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Do<Func<string>>(f => generator = f)).Returns("new-key");
        _apiKeyService.Encrypt("new-key").Returns("new-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));

        await sut.CreateKeyAsync("team-1", "My Key", AccessLevel.User);

        // Max 32 < Min 40 — the generator throws when invoked.
        Assert.NotNull(generator);
        Assert.Throws<InvalidOperationException>(() => generator());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    public void MinKeyLength_Below_Floor_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApiKeyOptions { MinKeyLength = value });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    public void MaxKeyLength_Below_Floor_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApiKeyOptions { MaxKeyLength = value });
    }

    [Fact]
    public void KeyLength_Defaults_And_Floor()
    {
        var options = new ApiKeyOptions();
        Assert.Equal(32, options.MinKeyLength);
        Assert.Null(options.MaxKeyLength);
        Assert.Equal(24, ApiKeyOptions.KeyLengthFloor);
    }

    [Fact]
    public async Task CreateKeyAsync_With_Custom_AccessLevel_Persists_Custom_And_Overrides()
    {
        // The headline #74 use case: a least-privilege machine key minted with Custom + a single
        // explicit override, carrying no inherited base scopes.
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("new-key");
        _apiKeyService.Encrypt("new-key").Returns("new-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));

        var created = await _sut.CreateKeyAsync("team-1", "Value Group reader", AccessLevel.Custom,
            roles: null, scopeOverrides: new[] { "valuegroup:read" });

        Assert.Equal(AccessLevel.Custom, created.AccessLevel);
        await _repository.Received(1).AddAsync(Arg.Is<ApiKeyEntity>(e =>
            e.AccessLevel == AccessLevel.Custom
            && e.ScopeOverrides != null
            && e.ScopeOverrides.Length == 1
            && e.ScopeOverrides[0] == "valuegroup:read"));
    }

    private static ApiKeyEntity CreateEntity(string key, string hash, string teamKey)
    {
        return new ApiKeyEntity
        {
            Id = global::MongoDB.Bson.ObjectId.GenerateNewId(),
            Key = key,
            Name = "Test",
            ApiKeyHash = hash,
            TeamKey = teamKey,
        };
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }
}
