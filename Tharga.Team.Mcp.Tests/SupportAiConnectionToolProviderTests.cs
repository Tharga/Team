using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Mcp;
using Tharga.Team.Mcp;
using Tharga.Team;

namespace Tharga.Team.Mcp.Tests;

/// <summary>
/// A team's support AI connection over MCP: what an agent is shown, what each tool does for the caller's team, and that
/// the key goes in and never comes out.
/// </summary>
/// <remarks>
/// <b>Authorization is the service's.</b> These tests substitute it, so they prove the tools hand the caller's team and
/// the request to it and report what it says; that it refuses a caller without <c>assistant:manage</c> is proven where the
/// service is tested.
/// </remarks>
public class SupportAiConnectionToolProviderTests
{
    private const string Team = "team-a";
    private const string SecretKey = "sk-very-secret-key-1234";

    private readonly ISupportAiConnectionService _service = Substitute.For<ISupportAiConnectionService>();

    private SupportAiConnectionToolProvider Provider(bool withService = true) => new(withService ? _service : null);

    private static TeamMcpContext Context(string teamId = Team) => TestMcpContextFactory.Create(teamId: teamId, scope: McpScope.Team);

    private static JsonElement Args(object arguments) => JsonSerializer.SerializeToElement(arguments);

    private static JsonElement NoArgs => JsonDocument.Parse("{}").RootElement.Clone();

    private static string Text(McpToolResult result) => string.Concat(result.Content.Select(x => x.Text));

    [Fact]
    public void ItIsATeamScopeProvider()
    {
        Assert.Equal(McpScope.Team, Provider().Scope);
    }

    [Fact]
    public async Task ACallerWithATeam_IsOfferedTheSevenTools()
    {
        var tools = await Provider().ListToolsAsync(Context(), TestContext.Current.CancellationToken);

        Assert.Equal(
        [
            SupportAiConnectionToolProvider.GetConnection, SupportAiConnectionToolProvider.GetChoices, SupportAiConnectionToolProvider.ListModels,
            SupportAiConnectionToolProvider.Test, SupportAiConnectionToolProvider.Connect, SupportAiConnectionToolProvider.UsePlatformModel,
            SupportAiConnectionToolProvider.TurnOff
        ], tools.Select(x => x.Name));
        Assert.All(tools, x => Assert.False(string.IsNullOrWhiteSpace(x.Description)));
    }

    [Fact]
    public async Task ACallerWithNoTeam_IsOfferedNothing()
    {
        var tools = await Provider().ListToolsAsync(Context(teamId: null), TestContext.Current.CancellationToken);

        Assert.Empty(tools);
    }

    [Fact]
    public async Task WithNoServiceRegistered_NothingIsOffered()
    {
        var tools = await Provider(withService: false).ListToolsAsync(Context(), TestContext.Current.CancellationToken);

        Assert.Empty(tools);
    }

    [Fact]
    public async Task EveryToolThatTakesAProvider_SaysSoInItsSchema_AndTheKeyIsMarkedWriteOnly()
    {
        var tools = await Provider().ListToolsAsync(Context(), TestContext.Current.CancellationToken);

        foreach (var name in new[] { SupportAiConnectionToolProvider.Connect, SupportAiConnectionToolProvider.Test, SupportAiConnectionToolProvider.ListModels })
        {
            var schema = tools.Single(x => x.Name == name).InputSchema.ToString();
            Assert.Contains("kind", schema);
            Assert.Contains("apiKey", schema);
            Assert.Contains("never returned", schema);
        }
    }

    [Fact]
    public async Task TheConnection_IsReadForTheCallersTeam()
    {
        _service.GetConnectionAsync(Team, Arg.Any<CancellationToken>()).Returns(new SupportAiConnection
        {
            Kind = SupportAiConnectionKind.Neurolito,
            BaseAddress = "https://neurolito.example",
            Model = "claude-opus-5-5",
            HasApiKey = true,
            ChangedBy = "Daniel"
        });

        var result = await Provider().CallToolAsync(SupportAiConnectionToolProvider.GetConnection, NoArgs, Context(), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        using var json = JsonDocument.Parse(Text(result));
        Assert.Equal("Neurolito", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("claude-opus-5-5", json.RootElement.GetProperty("model").GetString());
        Assert.True(json.RootElement.GetProperty("hasApiKey").GetBoolean());
        Assert.DoesNotContain("apiKey\"", Text(result));
    }

    [Fact]
    public async Task TheChoices_AreReadForTheCallersTeam()
    {
        _service.GetChoicesAsync(Team, Arg.Any<CancellationToken>()).Returns(new SupportAiConnectionChoices
        {
            Kinds = [SupportAiConnectionKind.Platform, SupportAiConnectionKind.Neurolito, SupportAiConnectionKind.Off],
            ProviderDefaults = [new SupportAiProviderDefault(SupportAiConnectionKind.Neurolito, "https://neurolito.example")],
            PlatformModel = "ollama, llama3"
        });

        var result = await Provider().CallToolAsync(SupportAiConnectionToolProvider.GetChoices, NoArgs, Context(), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("\"Neurolito\"", Text(result));
        Assert.Contains("ollama, llama3", Text(result));
    }

    [Fact]
    public async Task ConnectingPassesTheRequestAndTheCallersTeamToTheService()
    {
        _service.ConnectAsync(Team, Arg.Any<SupportAiProviderRequest>(), Arg.Any<CancellationToken>()).Returns(SupportAiConnectionResult.Success());

        var result = await Provider().CallToolAsync(SupportAiConnectionToolProvider.Connect,
            Args(new { kind = "Neurolito", baseAddress = "https://neurolito.example", apiKey = SecretKey, model = "claude-opus-5-5" }),
            Context(), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        await _service.Received(1).ConnectAsync(Team,
            Arg.Is<SupportAiProviderRequest>(x => x.Kind == SupportAiConnectionKind.Neurolito && x.BaseAddress == "https://neurolito.example"
                && x.ApiKey == SecretKey && x.Model == "claude-opus-5-5"),
            Arg.Any<CancellationToken>());
        Assert.DoesNotContain(SecretKey, Text(result));
    }

    [Fact]
    public async Task ConnectingWithNoKey_UsesTheStoredOne_ByPassingNull()
    {
        _service.ConnectAsync(Team, Arg.Any<SupportAiProviderRequest>(), Arg.Any<CancellationToken>()).Returns(SupportAiConnectionResult.Success());

        await Provider().CallToolAsync(SupportAiConnectionToolProvider.Connect, Args(new { kind = "Anthropic", model = "claude-haiku-5-5" }), Context(), TestContext.Current.CancellationToken);

        await _service.Received(1).ConnectAsync(Team, Arg.Is<SupportAiProviderRequest>(x => x.ApiKey == null && x.BaseAddress == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AConnectionThatDidNotWork_IsAnErrorResult_WithWhyInWords()
    {
        _service.ConnectAsync(Team, Arg.Any<SupportAiProviderRequest>(), Arg.Any<CancellationToken>()).Returns(SupportAiConnectionResult.Failure("The key was refused."));

        var result = await Provider().CallToolAsync(SupportAiConnectionToolProvider.Connect,
            Args(new { kind = "Neurolito", apiKey = SecretKey, model = "m" }), Context(), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("The key was refused.", Text(result));
        Assert.DoesNotContain(SecretKey, Text(result));
    }

    [Fact]
    public async Task Testing_PassesTheRequest_AndReturnsTheModelsReply()
    {
        _service.TestAsync(Team, Arg.Any<SupportAiProviderRequest>(), Arg.Any<CancellationToken>()).Returns(SupportAiConnectionResult.Success("Hello!"));

        var result = await Provider().CallToolAsync(SupportAiConnectionToolProvider.Test,
            Args(new { kind = "Ollama", baseAddress = "http://localhost:11434", model = "llama3" }), Context(), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("Hello!", Text(result));
        await _service.Received(1).TestAsync(Team, Arg.Is<SupportAiProviderRequest>(x => x.Kind == SupportAiConnectionKind.Ollama && x.Model == "llama3"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListingModels_ReturnsThem()
    {
        _service.ListModelsAsync(Team, Arg.Any<SupportAiProviderRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SupportAiModelList { Models = [new SupportAiModel("claude-opus-5-5", "Claude Opus 5.5")] });

        var result = await Provider().CallToolAsync(SupportAiConnectionToolProvider.ListModels,
            Args(new { kind = "Neurolito", apiKey = SecretKey }), Context(), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("claude-opus-5-5", Text(result));
        Assert.DoesNotContain(SecretKey, Text(result));
    }

    [Fact]
    public async Task ModelsThatCouldNotBeListed_AreAnErrorResult()
    {
        _service.ListModelsAsync(Team, Arg.Any<SupportAiProviderRequest>(), Arg.Any<CancellationToken>()).Returns(new SupportAiModelList { Problem = "The address could not be reached." });

        var result = await Provider().CallToolAsync(SupportAiConnectionToolProvider.ListModels, Args(new { kind = "Ollama" }), Context(), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("could not be reached", Text(result));
    }

    [Fact]
    public async Task UsingThePlatformModel_ForTheCallersTeam()
    {
        var result = await Provider().CallToolAsync(SupportAiConnectionToolProvider.UsePlatformModel, NoArgs, Context(), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        await _service.Received(1).UsePlatformModelAsync(Team, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASiteWithNoPlatformModel_SaysSo_InsteadOfFailingTheCall()
    {
        _service.UsePlatformModelAsync(Team, Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("none")));

        var result = await Provider().CallToolAsync(SupportAiConnectionToolProvider.UsePlatformModel, NoArgs, Context(), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("no platform model", Text(result));
    }

    [Fact]
    public async Task TurningTheAssistantOff_ForTheCallersTeam()
    {
        var result = await Provider().CallToolAsync(SupportAiConnectionToolProvider.TurnOff, NoArgs, Context(), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        await _service.Received(1).TurnOffAsync(Team, Arg.Any<CancellationToken>());
    }

    /// <summary>The service refuses a caller without <c>assistant:manage</c>; an agent is told that in words, not given a transport error.</summary>
    [Fact]
    public async Task ARefusalByTheService_IsAnErrorResult_NamingTheScope()
    {
        _service.TurnOffAsync(Team, Arg.Any<CancellationToken>()).Returns(Task.FromException(new UnauthorizedAccessException()));

        var result = await Provider().CallToolAsync(SupportAiConnectionToolProvider.TurnOff, NoArgs, Context(), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("assistant:manage", Text(result));
    }

    [Fact]
    public async Task ACallerWithNoTeam_IsRefused_AndTheServiceIsNeverCalled()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Provider().CallToolAsync(SupportAiConnectionToolProvider.TurnOff, NoArgs, Context(teamId: null), TestContext.Current.CancellationToken));

        await _service.DidNotReceive().TurnOffAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AToolThatDoesNotExist_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Provider().CallToolAsync("support.ai.nope", NoArgs, Context(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"kind\":\"\"}")]
    [InlineData("{\"kind\":\"Nonsense\"}")]
    [InlineData("{\"kind\":\"99\"}")]
    [InlineData("{\"kind\":7}")]
    public async Task ARequestWithNoUsableKind_IsAnErrorResult_AndTheServiceIsNeverCalled(string arguments)
    {
        var result = await Provider().CallToolAsync(SupportAiConnectionToolProvider.Connect, JsonDocument.Parse(arguments).RootElement.Clone(), Context(), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("kind", Text(result));
        await _service.DidNotReceive().ConnectAsync(Arg.Any<string>(), Arg.Any<SupportAiProviderRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Whatever goes wrong, what an agent reads back never quotes what it sent, because that holds the key.</summary>
    [Fact]
    public async Task AnErrorNeverQuotesTheArguments()
    {
        var result = await Provider().CallToolAsync(SupportAiConnectionToolProvider.Connect,
            Args(new { kind = "Nonsense", apiKey = SecretKey }), Context(), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.DoesNotContain(SecretKey, Text(result));
    }

    [Fact]
    public void ItInjectsNothingInternal_OnlyTheCheckedService()
    {
        var parameters = typeof(SupportAiConnectionToolProvider).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToArray();

        Assert.Equal([typeof(ISupportAiConnectionService)], parameters);
    }

    [Fact]
    public void TheToolsAreNotRegistered_UnlessTheHostAsksForThem()
    {
        var services = new ServiceCollection();
        services.AddThargaMcp(mcp => mcp.AddTeam());

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(SupportAiConnectionToolProvider));
    }

    [Fact]
    public async Task ResolvedFromTheContainer_ItOffersTheTools_WhenTheServiceIsRegistered_AndNothingWhenItIsNot()
    {
        var with = new ServiceCollection();
        with.AddSingleton(_service);
        with.AddThargaMcp(mcp => mcp.AddTeam(o => o.ExposeSupportAiConnection = true));
        using var withProvider = with.BuildServiceProvider();

        var without = new ServiceCollection();
        without.AddThargaMcp(mcp => mcp.AddTeam(o => o.ExposeSupportAiConnection = true));
        using var withoutProvider = without.BuildServiceProvider();

        Assert.Equal(7, (await withProvider.GetRequiredService<SupportAiConnectionToolProvider>().ListToolsAsync(Context(), TestContext.Current.CancellationToken)).Count);
        Assert.Empty(await withoutProvider.GetRequiredService<SupportAiConnectionToolProvider>().ListToolsAsync(Context(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheToolsAreRegistered_WhenTheHostAsksForThem()
    {
        var services = new ServiceCollection();
        services.AddThargaMcp(mcp => mcp.AddTeam(o => o.ExposeSupportAiConnection = true));

        Assert.Contains(services, d => d.ServiceType == typeof(SupportAiConnectionToolProvider));
        Assert.Contains(services, d => d.ServiceType == typeof(IMcpToolProvider));
    }
}
