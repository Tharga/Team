using Microsoft.AspNetCore.Http;
using Tharga.Team.Service.Audit;
using Tharga.Toolkit.Password;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Storing a personal system key (Tharga/Team#314): the owner is kept on create and on refresh, and both
/// decorators pass the new operation through.
/// </summary>
public class PersonalSystemKeyStoreTests
{
    private readonly IApiKeyRepository _repository = Substitute.For<IApiKeyRepository>();
    private readonly IApiKeyService _apiKeyService = Substitute.For<IApiKeyService>();
    private readonly ApiKeyAdministrationService _sut;

    public PersonalSystemKeyStoreTests()
    {
        _sut = new ApiKeyAdministrationService(_repository, _apiKeyService);
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("sys-key");
        _apiKeyService.Encrypt("sys-key").Returns("sys-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));
    }

    [Fact]
    public async Task CreatePersonal_StoresTheOwner()
    {
        var result = await _sut.CreatePersonalSystemKeyAsync("mine", ["sys:read"], "u1", createdBy: "alice");

        Assert.Null(result.TeamKey);
        Assert.Equal("u1", result.OwnerMemberKey);
        Assert.Equal(["sys:read"], result.SystemScopes);
        Assert.Equal("alice", result.CreatedBy);
        await _repository.Received(1).AddAsync(Arg.Is<ApiKeyEntity>(e => e.TeamKey == null && e.OwnerMemberKey == "u1"));
    }

    [Fact]
    public async Task CreateShared_HasNoOwner()
    {
        var result = await _sut.CreateSystemKeyAsync("shared", ["sys:read"]);

        Assert.Null(result.OwnerMemberKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task CreatePersonal_WithoutAnOwner_IsRefused(string owner)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreatePersonalSystemKeyAsync("mine", [], owner));

        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default);
    }

    /// <summary>
    /// The refresh rebuilds the entity from a fixed field list. Dropping the owner there would turn a
    /// personal key into a shared one, visible and usable by everyone who manages system keys.
    /// </summary>
    [Fact]
    public async Task Refresh_KeepsTheOwner()
    {
        var existing = new ApiKeyEntity
        {
            Id = global::MongoDB.Bson.ObjectId.GenerateNewId(),
            Key = "k1",
            Name = "mine",
            ApiKeyHash = "old",
            TeamKey = null,
            SystemScopes = ["sys:read"],
            OwnerMemberKey = "u1",
        };
        _repository.GetAsync("k1").Returns(Task.FromResult(existing));

        var refreshed = await _sut.RefreshSystemKeyAsync("k1");

        Assert.Equal("u1", refreshed.OwnerMemberKey);
        await _repository.Received(1).UpdateAsync("k1", Arg.Is<ApiKeyEntity>(e => e.OwnerMemberKey == "u1"));
    }

    [Fact]
    public async Task LifecycleDecorator_SignalsCreatedAsSystem()
    {
        var inner = Substitute.For<IApiKeyAdministrationService>();
        var key = Substitute.For<IApiKey>();
        key.Key.Returns("k1");
        key.ApiKey.Returns("raw-token");
        key.TeamKey.Returns((string)null);
        inner.CreatePersonalSystemKeyAsync("mine", Arg.Any<string[]>(), "u1", Arg.Any<DateTime?>(), Arg.Any<string>()).Returns(key);
        var handler = Substitute.For<IApiKeyLifecycleHandler>();

        var result = await new ApiKeyLifecycleDecorator(inner, [handler]).CreatePersonalSystemKeyAsync("mine", [], "u1");

        Assert.Same(key, result);
        await handler.Received(1).OnApiKeyLifecycleAsync(Arg.Is<ApiKeyLifecycleContext>(c =>
            c.Reason == ApiKeyLifecycleReason.Created && c.IsSystemKey && c.PrivateToken == "raw-token"));
    }

    [Fact]
    public async Task AuditDecorator_RecordsTheCreate()
    {
        var inner = Substitute.For<IApiKeyAdministrationService>();
        inner.CreatePersonalSystemKeyAsync(default, default, default).ReturnsForAnyArgs(Substitute.For<IApiKey>());
        var (logger, backend) = FakeAuditLoggerFactory.Create();

        await new AuditingApiKeyServiceDecorator(inner, logger, Substitute.For<IHttpContextAccessor>())
            .CreatePersonalSystemKeyAsync("mine", [], "u1");

        var entry = Assert.Single(backend.Entries);
        Assert.Equal("create", entry.Action);
        Assert.Equal(nameof(IApiKeyAdministrationService.CreatePersonalSystemKeyAsync), entry.MethodName);
        Assert.True(entry.Success);
        await inner.Received(1).CreatePersonalSystemKeyAsync("mine", Arg.Any<string[]>(), "u1", Arg.Any<DateTime?>(), Arg.Any<string>());
    }

    /// <summary>
    /// A host's own key store, written before 4.1, keeps compiling and refuses the one thing it can't do,
    /// rather than quietly creating a shared key.
    /// </summary>
    [Fact]
    public async Task AStoreWithoutPersonalSupport_RefusesAPersonalKey()
    {
        IApiKeyAdministrationService store = new PreFourOneStore();

        await Assert.ThrowsAsync<NotSupportedException>(() => store.CreatePersonalSystemKeyAsync("mine", [], "u1"));
    }

    private sealed class PreFourOneStore : IApiKeyAdministrationService
    {
        public Task<IApiKey> GetByApiKeyAsync(string apiKey) => throw new NotImplementedException();
        public IAsyncEnumerable<IApiKey> GetKeysAsync(string teamKey) => throw new NotImplementedException();
        public Task<IApiKey> CreateKeyAsync(string teamKey, string name, AccessLevel accessLevel, string[] roles = null, string[] scopeOverrides = null, DateTime? expiryDate = null, IReadOnlyList<Tag> tags = null, string createdBy = null, string ownerMemberKey = null) => throw new NotImplementedException();
        public Task<IApiKey> RefreshKeyAsync(string teamKey, string key) => throw new NotImplementedException();
        public Task LockKeyAsync(string teamKey, string key) => throw new NotImplementedException();
        public Task SetKeyDisabledAsync(string teamKey, string key, bool disabled, string actor = null) => throw new NotImplementedException();
        public Task DeleteKeyAsync(string teamKey, string key) => throw new NotImplementedException();
        public Task SetScopeOverridesAsync(string teamKey, string key, string[] scopes) => throw new NotImplementedException();
        public Task SetRolesAsync(string teamKey, string key, string[] roles) => throw new NotImplementedException();
        public IAsyncEnumerable<IApiKey> GetSystemKeysAsync() => throw new NotImplementedException();
        public Task<IApiKey> CreateSystemKeyAsync(string name, string[] scopes, DateTime? expiryDate = null, string createdBy = null) => throw new NotImplementedException();
        public Task<IApiKey> RefreshSystemKeyAsync(string key) => throw new NotImplementedException();
        public Task LockSystemKeyAsync(string key) => throw new NotImplementedException();
        public Task SetSystemKeyDisabledAsync(string key, bool disabled, string actor = null) => throw new NotImplementedException();
        public Task DeleteSystemKeyAsync(string key) => throw new NotImplementedException();
    }
}
