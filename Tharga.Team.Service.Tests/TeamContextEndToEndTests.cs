using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// A real request, through the real pipeline: authentication, the middleware, and an endpoint that knows
/// nothing about any of it.
/// </summary>
/// <remarks>
/// Everything else about team context is unit-level. That matters because every genuine defect this area
/// produced was found by <i>running</i> something rather than reading it — a sample that would not start
/// because a singleton captured a scoped service, a guard that found no files on Linux, three guards that
/// passed while examining nothing. None were visible to a unit test of the piece involved.
/// <para>
/// The endpoint below deliberately contains no authorization logic beyond asking
/// <see cref="TeamScopePolicy"/> the ordinary question. If a host's own controller works, the claim that
/// the header is covered "for free" is true; if it needs to know about headers, the claim was false.
/// </para>
/// </remarks>
public class TeamContextEndToEndTests
{
    private const string TeamKeyValue = "team-key";
    private const string SystemKeyValue = "system-key";
    private const string BoundTeam = "team-1";
    private const string ConsentingTeam = "team-2";
    private const string ClosedTeam = "team-3";
    private const string InteractiveScheme = "Interactive";

    private sealed record FakeApiKey(string Key, string Name, string TeamKey, string[] SystemScopes) : IApiKey
    {
        public string ApiKey => null;
        public string CreatedBy => null;
        public string OwnerMemberKey => null;
        public IReadOnlyList<Tag> Tags => [];
        public AccessLevel? AccessLevel => Tharga.Team.AccessLevel.Administrator;
        public string[] Roles => [];
        public string[] ScopeOverrides => [];
        public DateTime? ExpiryDate => null;
        public DateTime? CreatedAt => DateTime.UtcNow;
        public DateTime? LastUsedAt => null;
        public DateTime? DisabledAt => null;
        public string DisabledBy => null;
    }

    private sealed class FakeApiKeyStore : IApiKeyAdministrationService
    {
        public Task<IApiKey> GetByApiKeyAsync(string apiKey) => Task.FromResult<IApiKey>(apiKey switch
        {
            TeamKeyValue => new FakeApiKey("k1", "Team", BoundTeam, null),
            SystemKeyValue => new FakeApiKey("k2", "System", null, ["audit:read"]),
            _ => null
        });

        private static T NotUsed<T>() => throw new NotSupportedException("Not part of the authentication path.");

        public IAsyncEnumerable<IApiKey> GetKeysAsync(string teamKey) => NotUsed<IAsyncEnumerable<IApiKey>>();
        public Task<IApiKey> CreateKeyAsync(string teamKey, string name, AccessLevel accessLevel, string[] roles = null, string[] scopeOverrides = null, DateTime? expiryDate = null, IReadOnlyList<Tag> tags = null, string createdBy = null, string ownerMemberKey = null) => NotUsed<Task<IApiKey>>();
        public Task<IApiKey> RefreshKeyAsync(string teamKey, string key) => NotUsed<Task<IApiKey>>();
        public Task LockKeyAsync(string teamKey, string key) => NotUsed<Task>();
        public Task DeleteKeyAsync(string teamKey, string key) => NotUsed<Task>();
        public Task SetScopeOverridesAsync(string teamKey, string key, string[] scopes) => NotUsed<Task>();
        public Task SetRolesAsync(string teamKey, string key, string[] roles) => NotUsed<Task>();
        public IAsyncEnumerable<IApiKey> GetSystemKeysAsync() => NotUsed<IAsyncEnumerable<IApiKey>>();
        public Task<IApiKey> CreateSystemKeyAsync(string name, string[] scopes, DateTime? expiryDate = null, string createdBy = null) => NotUsed<Task<IApiKey>>();
        public Task<IApiKey> RefreshSystemKeyAsync(string key) => NotUsed<Task<IApiKey>>();
        public Task LockSystemKeyAsync(string key) => NotUsed<Task>();
        public Task DeleteSystemKeyAsync(string key) => NotUsed<Task>();
        public Task SetKeyDisabledAsync(string teamKey, string key, bool disabled, string actor = null) => NotUsed<Task>();
        public Task SetSystemKeyDisabledAsync(string key, bool disabled, string actor = null) => NotUsed<Task>();
    }

    private sealed record FakeTeam(string Key, string[] ConsentedRoles, AccessLevel? ConsentAccessLevel) : ITeam
    {
        public string Name => Key;
        public string Icon => null;
    }

    private static async Task<IHost> StartHostAsync()
        => await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication(InteractiveScheme)
                        .AddCookie(InteractiveScheme)
                        .AddThargaApiKeyAuthentication<FakeApiKeyStore>();

                    var teamService = Substitute.For<ITeamService>();
                    teamService.GetTeamByKeyAsync(Arg.Any<string>()).Returns((ITeam)null);

                    // The key's own team has to exist. Authentication now refuses a key whose team has been
                    // deleted, so a fixture where the bound team never existed authenticates nothing.
                    teamService.GetTeamByKeyAsync(BoundTeam)
                        .Returns(new FakeTeam(BoundTeam, [], AccessLevel.Administrator));
                    teamService.GetTeamByKeyAsync(ConsentingTeam)
                        .Returns(new FakeTeam(ConsentingTeam, ["Support"], AccessLevel.Administrator));
                    teamService.GetTeamByKeyAsync(ClosedTeam)
                        .Returns(new FakeTeam(ClosedTeam, [], AccessLevel.Administrator));
                    services.AddScoped(_ => teamService);

                    var registry = Substitute.For<IScopeRegistry>();
                    registry.GetEffectiveScopes(Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>())
                        .Returns(["audit:read"]);
                    services.AddScoped(_ => registry);

                    services.AddScoped<TeamContextResolver>();
                    services.Configure<TeamContextOptions>(_ => { });
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();

                    // AFTER authorization, deliberately. A host authenticates API keys through a policy
                    // that names the scheme, and that happens during authorization -- so a middleware
                    // placed before it sees an unauthenticated caller and silently does nothing. This is
                    // the order UseThargaControllers uses.
                    app.UseAuthorization();
                    app.UseMiddleware<TeamContextMiddleware>();
                    app.UseEndpoints(endpoints =>
                    {
                        // A host's own endpoint. It knows nothing about headers or team context -- it asks
                        // the ordinary question and is answered from the claims.
                        endpoints.MapGet("/probe/{team}", (HttpContext ctx, string team) =>
                            TeamScopePolicy.HasTeamScope(ctx.User, "audit:read", team)
                                ? Results.Ok(team)
                                : Results.StatusCode(StatusCodes.Status403Forbidden))
                            .RequireAuthorization(ApiKeyConstants.AnyKeyPolicyName);
                    });
                });
            })
            .StartAsync();

    private static async Task<HttpStatusCode> CallAsync(IHost host, string path, string apiKey, string teamHeader = null)
    {
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (teamHeader != null) request.Headers.Add("X-Team-Key", teamHeader);

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>A team key reaches its own team with no parameter and no header.</summary>
    [Fact]
    public async Task ATeamKey_ReachesItsOwnTeam()
    {
        using var host = await StartHostAsync();

        Assert.Equal(HttpStatusCode.OK, await CallAsync(host, $"/probe/{BoundTeam}", TeamKeyValue));
    }

    [Fact]
    public async Task ATeamKey_DoesNotReachAnotherTeam()
    {
        using var host = await StartHostAsync();

        Assert.Equal(HttpStatusCode.Forbidden, await CallAsync(host, $"/probe/{ConsentingTeam}", TeamKeyValue));
    }

    /// <summary>And naming one in the header is refused outright, before the endpoint runs.</summary>
    [Fact]
    public async Task ATeamKey_NamingAnotherTeamInTheHeader_IsRefused()
    {
        using var host = await StartHostAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            await CallAsync(host, $"/probe/{BoundTeam}", TeamKeyValue, teamHeader: ConsentingTeam));
    }

    /// <summary>
    /// The claim this whole design rests on: a system key naming a consenting team satisfies an ordinary
    /// team-scope check in an endpoint that was never told headers exist.
    /// </summary>
    [Fact]
    public async Task ASystemKey_NamingAConsentingTeam_ReachesIt()
    {
        using var host = await StartHostAsync();

        Assert.Equal(HttpStatusCode.OK,
            await CallAsync(host, $"/probe/{ConsentingTeam}", SystemKeyValue, teamHeader: ConsentingTeam));
    }

    [Fact]
    public async Task ASystemKey_NamingANonConsentingTeam_IsRefused()
    {
        using var host = await StartHostAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            await CallAsync(host, $"/probe/{ClosedTeam}", SystemKeyValue, teamHeader: ClosedTeam));
    }

    /// <summary>
    /// Without the header a system key has no team context, so a team-scoped check refuses it — its system
    /// grant authorizes system-wide operations, not this one.
    /// </summary>
    [Fact]
    public async Task ASystemKey_WithNoHeader_HasNoTeamScope()
    {
        using var host = await StartHostAsync();

        Assert.Equal(HttpStatusCode.Forbidden, await CallAsync(host, $"/probe/{ConsentingTeam}", SystemKeyValue));
    }

    /// <summary>Naming one team does not reach another — selection narrows, it never accumulates.</summary>
    [Fact]
    public async Task ASystemKey_ReachesOnlyTheTeamItNamed()
    {
        using var host = await StartHostAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            await CallAsync(host, $"/probe/{ClosedTeam}", SystemKeyValue, teamHeader: ConsentingTeam));
    }
}
