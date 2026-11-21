using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// What a caller may hand out as a system key's scopes, when the key is created and when its scopes are changed afterwards
/// (Tharga/Team#522): registered scopes only, and nothing beyond what they hold themselves.
/// </summary>
/// <remarks>
/// <b>The refusals are the tests that earn their place.</b> Holding <c>apikeys:system:manage</c> says a caller may administer system keys, and
/// every negative here is asserted against a caller who holds it and still may not hand out what they do not hold.
/// </remarks>
public class SystemApiKeyScopeCeilingTests
{
    private const string Read = "support:all:read";
    private const string Manage = "support:all:manage";
    private const string Discover = "mcp:discover";

    private readonly IApiKeyAdministrationService _inner = Substitute.For<IApiKeyAdministrationService>();

    private static IApiKey Key(string id, string owner = null, params string[] scopes)
    {
        var k = Substitute.For<IApiKey>();
        k.Key.Returns(id);
        k.TeamKey.Returns((string)null);
        k.OwnerMemberKey.Returns(owner);
        k.SystemScopes.Returns(scopes);
        return k;
    }

    private static async IAsyncEnumerable<IApiKey> Async(params IApiKey[] items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    private static SystemScopeRegistry Registry()
    {
        var registry = new SystemScopeRegistry();
        registry.Register(Read);
        registry.Register(Manage);
        registry.Register(Discover);
        return registry;
    }

    /// <param name="held">The system scopes the caller holds.</param>
    private SystemApiKeyManagementService Build(string[] held = null, bool overseer = false, string memberKey = null, bool withRegistry = true, params IApiKey[] keys)
    {
        _inner.GetSystemKeysAsync().Returns(_ => Async(keys));

        var claims = new List<Claim> { new(ClaimTypes.Name, "alice") };
        if (memberKey != null) claims.Add(new Claim(TeamClaimTypes.MemberKey, memberKey));
        claims.AddRange((held ?? []).Select(s => new Claim(TeamClaimTypes.SystemScope, s)));
        if (overseer) claims.Add(new Claim(TeamClaimTypes.SystemScope, SystemApiKeyScopes.Oversee));

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) });

        return new SystemApiKeyManagementService(_inner, accessor, Options.Create(new ApiKeyOptions()), null, withRegistry ? Registry() : null);
    }

    // Creating

    [Fact]
    public async Task Creating_WithScopesTheCallerHolds_Works()
    {
        var sut = Build([Read, Discover]);

        await sut.CreateSystemKeyAsync("k", [Read, Discover]);

        await _inner.Received(1).CreateSystemKeyAsync("k", Arg.Is<string[]>(s => s.Length == 2), null, Arg.Any<string>());
    }

    [Fact]
    public async Task Creating_WithAScopeTheCallerDoesNotHold_IsRefused_AndNothingIsStored()
    {
        var sut = Build([Read]);

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.CreateSystemKeyAsync("k", [Read, Manage]));

        Assert.Contains(Manage, error.Message);
        Assert.DoesNotContain(Read + ",", error.Message);
        await _inner.DidNotReceiveWithAnyArgs().CreateSystemKeyAsync(default, default);
    }

    [Fact]
    public async Task CreatingAPersonalKey_IsHeldToTheSameCeiling()
    {
        var sut = Build([Read], memberKey: "u1");
        _inner.GetSystemKeysAsync().Returns(_ => Async());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.CreateSystemKeyAsync("k", [Manage], personal: true));

        await _inner.DidNotReceiveWithAnyArgs().CreatePersonalSystemKeyAsync(default, default, default);
    }

    [Fact]
    public async Task Creating_WithAnUnregisteredScope_IsRefused()
    {
        var sut = Build(["made:up"]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateSystemKeyAsync("k", ["made:up"]));

        Assert.Contains("made:up", error.Message);
    }

    [Fact]
    public async Task AnOverseer_MayGrantAnyRegisteredScope_ButNotAnUnregisteredOne()
    {
        var sut = Build(overseer: true);

        await sut.CreateSystemKeyAsync("k", [Read, Manage, Discover]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateSystemKeyAsync("k", ["made:up"]));
    }

    [Fact]
    public async Task AKeyWithNoScopes_NeedsNothingHeld()
    {
        var sut = Build();

        await sut.CreateSystemKeyAsync("k", []);

        await _inner.Received(1).CreateSystemKeyAsync("k", Arg.Is<string[]>(s => s.Length == 0), null, Arg.Any<string>());
    }

    [Fact]
    public async Task WithNoRegistry_TheCeilingStillHolds()
    {
        var sut = Build([Read], withRegistry: false);

        await sut.CreateSystemKeyAsync("k", [Read]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.CreateSystemKeyAsync("k", [Manage]));
    }

    // Changing the scopes

    [Fact]
    public async Task Changing_ToScopesTheCallerHolds_KeepsTheKey()
    {
        var sut = Build([Read, Discover], keys: Key("k1", null, Read));

        await sut.SetSystemKeyScopesAsync("k1", [Read, Discover]);

        await _inner.Received(1).SetSystemKeyScopesAsync("k1", Arg.Is<string[]>(s => s.Length == 2));
        await _inner.DidNotReceiveWithAnyArgs().RefreshSystemKeyAsync(default);
    }

    [Fact]
    public async Task Changing_ToAScopeTheCallerDoesNotHold_IsRefusedByTheServiceItself()
    {
        var sut = Build([Read], keys: Key("k1", null, Read));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetSystemKeyScopesAsync("k1", [Read, Manage]));

        await _inner.DidNotReceiveWithAnyArgs().SetSystemKeyScopesAsync(default, default);
    }

    [Fact]
    public async Task Changing_ToAnUnregisteredScope_IsRefused()
    {
        var sut = Build(["made:up", Read], keys: Key("k1", null, Read));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SetSystemKeyScopesAsync("k1", ["made:up"]));

        await _inner.DidNotReceiveWithAnyArgs().SetSystemKeyScopesAsync(default, default);
    }

    /// <summary>Somebody holding little cannot lower a key that holds more, any more than they could have made it.</summary>
    [Fact]
    public async Task AKeyThatHoldsMoreThanTheCaller_IsNotTheirsToChange_EvenToLessOrNothing()
    {
        var sut = Build([Read], keys: Key("k1", null, Read, Manage));

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetSystemKeyScopesAsync("k1", [Read]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetSystemKeyScopesAsync("k1", []));

        Assert.Contains(Manage, error.Message);
        await _inner.DidNotReceiveWithAnyArgs().SetSystemKeyScopesAsync(default, default);
    }

    [Fact]
    public async Task AnOverseer_MayChangeAnyKeyToAnyRegisteredScopes()
    {
        var sut = Build(overseer: true, keys: Key("k1", null, Manage));

        await sut.SetSystemKeyScopesAsync("k1", [Read, Discover]);

        await _inner.Received(1).SetSystemKeyScopesAsync("k1", Arg.Any<string[]>());
    }

    [Fact]
    public async Task ClearingAKeyThatTheCallerCouldHaveMade_Works()
    {
        var sut = Build([Read], keys: Key("k1", null, Read));

        await sut.SetSystemKeyScopesAsync("k1", []);

        await _inner.Received(1).SetSystemKeyScopesAsync("k1", Arg.Is<string[]>(s => s.Length == 0));
    }

    [Fact]
    public async Task APersonalKeyOfSomebodyElse_IsRefusedBeforeItsScopesAreSaid()
    {
        var sut = Build([Read, Manage], memberKey: "u1", keys: Key("k1", "u2", Read, Manage));

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetSystemKeyScopesAsync("k1", [Read]));

        Assert.DoesNotContain(Manage, error.Message);
        Assert.Contains("personal", error.Message);
    }

    [Fact]
    public async Task TheOwnerOfAPersonalKey_ChangesItWithinWhatTheyHold()
    {
        var sut = Build([Read, Discover], memberKey: "u1", keys: Key("k1", "u1", Read));

        await sut.SetSystemKeyScopesAsync("k1", [Read, Discover]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetSystemKeyScopesAsync("k1", [Read, Manage]));

        await _inner.Received(1).SetSystemKeyScopesAsync("k1", Arg.Any<string[]>());
    }

    [Fact]
    public async Task AnOverseer_MayChangeSomebodyElsesPersonalKey()
    {
        var sut = Build(overseer: true, memberKey: "u1", keys: Key("k1", "u2", Read));

        await sut.SetSystemKeyScopesAsync("k1", [Discover]);

        await _inner.Received(1).SetSystemKeyScopesAsync("k1", Arg.Any<string[]>());
    }

    // Acting on the whole key: refresh, lock, disable, delete

    private static readonly (string Name, Func<SystemApiKeyManagementService, Task> Act, Func<IApiKeyAdministrationService, Task> Reached)[] Operations =
    [
        ("refresh", s => s.RefreshSystemKeyAsync("k1"), i => i.RefreshSystemKeyAsync("k1")),
        ("lock", s => s.LockSystemKeyAsync("k1"), i => i.LockSystemKeyAsync("k1")),
        ("disable", s => s.SetSystemKeyDisabledAsync("k1", true), i => i.SetSystemKeyDisabledAsync("k1", true, Arg.Any<string>())),
        ("delete", s => s.DeleteSystemKeyAsync("k1"), i => i.DeleteSystemKeyAsync("k1"))
    ];

    public static TheoryData<string> OperationNames() => new(Operations.Select(o => o.Name));

    private static (Func<SystemApiKeyManagementService, Task> Act, Func<IApiKeyAdministrationService, Task> Reached) Operation(string name)
    {
        var o = Operations.Single(x => x.Name == name);
        return (o.Act, o.Reached);
    }

    /// <summary>Somebody holding little cannot delete, lock, disable or replace the secret of a key that holds more, any more than they could change its scopes.</summary>
    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task AKeyThatHoldsMoreThanTheCaller_IsNotTheirsToActOn(string name)
    {
        var (act, _) = Operation(name);
        var sut = Build([Read], keys: Key("k1", null, Read, Manage));

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => act(sut));

        Assert.Contains(Manage, error.Message);
        await _inner.DidNotReceiveWithAnyArgs().RefreshSystemKeyAsync(default);
        await _inner.DidNotReceiveWithAnyArgs().LockSystemKeyAsync(default);
        await _inner.DidNotReceiveWithAnyArgs().SetSystemKeyDisabledAsync(default, default, default);
        await _inner.DidNotReceiveWithAnyArgs().DeleteSystemKeyAsync(default);
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task AKeyWithinWhatTheCallerHolds_CanBeActedOn(string name)
    {
        var (act, reached) = Operation(name);
        var sut = Build([Read, Discover], keys: Key("k1", null, Read));

        await act(sut);

        await reached(_inner.Received(1));
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task AKeyWithNoScopes_CanBeActedOnByAnyone(string name)
    {
        var (act, reached) = Operation(name);
        var sut = Build(keys: Key("k1", null));

        await act(sut);

        await reached(_inner.Received(1));
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task AnOverseer_CanActOnAnyKey(string name)
    {
        var (act, reached) = Operation(name);
        var sut = Build(overseer: true, keys: Key("k1", null, Manage));

        await act(sut);

        await reached(_inner.Received(1));
    }

    /// <summary>Who may touch a personal key is decided before anything about its scopes is said.</summary>
    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task APersonalKeyOfSomebodyElse_IsRefusedBeforeItsScopesAreSaid_WhateverIsDoneToIt(string name)
    {
        var (act, _) = Operation(name);
        var sut = Build([Read], memberKey: "u1", keys: Key("k1", "u2", Read, Manage));

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => act(sut));

        Assert.DoesNotContain(Manage, error.Message);
        Assert.Contains("personal", error.Message);
    }

    [Theory]
    [MemberData(nameof(OperationNames))]
    public async Task TheOwnerOfAPersonalKey_IsStillHeldToWhatTheyHold(string name)
    {
        var (act, _) = Operation(name);
        var sut = Build([Read], memberKey: "u1", keys: Key("k1", "u1", Read, Manage));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => act(sut));
    }

    // The store

    [Fact]
    public async Task TheStore_ReplacesOnlyTheScopes_AndKeepsTheValue()
    {
        var repository = Substitute.For<IApiKeyRepository>();
        var sut = new ApiKeyAdministrationService(repository, Substitute.For<global::Tharga.Toolkit.Password.IApiKeyService>());
        var stored = Entity(scopes: [Read]);
        repository.GetAsync("k1").Returns(Task.FromResult(stored));

        await sut.SetSystemKeyScopesAsync("k1", [Discover, " " + Read + " ", Discover, ""]);

        await repository.Received(1).UpdateAsync("k1", Arg.Is<ApiKeyEntity>(e =>
            e.ApiKeyHash == "hash" && e.Name == "Integration" && e.ExpiryDate == stored.ExpiryDate && e.CreatedBy == "alice" &&
            e.SystemScopes.SequenceEqual(new[] { Discover, Read })));
    }

    [Fact]
    public async Task TheStore_TreatsNullAsNoScopes()
    {
        var repository = Substitute.For<IApiKeyRepository>();
        var sut = new ApiKeyAdministrationService(repository, Substitute.For<global::Tharga.Toolkit.Password.IApiKeyService>());
        repository.GetAsync("k1").Returns(Task.FromResult(Entity(scopes: [Read])));

        await sut.SetSystemKeyScopesAsync("k1", null);

        await repository.Received(1).UpdateAsync("k1", Arg.Is<ApiKeyEntity>(e => e.SystemScopes.Length == 0));
    }

    [Fact]
    public async Task TheStore_RefusesATeamKey()
    {
        var repository = Substitute.For<IApiKeyRepository>();
        var sut = new ApiKeyAdministrationService(repository, Substitute.For<global::Tharga.Toolkit.Password.IApiKeyService>());
        repository.GetAsync("team-key").Returns(Task.FromResult(Entity(teamKey: "team-1")));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetSystemKeyScopesAsync("team-key", [Read]));

        await repository.DidNotReceiveWithAnyArgs().UpdateAsync(default, default);
    }

    // Audit

    [Fact]
    public async Task AChange_IsAudited_WithTheKeyAndTheScopesAddedAndRemoved()
    {
        var collected = new CollectingAudit();
        var inner = Substitute.For<IApiKeyAdministrationService>();
        inner.GetSystemKeysAsync().Returns(_ => Async(Key("k1", null, Read, Manage)));
        var sut = new AuditingApiKeyServiceDecorator(inner, new CompositeAuditLogger([collected], Options.Create(new AuditOptions())), Substitute.For<IHttpContextAccessor>());

        await sut.SetSystemKeyScopesAsync("k1", [Read, Discover]);

        var entry = Assert.Single(collected.Entries);
        Assert.Equal("set-scopes", entry.Action);
        Assert.True(entry.Success);
        Assert.Equal("k1", entry.Metadata[AuditMetadataKeys.ApiKeyId]);
        Assert.Equal(Discover, entry.Metadata[AuditMetadataKeys.ApiKeyScopesAdded]);
        Assert.Equal(Manage, entry.Metadata[AuditMetadataKeys.ApiKeyScopesRemoved]);
        Assert.Equal("System", entry.Metadata["ApiKeyType"]);
    }

    [Fact]
    public async Task ARefusedChange_IsAuditedAsAFailure()
    {
        var collected = new CollectingAudit();
        var inner = Substitute.For<IApiKeyAdministrationService>();
        inner.GetSystemKeysAsync().Returns(_ => Async(Key("k1", null, Read)));
        inner.SetSystemKeyScopesAsync("k1", Arg.Any<string[]>()).Returns<Task>(_ => throw new UnauthorizedAccessException("no"));
        var sut = new AuditingApiKeyServiceDecorator(inner, new CompositeAuditLogger([collected], Options.Create(new AuditOptions())), Substitute.For<IHttpContextAccessor>());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetSystemKeyScopesAsync("k1", [Manage]));

        Assert.False(Assert.Single(collected.Entries).Success);
    }

    private static ApiKeyEntity Entity(string[] scopes = null, string teamKey = null) => new()
    {
        Id = global::MongoDB.Bson.ObjectId.GenerateNewId(),
        Key = "k1",
        Name = "Integration",
        ApiKeyHash = "hash",
        TeamKey = teamKey,
        SystemScopes = scopes,
        CreatedBy = "alice",
        ExpiryDate = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private sealed class CollectingAudit : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Log(AuditEntry entry) => Entries.Add(entry);

        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }
}
