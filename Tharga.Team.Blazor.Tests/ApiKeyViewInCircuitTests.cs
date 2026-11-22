using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.Api;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Ticking Personal on <c>ApiKeyView</c> in an interactive circuit creates a key owned by the caller (Tharga/Team#550).
/// </summary>
/// <remarks>
/// <b>Rendered with the real management service and the real circuit principal accessor, and no request.</b> <c>IHttpContextAccessor.HttpContext</c> is null, as
/// it is for a circuit's calls, so the caller can only come from the circuit's authentication state, which carries the member key for the selected team. A test
/// that fakes the management service, or the request, cannot see the defect this guards: Create failed with "the caller has no team-member context" for
/// every personal key, while every test was green.
/// </remarks>
public class ApiKeyViewInCircuitTests : BunitContext
{
    private const string Team = "team-1";
    private const string MemberKey = "member-1";

    private readonly Mock<IApiKeyAdministrationService> _store = new();

    private IRenderedComponent<ApiKeyView> RenderInACircuit(string memberKey = MemberKey)
    {
        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(Team);
        team.SetupGet(x => x.Name).Returns("Acme");
        var teamState = new Mock<ITeamStateService>();
        teamState.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(team.Object);

        List<Claim> claims =
        [
            new(ClaimTypes.Name, "alice"),
            new(TeamClaimTypes.TeamKey, Team),
            new(TeamClaimTypes.AccessLevel, nameof(AccessLevel.Administrator)),
            new(TeamClaimTypes.Scope, ApiKeyScopes.Manage)
        ];
        if (memberKey != null) claims.Add(new Claim(TeamClaimTypes.MemberKey, memberKey));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "circuit"));

        var auth = new Mock<AuthenticationStateProvider>();
        auth.Setup(x => x.GetAuthenticationStateAsync()).ReturnsAsync(new AuthenticationState(principal));

        _store.Setup(x => x.GetKeysAsync(Team)).Returns(Empty);
        _store.Setup(x => x.CreateKeyAsync(Team, It.IsAny<string>(), It.IsAny<AccessLevel>(), It.IsAny<string[]>(), It.IsAny<string[]>(), It.IsAny<DateTime?>(),
                It.IsAny<IReadOnlyList<Tag>>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Mock.Of<IApiKey>());

        // No request: this is what a circuit's calls see.
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(x => x.HttpContext).Returns((HttpContext)null);

        var options = Options.Create(new ApiKeyOptions { AdvancedMode = true });
        var principalAccessor = new BlazorTeamPrincipalAccessor(http.Object, auth.Object);

        Services.AddSingleton(teamState.Object);
        Services.AddSingleton(auth.Object);
        Services.AddSingleton<IApiKeyManagementService>(new ApiKeyManagementService(_store.Object, http.Object, options, principalAccessor: principalAccessor));
        Services.AddSingleton(Options.Create(new ThargaBlazorOptions()));
        Services.AddSingleton(options);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));

        Render<RadzenDialog>();
        Render<RadzenNotification>();
        return Render<ApiKeyView>();
    }

    private static async IAsyncEnumerable<IApiKey> Empty()
    {
        await Task.CompletedTask;
        yield break;
    }

    private async Task CreateAsync(IRenderedComponent<ApiKeyView> view, bool personal)
    {
        view.WaitForAssertion(() => Assert.True(view.FindAll("input[placeholder]").Any(), view.Markup));
        view.Find("input[placeholder]").Input("Evaluation");
        if (personal)
            await view.InvokeAsync(() => view.FindComponent<RadzenCheckBox<bool>>().Instance.ValueChanged.InvokeAsync(true));

        await view.InvokeAsync(() => view.FindAll("button").First(x => x.TextContent.Contains("Create", StringComparison.OrdinalIgnoreCase)).Click());
    }

    [Fact]
    public async Task APersonalKey_IsCreated_OwnedByTheCallersMemberKey()
    {
        var view = RenderInACircuit();

        await CreateAsync(view, personal: true);

        view.WaitForAssertion(() => _store.Verify(x => x.CreateKeyAsync(Team, "Evaluation", It.IsAny<AccessLevel>(), It.IsAny<string[]>(), It.IsAny<string[]>(),
            It.IsAny<DateTime?>(), It.IsAny<IReadOnlyList<Tag>>(), "alice", MemberKey), Times.Once));
    }

    /// <summary>A shared key from the same form never needed an owner, and still gets none.</summary>
    [Fact]
    public async Task ASharedKey_IsStillCreated_WithNoOwner()
    {
        var view = RenderInACircuit();

        await CreateAsync(view, personal: false);

        view.WaitForAssertion(() => _store.Verify(x => x.CreateKeyAsync(Team, "Evaluation", It.IsAny<AccessLevel>(), It.IsAny<string[]>(), It.IsAny<string[]>(),
            It.IsAny<DateTime?>(), It.IsAny<IReadOnlyList<Tag>>(), "alice", null), Times.Once));
    }

    /// <summary>When the circuit carries no member key there is nobody to own it, and nothing is stored.</summary>
    [Fact]
    public async Task WithNoMemberKeyInTheCircuit_NothingIsStored()
    {
        var view = RenderInACircuit(memberKey: null);

        await CreateAsync(view, personal: true);

        _store.Verify(x => x.CreateKeyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AccessLevel>(), It.IsAny<string[]>(), It.IsAny<string[]>(),
            It.IsAny<DateTime?>(), It.IsAny<IReadOnlyList<Tag>>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
