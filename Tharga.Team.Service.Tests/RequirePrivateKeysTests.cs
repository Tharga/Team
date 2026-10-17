using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Toolkit.Password;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// <see cref="ApiKeyOptions.RequirePrivateKeys"/>: every new team key has an owner (Tharga/Team#314).
/// </summary>
/// <remarks>
/// Held at both creation paths. The administration service is the one every team key goes through; the
/// management service checks as well, so the UI is held to it even under a host's own administration service.
/// </remarks>
public class RequirePrivateKeysTests
{
    private const string Team = "team-1";

    private readonly IApiKeyRepository _repository = Substitute.For<IApiKeyRepository>();
    private readonly IApiKeyService _apiKeyService = Substitute.For<IApiKeyService>();

    public RequirePrivateKeysTests()
    {
        _apiKeyService.BuildApiKey(Arg.Any<string>(), Arg.Any<Func<string>>()).Returns("new-key");
        _apiKeyService.Encrypt("new-key").Returns("new-hash");
        _repository.AddAsync(Arg.Any<ApiKeyEntity>()).Returns(ci => Task.FromResult(ci.Arg<ApiKeyEntity>()));
    }

    private ApiKeyAdministrationService Administration(bool require, bool advanced = true)
        => new(_repository, _apiKeyService, Options.Create(new ApiKeyOptions { RequirePrivateKeys = require, AdvancedMode = advanced }));

    private static async IAsyncEnumerable<T> Async<T>(params T[] items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    private static (ApiKeyManagementService sut, IApiKeyAdministrationService inner) Management(bool require, string memberKey = "m1", params IApiKey[] keys)
    {
        var inner = Substitute.For<IApiKeyAdministrationService>();
        inner.GetKeysAsync(Team).Returns(_ => Async(keys));

        var claims = memberKey == null ? new List<Claim>() : new List<Claim> { new(TeamClaimTypes.MemberKey, memberKey) };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) });

        return (new ApiKeyManagementService(inner, accessor, Options.Create(new ApiKeyOptions { RequirePrivateKeys = require, AdvancedMode = true })), inner);
    }

    private static IApiKey TeamWideKey(string id)
    {
        var key = Substitute.For<IApiKey>();
        key.Key.Returns(id);
        return key;
    }

    // ---- The administration service: the single path every team key goes through ----

    [Fact]
    public async Task Administration_KeyWithNoOwner_IsRefused_AndNothingIsStored()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Administration(require: true).CreateKeyAsync(Team, "Shared", AccessLevel.User));

        Assert.Contains("RequirePrivateKeys", ex.Message);
        await _repository.DidNotReceive().AddAsync(Arg.Any<ApiKeyEntity>());
    }

    [Fact]
    public async Task Administration_KeyWithAnOwner_IsCreated()
    {
        var created = await Administration(require: true).CreateKeyAsync(Team, "Mine", AccessLevel.User, ownerMemberKey: "m1");

        Assert.Equal("m1", created.OwnerMemberKey);
    }

    [Fact]
    public async Task Administration_OptionOff_KeyWithNoOwner_IsCreatedAsBefore()
    {
        var created = await Administration(require: false).CreateKeyAsync(Team, "Shared", AccessLevel.User);

        Assert.Null(created.OwnerMemberKey);
    }

    /// <summary>
    /// Simple mode's auto-created keys have no owner. The combination fails at startup; the service itself
    /// still creates none, so the promise holds where no host runs that check.
    /// </summary>
    [Fact]
    public async Task Administration_SimpleMode_AutoCreatesNoOwnerlessKeys()
    {
        _repository.GetAsync().Returns(Async<ApiKeyEntity>());

        var keys = await Administration(require: true, advanced: false).GetKeysAsync(Team).ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Empty(keys);
        await _repository.DidNotReceive().AddAsync(Arg.Any<ApiKeyEntity>());
    }

    // ---- The management service: what the UI and host code call ----

    [Fact]
    public async Task Management_TeamWideRequest_IsRefused_NotTurnedIntoTheCallersKey()
    {
        var (sut, inner) = Management(require: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.CreateKeyAsync(Team, "Shared", AccessLevel.User, ownerScoped: false));

        Assert.Contains("RequirePrivateKeys", ex.Message);
        await inner.DidNotReceiveWithAnyArgs().CreateKeyAsync(default, default, default);
    }

    [Fact]
    public async Task Management_PrivateRequest_IsOwnedByTheCaller()
    {
        var (sut, inner) = Management(require: true, memberKey: "m1");

        await sut.CreateKeyAsync(Team, "Mine", AccessLevel.User, ownerScoped: true);

        await inner.Received(1).CreateKeyAsync(Team, "Mine", AccessLevel.User, Arg.Any<string[]>(), Arg.Any<string[]>(),
            Arg.Any<DateTime?>(), Arg.Any<IReadOnlyList<Tag>>(), Arg.Any<string>(), "m1");
    }

    [Fact]
    public async Task Management_OptionOff_TeamWideRequest_IsCreatedAsBefore()
    {
        var (sut, inner) = Management(require: false);

        await sut.CreateKeyAsync(Team, "Shared", AccessLevel.User);

        await inner.Received(1).CreateKeyAsync(Team, "Shared", AccessLevel.User, Arg.Any<string[]>(), Arg.Any<string[]>(),
            Arg.Any<DateTime?>(), Arg.Any<IReadOnlyList<Tag>>(), Arg.Any<string>(), (string)null);
    }

    /// <summary>The option governs new keys only: an existing team-wide key is still listed and managed.</summary>
    [Fact]
    public async Task Management_ExistingTeamWideKey_IsStillListedAndManageable()
    {
        var (sut, inner) = Management(require: true, keys: TeamWideKey("shared"));

        var listed = await sut.GetKeysAsync(Team).ToArrayAsync(TestContext.Current.CancellationToken);
        await sut.RefreshKeyAsync(Team, "shared");
        await sut.DeleteKeyAsync(Team, "shared");

        Assert.Equal(["shared"], listed.Select(k => k.Key));
        await inner.Received(1).RefreshKeyAsync(Team, "shared");
        await inner.Received(1).DeleteKeyAsync(Team, "shared");
    }

    // ---- Startup: the option cannot hold in simple mode ----

    private static Task Start(bool require, bool advanced)
        => new RequirePrivateKeysCheck(Options.Create(new ApiKeyOptions { RequirePrivateKeys = require, AdvancedMode = advanced }))
            .StartAsync(CancellationToken.None);

    [Fact]
    public async Task Startup_RequiredInSimpleMode_Fails_NamingBothOptions()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Start(require: true, advanced: false));

        Assert.Contains("RequirePrivateKeys", ex.Message);
        Assert.Contains("AdvancedMode", ex.Message);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Startup_AnyOtherCombination_Passes(bool require, bool advanced)
    {
        await Start(require, advanced);
    }

    /// <summary>Wired through the real registration, reading the bound options, and registered once.</summary>
    [Fact]
    public async Task Startup_TheCheckIsRegisteredWithTheScheme_AndReadsTheBoundOptions()
    {
        var services = new ServiceCollection();
        services.AddAuthentication().AddThargaApiKeyAuthentication(o => o.RequirePrivateKeys = true);
        services.AddAuthentication().AddThargaApiKeyAuthentication(o => o.RequirePrivateKeys = true);

        var checks = services.BuildServiceProvider().GetServices<IHostedService>().OfType<RequirePrivateKeysCheck>().ToArray();

        var check = Assert.Single(checks);
        await Assert.ThrowsAsync<InvalidOperationException>(() => check.StartAsync(CancellationToken.None));
    }
}
