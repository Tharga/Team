using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tharga.Mcp;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Mcp.Tests;

/// <summary>
/// The same expectations as the REST end-to-end tests, through the real MCP accessor.
/// </summary>
/// <remarks>
/// Both surfaces call <c>TeamContextResolver</c>, so they <i>should</i> agree — but "should, because they
/// share code" is what was true of audit before the MCP resource grew its own rule. Asserting the same
/// table on both sides means a divergence shows up as a failing test rather than as a difference nobody
/// thought to compare.
/// <para>
/// This drives the accessor through a live request rather than constructing an <c>HttpContext</c> by
/// hand: the REST equivalent found that the middleware saw an unauthenticated caller because API keys are
/// authenticated during <i>authorization</i>, and a hand-built context would have hidden exactly that.
/// </para>
/// </remarks>
public class McpTeamContextEndToEndTests
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
                    services.AddHttpContextAccessor();
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

                    services.Configure<McpTeamOptions>(_ => { });
                    services.Configure<ConsentOptions>(_ => { });
                    services.Configure<TeamContextOptions>(_ => { });
                    services.AddSingleton<IMcpContextAccessor, HttpContextMcpContextAccessor>();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        // Resolves the MCP context exactly as a provider dispatch would, on a live request.
                        endpoints.MapGet("/mcp-context", (IMcpContextAccessor accessor) =>
                        {
                            try
                            {
                                return Results.Ok(accessor.Current.AsTeamContext()?.TeamId ?? "(none)");
                            }
                            catch (UnauthorizedAccessException)
                            {
                                return Results.StatusCode(StatusCodes.Status403Forbidden);
                            }
                        }).RequireAuthorization(ApiKeyConstants.AnyKeyPolicyName);
                    });
                });
            })
            .StartAsync();

    private static async Task<(HttpStatusCode Status, string Team)> CallAsync(IHost host, string apiKey, string teamHeader = null)
    {
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp-context");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (teamHeader != null) request.Headers.Add("X-Team-Key", teamHeader);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, body.Trim('"'));
    }

    [Fact]
    public async Task ATeamKey_ActsOnItsOwnTeam()
    {
        using var host = await StartHostAsync();

        var (status, team) = await CallAsync(host, TeamKeyValue);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(BoundTeam, team);
    }

    /// <summary>Refused, exactly as on REST — the same contradiction, the same answer.</summary>
    [Fact]
    public async Task ATeamKey_NamingAnotherTeam_IsRefused()
    {
        using var host = await StartHostAsync();

        var (status, _) = await CallAsync(host, TeamKeyValue, ConsentingTeam);

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task ASystemKey_NamingAConsentingTeam_ActsOnIt()
    {
        using var host = await StartHostAsync();

        var (status, team) = await CallAsync(host, SystemKeyValue, ConsentingTeam);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(ConsentingTeam, team);
    }

    [Fact]
    public async Task ASystemKey_NamingANonConsentingTeam_IsRefused()
    {
        using var host = await StartHostAsync();

        var (status, _) = await CallAsync(host, SystemKeyValue, ClosedTeam);

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task ASystemKey_WithNoHeader_HasNoTeam()
    {
        using var host = await StartHostAsync();

        var (status, team) = await CallAsync(host, SystemKeyValue);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("(none)", team);
    }
}
