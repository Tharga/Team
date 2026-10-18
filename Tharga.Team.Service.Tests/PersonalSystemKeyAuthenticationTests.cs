using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// A personal system key is its owner's credential, so it stops working when they are deleted or disabled,
/// and a store fault refuses it (Tharga/Team#314, following #323 for personal team keys).
/// </summary>
public class PersonalSystemKeyAuthenticationTests
{
    private readonly IApiKeyAdministrationService _apiKeyService = Substitute.For<IApiKeyAdministrationService>();
    private readonly IUserService _userService = Substitute.For<IUserService>();

    private async Task<AuthenticateResult> AuthenticateAsync(IApiKey key, bool withUserService = true)
    {
        _apiKeyService.GetByApiKeyAsync("raw").Returns(key);

        var optionsMonitor = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        optionsMonitor.Get(ApiKeyConstants.SchemeName).Returns(new AuthenticationSchemeOptions());
        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

        var handler = new ApiKeyAuthenticationHandler(optionsMonitor, loggerFactory, UrlEncoder.Default, _apiKeyService,
            userService: withUserService ? _userService : null);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[ApiKeyConstants.HeaderName] = "raw";
        await handler.InitializeAsync(new AuthenticationScheme(ApiKeyConstants.SchemeName, "API Key", typeof(ApiKeyAuthenticationHandler)), ctx);
        return await handler.AuthenticateAsync();
    }

    private static IApiKey SystemKey(string owner)
    {
        var key = Substitute.For<IApiKey>();
        key.Key.Returns("k1");
        key.Name.Returns("mine");
        key.TeamKey.Returns((string)null);
        key.OwnerMemberKey.Returns(owner);
        key.SystemScopes.Returns(["sys:read"]);
        key.Tags.Returns(Array.Empty<Tag>());
        return key;
    }

    private void Owner(string userKey, DateTime? disabledAt = null)
    {
        var user = Substitute.For<IUser>();
        user.Key.Returns(userKey);
        user.DisabledAt.Returns(disabledAt);
        _userService.GetUserByKeyAsync(userKey).Returns(user);
    }

    [Fact]
    public async Task AnActiveOwner_KeepsTheKeyWorking()
    {
        Owner("u1");

        var result = await AuthenticateAsync(SystemKey("u1"));

        Assert.True(result.Succeeded);
        Assert.Equal("u1", result.Principal!.FindFirst(TeamClaimTypes.ApiKeyOwner)?.Value);
        Assert.True(result.Principal.HasClaim(TeamClaimTypes.SystemScope, "sys:read"));
    }

    [Fact]
    public async Task ADeletedOwner_StopsTheKey()
    {
        _userService.GetUserByKeyAsync("u1").Returns((IUser)null);

        var result = await AuthenticateAsync(SystemKey("u1"));

        Assert.False(result.Succeeded);
        Assert.Contains("no longer exists", result.Failure!.Message);
    }

    [Fact]
    public async Task ADisabledOwner_StopsTheKey()
    {
        Owner("u1", disabledAt: DateTime.UtcNow);

        var result = await AuthenticateAsync(SystemKey("u1"));

        Assert.False(result.Succeeded);
        Assert.Contains("disabled", result.Failure!.Message);
    }

    [Fact]
    public async Task AStoreFault_RefusesTheKey()
    {
        _userService.GetUserByKeyAsync("u1").Returns<IUser>(_ => throw new TimeoutException("store down"));

        var result = await AuthenticateAsync(SystemKey("u1"));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task WithNoUserStore_ThePersonalKeyIsRefused()
    {
        var result = await AuthenticateAsync(SystemKey("u1"), withUserService: false);

        Assert.False(result.Succeeded);
    }

    /// <summary>A shared system key has no owner to look up, so the user store is never asked.</summary>
    [Fact]
    public async Task ASharedSystemKey_IsNotTiedToAnyone()
    {
        var result = await AuthenticateAsync(SystemKey(null));

        Assert.True(result.Succeeded);
        Assert.Null(result.Principal!.FindFirst(TeamClaimTypes.ApiKeyOwner));
        await _userService.DidNotReceiveWithAnyArgs().GetUserByKeyAsync(default);
    }
}
