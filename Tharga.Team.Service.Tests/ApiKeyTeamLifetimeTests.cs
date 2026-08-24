using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// An API key must not outlive the team it belongs to.
/// </summary>
/// <remarks>
/// <b>The ordinary delete path is the one that matters.</b> Deleting a team soft-deletes it, and every other
/// read in the toolkit excludes soft-deleted teams — but <c>ApiKeyAuthenticationHandler</c> never looks the
/// team up at all, so its keys keep authenticating and keep carrying that team's scope claims. This needs no
/// purge and no key reuse.
/// <para>
/// <b>A system key has no team</b> and must be untouched by any of this. It is the obvious way to break the
/// fix, so it is asserted rather than assumed.
/// </para>
/// </remarks>
public class ApiKeyTeamLifetimeTests
{
    private const string TeamKey = "acme";
    private const string Secret = "the-secret";

    private readonly IApiKeyAdministrationService _apiKeyService = Substitute.For<IApiKeyAdministrationService>();
    private readonly ITeamService _teamService = Substitute.For<ITeamService>();

    [Fact]
    public async Task AKeyWhoseTeamIsDeleted_DoesNotAuthenticate()
    {
        GivenKey(TeamKey);
        GivenTeamIsGone(TeamKey);

        var result = await Authenticate();

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AKeyWhoseTeamIsLive_StillAuthenticates()
    {
        GivenKey(TeamKey);
        GivenTeamIsLive(TeamKey);

        var result = await Authenticate();

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// A system key carries no team, so the liveness check must not run for it at all.
    /// </summary>
    [Fact]
    public async Task ASystemKey_IsUnaffected()
    {
        GivenKey(teamKey: null);

        var result = await Authenticate();

        Assert.True(result.Succeeded);
        await _teamService.DidNotReceive().GetTeamByKeyAsync(Arg.Any<string>());
    }

    /// <summary>
    /// The check follows the team's current state rather than latching, so restoring a team restores its
    /// keys.
    /// </summary>
    [Fact]
    public async Task RestoringATeam_RestoresItsKeys()
    {
        GivenKey(TeamKey);
        GivenTeamIsGone(TeamKey);
        Assert.False((await Authenticate()).Succeeded);

        GivenTeamIsLive(TeamKey);

        Assert.True((await Authenticate()).Succeeded);
    }

    private void GivenKey(string teamKey)
    {
        var key = Substitute.For<IApiKey>();
        key.TeamKey.Returns(teamKey);
        key.Name.Returns("Test Key");
        key.Tags.Returns(Array.Empty<Tag>());

        _apiKeyService.GetByApiKeyAsync(Secret).Returns(key);
    }

    private void GivenTeamIsLive(string teamKey)
    {
        var team = Substitute.For<ITeam>();
        team.Key.Returns(teamKey);
        _teamService.GetTeamByKeyAsync(teamKey).Returns(team);
    }

    private void GivenTeamIsGone(string teamKey)
        => _teamService.GetTeamByKeyAsync(teamKey).Returns((ITeam)null);

    private async Task<AuthenticateResult> Authenticate()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[ApiKeyConstants.HeaderName] = Secret;

        var optionsMonitor = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        optionsMonitor.Get(ApiKeyConstants.SchemeName).Returns(new AuthenticationSchemeOptions());

        var loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

        var handler = new ApiKeyAuthenticationHandler(
            optionsMonitor,
            loggerFactory,
            UrlEncoder.Default,
            _apiKeyService,
            teamService: _teamService);

        await handler.InitializeAsync(
            new AuthenticationScheme(ApiKeyConstants.SchemeName, "API Key", typeof(ApiKeyAuthenticationHandler)),
            context);

        return await handler.AuthenticateAsync();
    }
}
