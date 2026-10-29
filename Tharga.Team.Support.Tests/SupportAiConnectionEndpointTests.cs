using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The AI connection over HTTP, through the real service and its authorization: each route does what its method does,
/// for the team the credential is bound to, and no response carries the key.
/// </summary>
public sealed class SupportAiConnectionEndpointTests : IAsyncLifetime
{
    private const string Path = SupportAiConnectionEndpointRegistration.DefaultPath;
    private const string TeamA = "team-a";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly SupportAiConnectionServiceTests.FakeFactory _factory = new();
    private readonly SupportAiConnectionServiceTests.InMemoryConnectionStore _store = new();
    private WebApplication _app;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddAuthentication(TestCaller.Scheme).AddScheme<AuthenticationSchemeOptions, TestCaller>(TestCaller.Scheme, _ => { });
        builder.Services.AddAuthorizationBuilder().AddPolicy(ApiKeyConstants.ThargaApiPolicyName, x => x.RequireAuthenticatedUser());
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ITeamPrincipalAccessor, HttpContextTeamPrincipalAccessor>();
        builder.Services.AddSingleton<TeamAuthorizer>();
        builder.Services.AddSingleton<ISupportAiConnectionStore>(_store);
        builder.Services.AddSingleton(Substitute.For<ISupportCaseStore>());
        builder.Services.Configure<AuditOptions>(_ => { });
        builder.Services.AddSingleton<CompositeAuditLogger>();
        builder.Services.AddSingleton<IAuditEntryFactory>(new AuditEntryFactory(null));
        builder.Services.AddThargaSupportCases();
        builder.Services.AddThargaSupportAiConnections();
        builder.Services.AddSingleton<ISupportChatClientFactory>(_factory);
        builder.Services.AddSingleton(Substitute.For<IChatClient>());

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapThargaSupportAiConnection();

        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private HttpClient Caller(string teamKey = TeamA, bool withScope = true)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add(TestCaller.Header, "admin");
        if (teamKey != null) client.DefaultRequestHeaders.Add(TestCaller.TeamHeader, teamKey);
        if (withScope) client.DefaultRequestHeaders.Add(TestCaller.ScopeHeader, SupportScopes.AssistantManage);
        return client;
    }

    private static SupportAiProviderRequest Neurolito(string apiKey = SupportAiConnectionServiceTests.GoodKey)
        => new() { Kind = SupportAiConnectionKind.Neurolito, ApiKey = apiKey, Model = SupportAiConnectionServiceTests.Model };

    private static StringContent Body(SupportAiProviderRequest request) => new(JsonSerializer.Serialize(request, Json), System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task TheChoices_AreWhatTheTeamMayChoose_WithKindsByName()
    {
        var response = await Caller().GetAsync($"{Path}/choices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"Neurolito\"", text);
        var choices = JsonSerializer.Deserialize<SupportAiConnectionChoices>(text, Json);
        Assert.Equal([SupportAiConnectionKind.Platform, SupportAiConnectionKind.Neurolito, SupportAiConnectionKind.Off], choices.Kinds);
    }

    [Fact]
    public async Task Connecting_SavesTheProvider_AndTheConnectionAsRead_NeverShowsTheKey()
    {
        var client = Caller();

        var connected = await client.PutAsync(Path, Body(Neurolito()));
        var read = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.OK, connected.StatusCode);
        Assert.True((await connected.Content.ReadFromJsonAsync<SupportAiConnectionResult>(Json)).Succeeded);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var text = await read.Content.ReadAsStringAsync();
        Assert.DoesNotContain(SupportAiConnectionServiceTests.GoodKey, text);
        var connection = JsonSerializer.Deserialize<SupportAiConnection>(text, Json);
        Assert.Equal(SupportAiConnectionKind.Neurolito, connection.Kind);
        Assert.True(connection.HasApiKey);
        Assert.Equal(SupportAiConnectionKind.Neurolito, (await _store.GetAsync(TeamA)).Kind);
    }

    /// <summary>A refused connection is 422 with why, and the key is not in the answer even where the provider echoed it.</summary>
    [Fact]
    public async Task ARefusedConnection_Is422_WithWhy_AndWithoutTheKey()
    {
        var response = await Caller().PutAsync(Path, Body(Neurolito(apiKey: "wrong-key-9876543210")));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("wrong-key-9876543210", text);
        Assert.Equal(SupportAiConnectionServiceTests.FakeFactory.Refused, JsonSerializer.Deserialize<SupportAiConnectionResult>(text, Json).Problem);
        Assert.Null(await _store.GetAsync(TeamA));
    }

    [Fact]
    public async Task TheModels_AreListed()
    {
        var response = await Caller().PostAsync($"{Path}/models", Body(Neurolito() with { Model = null }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<SupportAiModelList>(Json);
        Assert.Contains(list.Models, x => x.Id == SupportAiConnectionServiceTests.Model);
    }

    [Fact]
    public async Task ATest_ReturnsWhatTheModelSaid_AndSavesNothing()
    {
        var response = await Caller().PostAsync($"{Path}/test", Body(Neurolito()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SupportAiConnectionServiceTests.FakeFactory.Reply, (await response.Content.ReadFromJsonAsync<SupportAiConnectionResult>(Json)).Reply);
        Assert.Null(await _store.GetAsync(TeamA));
    }

    [Fact]
    public async Task ThePlatformsModel_AndOff_AreChosen()
    {
        var client = Caller();

        var off = await client.PutAsync($"{Path}/off", null);
        var offStored = (await _store.GetAsync(TeamA)).Kind;
        var platform = await client.PutAsync($"{Path}/platform", null);

        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        Assert.Equal(SupportAiConnectionKind.Off, offStored);
        Assert.Equal(HttpStatusCode.NoContent, platform.StatusCode);
        Assert.Equal(SupportAiConnectionKind.Platform, (await _store.GetAsync(TeamA)).Kind);
    }

    /// <summary>The service's own check: a caller without <c>assistant:manage</c> on the team is refused every route.</summary>
    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/choices")]
    [InlineData("POST", "/models")]
    [InlineData("POST", "/test")]
    [InlineData("PUT", "")]
    [InlineData("PUT", "/platform")]
    [InlineData("PUT", "/off")]
    public async Task ACallerWithoutTheScope_IsRefused(string method, string route)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), $"{Path}{route}") { Content = method == "GET" ? null : Body(Neurolito()) };

        var response = await Caller(withScope: false).SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await _store.GetAsync(TeamA));
        Assert.Empty(_factory.Seen);
    }

    /// <summary>Which team is the credential's to say, never the request's: with none bound, there is nothing to act on.</summary>
    [Fact]
    public async Task ACallerBoundToNoTeam_IsABadRequest()
    {
        var response = await Caller(teamKey: null).GetAsync(Path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnAnonymousCaller_IsUnauthorized()
    {
        var response = await _app.GetTestClient().GetAsync(Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ABodyThatIsNotARequest_IsABadRequest()
    {
        var response = await Caller().PutAsync(Path, new StringContent("not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Seen);
    }

    /// <summary>Signs the caller in from test headers: who, the team it is bound to, and its scope.</summary>
    private sealed class TestCaller(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "test";
        public const string Header = "X-Test-Caller";
        public const string TeamHeader = "X-Test-Team";
        public const string ScopeHeader = "X-Test-Scope";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(Header, out var caller)) return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, caller.ToString()), new(ClaimTypes.Name, "Ada Admin") };
            if (Request.Headers.TryGetValue(TeamHeader, out var team)) claims.Add(new Claim(TeamClaimTypes.TeamKey, team.ToString()));
            if (Request.Headers.TryGetValue(ScopeHeader, out var scope)) claims.Add(new Claim(TeamClaimTypes.Scope, scope.ToString()));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
        }
    }
}
