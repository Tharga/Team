using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Personal system keys through <see cref="SystemApiKeyManagementService"/> (Tharga/Team#314): who owns a new
/// key, who sees it, who may change it, and what <see cref="ApiKeyOptions.SystemKeys"/> allows.
/// </summary>
public class SystemApiKeyPersonalKeysTests
{
    private readonly IApiKeyAdministrationService _inner = Substitute.For<IApiKeyAdministrationService>();
    private readonly IUserService _userService = Substitute.For<IUserService>();

    private static IApiKey Key(string id, string owner = null)
    {
        var k = Substitute.For<IApiKey>();
        k.Key.Returns(id);
        k.TeamKey.Returns((string)null);
        k.OwnerMemberKey.Returns(owner);
        return k;
    }

    private static async IAsyncEnumerable<IApiKey> ToAsyncEnumerable(params IApiKey[] items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    /// <param name="memberKey">The caller's member key claim, present when a team is selected.</param>
    /// <param name="userKey">The caller's user key as the user store resolves it.</param>
    private SystemApiKeyManagementService Build(
        string memberKey = null, string userKey = "u1", bool overseer = false, bool isApiKey = false,
        bool authenticated = true, ApiKeyOwnership ownership = ApiKeyOwnership.Both, params IApiKey[] keys)
    {
        _inner.GetSystemKeysAsync().Returns(_ => ToAsyncEnumerable(keys));

        var claims = new List<Claim> { new(ClaimTypes.Name, "alice") };
        if (memberKey != null) claims.Add(new Claim(TeamClaimTypes.MemberKey, memberKey));
        if (overseer) claims.Add(new Claim(TeamClaimTypes.SystemScope, SystemApiKeyScopes.Oversee));
        if (isApiKey) claims.Add(new Claim(TeamClaimTypes.ApiKeyId, "calling-key"));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null));
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = principal });

        var user = Substitute.For<IUser>();
        user.Key.Returns(userKey);
        _userService.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(userKey == null ? null : user);

        return new SystemApiKeyManagementService(_inner, accessor, Options.Create(new ApiKeyOptions { SystemKeys = ownership }), _userService);
    }

    private static async Task<string[]> Ids(IAsyncEnumerable<IApiKey> keys)
    {
        var list = new List<string>();
        await foreach (var k in keys) list.Add(k.Key);
        return list.ToArray();
    }

    // Listing

    [Fact]
    public async Task ByDefault_OnlySharedKeysAreListed()
    {
        var sut = Build(keys: [Key("shared"), Key("mine", "u1")]);

        Assert.Equal(["shared"], await Ids(sut.GetSystemKeysAsync()));
    }

    [Fact]
    public async Task Mine_AddsTheCallersOwnKeys()
    {
        var sut = Build(keys: [Key("shared"), Key("mine", "u1"), Key("theirs", "u2")]);

        Assert.Equal(["shared", "mine"], await Ids(sut.GetSystemKeysAsync(PrivateKeyScope.Mine)));
    }

    [Fact]
    public async Task Mine_DoesNotShowOthersKeysEvenToAnOverseer()
    {
        var sut = Build(overseer: true, keys: [Key("mine", "u1"), Key("theirs", "u2")]);

        Assert.Equal(["mine"], await Ids(sut.GetSystemKeysAsync(PrivateKeyScope.Mine)));
    }

    [Fact]
    public async Task All_ShowsEveryonesKeysToAnOverseer()
    {
        var sut = Build(overseer: true, keys: [Key("shared"), Key("mine", "u1"), Key("theirs", "u2")]);

        Assert.Equal(["shared", "mine", "theirs"], await Ids(sut.GetSystemKeysAsync(PrivateKeyScope.All)));
    }

    [Fact]
    public async Task All_WithoutOversight_StillShowsOnlyYourOwn()
    {
        var sut = Build(keys: [Key("mine", "u1"), Key("theirs", "u2")]);

        Assert.Equal(["mine"], await Ids(sut.GetSystemKeysAsync(PrivateKeyScope.All)));
    }

    /// <summary>The member key is the user key, so it identifies the owner without asking the user store.</summary>
    [Fact]
    public async Task TheMemberKey_IdentifiesTheOwner_WhenATeamIsSelected()
    {
        var sut = Build(memberKey: "u1", userKey: null, keys: [Key("mine", "u1")]);

        Assert.Equal(["mine"], await Ids(sut.GetSystemKeysAsync(PrivateKeyScope.Mine)));
        await _userService.DidNotReceiveWithAnyArgs().GetCurrentUserAsync(default);
    }

    // Creating

    [Fact]
    public async Task APersonalKey_IsOwnedByTheCaller()
    {
        var sut = Build();

        await sut.CreateSystemKeyAsync("mine", ["sys:read"], personal: true);

        await _inner.Received(1).CreatePersonalSystemKeyAsync("mine", Arg.Any<string[]>(), "u1", Arg.Any<DateTime?>(), "alice");
        await _inner.DidNotReceiveWithAnyArgs().CreateSystemKeyAsync(default, default);
    }

    [Fact]
    public async Task ASharedKey_HasNoOwner()
    {
        var sut = Build();

        await sut.CreateSystemKeyAsync("shared", ["sys:read"]);

        await _inner.Received(1).CreateSystemKeyAsync("shared", Arg.Any<string[]>(), Arg.Any<DateTime?>(), "alice");
        await _inner.DidNotReceiveWithAnyArgs().CreatePersonalSystemKeyAsync(default, default, default);
    }

    [Fact]
    public async Task SharedOption_RefusesAPersonalKey()
    {
        var sut = Build(ownership: ApiKeyOwnership.Shared);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateSystemKeyAsync("mine", [], personal: true));

        await _inner.DidNotReceiveWithAnyArgs().CreatePersonalSystemKeyAsync(default, default, default);
        await _inner.DidNotReceiveWithAnyArgs().CreateSystemKeyAsync(default, default);
    }

    [Fact]
    public async Task PersonalOption_MakesEveryKeyPersonal()
    {
        var sut = Build(ownership: ApiKeyOwnership.Personal);

        await sut.CreateSystemKeyAsync("k", []);

        await _inner.Received(1).CreatePersonalSystemKeyAsync("k", Arg.Any<string[]>(), "u1", Arg.Any<DateTime?>(), Arg.Any<string>());
    }

    /// <summary>An API key is not a person, so it can't own a key — even if the store would resolve a user for it.</summary>
    [Fact]
    public async Task AnApiKeyCaller_CannotCreateAPersonalKey()
    {
        var sut = Build(isApiKey: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateSystemKeyAsync("mine", [], personal: true));

        await _inner.DidNotReceiveWithAnyArgs().CreatePersonalSystemKeyAsync(default, default, default);
    }

    [Fact]
    public async Task AnAnonymousCaller_CannotCreateAPersonalKey()
    {
        var sut = Build(authenticated: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateSystemKeyAsync("mine", [], personal: true));
    }

    // Changing

    public static TheoryData<string> Mutations() => ["refresh", "lock", "disable", "delete"];

    private static Task Mutate(SystemApiKeyManagementService sut, string action, string key) => action switch
    {
        "refresh" => sut.RefreshSystemKeyAsync(key),
        "lock" => sut.LockSystemKeyAsync(key),
        "disable" => sut.SetSystemKeyDisabledAsync(key, true),
        "delete" => sut.DeleteSystemKeyAsync(key),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private Task ReceivedMutation(string action, string key) => action switch
    {
        "refresh" => _inner.Received(1).RefreshSystemKeyAsync(key),
        "lock" => _inner.Received(1).LockSystemKeyAsync(key),
        "disable" => _inner.Received(1).SetSystemKeyDisabledAsync(key, true, Arg.Any<string>()),
        "delete" => _inner.Received(1).DeleteSystemKeyAsync(key),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task SomeoneElsesPersonalKey_CannotBeChanged(string action)
    {
        var sut = Build(keys: [Key("theirs", "u2")]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Mutate(sut, action, "theirs"));

        await _inner.DidNotReceiveWithAnyArgs().RefreshSystemKeyAsync(default);
        await _inner.DidNotReceiveWithAnyArgs().LockSystemKeyAsync(default);
        await _inner.DidNotReceiveWithAnyArgs().SetSystemKeyDisabledAsync(default, default, default);
        await _inner.DidNotReceiveWithAnyArgs().DeleteSystemKeyAsync(default);
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task YourOwnPersonalKey_CanBeChanged(string action)
    {
        var sut = Build(keys: [Key("mine", "u1")]);

        await Mutate(sut, action, "mine");

        await ReceivedMutation(action, "mine");
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task AnOverseer_CanChangeSomeoneElsesPersonalKey(string action)
    {
        var sut = Build(overseer: true, keys: [Key("theirs", "u2")]);

        await Mutate(sut, action, "theirs");

        await ReceivedMutation(action, "theirs");
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task ASharedKey_CanBeChangedByAnyone(string action)
    {
        var sut = Build(userKey: null, keys: [Key("shared")]);

        await Mutate(sut, action, "shared");

        await ReceivedMutation(action, "shared");
    }
}
