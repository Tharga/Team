using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Choosing what the support assistant answers a team with: the current choice, only what the host offers, loading a
/// provider's models, testing, saving, and a key that is sent and never shown again.
/// </summary>
public class SupportAssistantConnectionViewTests : BunitContext
{
    private const string TeamKey = "acme";
    private const string Key = "nl-team-key-0123456789";
    private const string Model = "claude-qwen3-coder:30b";
    private const string Address = "https://neurolito.com";

    private readonly Mock<ISupportAiConnectionService> _connections = new();

    private static readonly SupportAiConnectionChoices AllChoices = new()
    {
        Kinds = [SupportAiConnectionKind.Platform, SupportAiConnectionKind.Neurolito, SupportAiConnectionKind.Off],
        ProviderDefaults = [new SupportAiProviderDefault(SupportAiConnectionKind.Neurolito, Address)]
    };

    private IRenderedComponent<SupportAssistantConnectionView> Render(SupportAiConnection connection = null, SupportAiConnectionChoices choices = null, bool withTeam = true)
    {
        _connections.Setup(x => x.GetChoicesAsync(TeamKey, default)).ReturnsAsync(choices ?? AllChoices);
        _connections.Setup(x => x.GetConnectionAsync(TeamKey, default)).ReturnsAsync(connection ?? new SupportAiConnection());

        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(TeamKey);
        var teamState = new Mock<ITeamStateService>();
        teamState.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(withTeam ? team.Object : null);

        Services.AddSingleton(_connections.Object);
        Services.AddSingleton(teamState.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));

        return Render<SupportAssistantConnectionView>();
    }

    private static IRenderedComponent<RadzenRadioButtonList<SupportAiConnectionKind>> Kinds(IRenderedComponent<SupportAssistantConnectionView> view)
        => view.FindComponent<RadzenRadioButtonList<SupportAiConnectionKind>>();

    private static async Task ChooseAsync(IRenderedComponent<SupportAssistantConnectionView> view, SupportAiConnectionKind kind)
    {
        var kinds = Kinds(view);
        await view.InvokeAsync(() => kinds.Instance.Change.InvokeAsync(kind));
    }

    private static SupportAiConnection Neurolito() => new()
    {
        Kind = SupportAiConnectionKind.Neurolito,
        BaseAddress = Address,
        Model = Model,
        HasApiKey = true,
        ChangedAt = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc),
        ChangedBy = "Ada Admin"
    };

    [Fact]
    public void TheCurrentChoice_IsShown_WithWhoMadeIt()
    {
        var view = Render(Neurolito());

        Assert.Equal(SupportAiConnectionKind.Neurolito, Kinds(view).Instance.Value);
        Assert.Equal(Address, view.Find("input[data-address]").GetAttribute("value"));
        Assert.Contains("Ada Admin", view.Find("[data-changed]").TextContent);
        Assert.Equal(SupportAssistantConnectionViewText.ApiKeyStored.Default, view.Find("[data-key-hint]").TextContent.Trim());
    }

    [Fact]
    public void ATeamThatNeverChose_IsOnThePlatformsModel()
    {
        var view = Render();

        Assert.Equal(SupportAiConnectionKind.Platform, Kinds(view).Instance.Value);
        Assert.Empty(view.FindAll("[data-changed]"));
        Assert.Empty(view.FindAll("input[data-key]"));
    }

    [Fact]
    public void OnlyWhatTheHostOffers_IsOffered()
    {
        var view = Render(choices: new SupportAiConnectionChoices { Kinds = [SupportAiConnectionKind.Platform, SupportAiConnectionKind.Off] });

        var offered = view.FindComponents<RadzenRadioButtonListItem<SupportAiConnectionKind>>().Select(x => x.Instance.Value);
        Assert.Equal([SupportAiConnectionKind.Platform, SupportAiConnectionKind.Off], offered);
    }

    [Fact]
    public async Task ChoosingAProvider_StartsFromItsAddress_WithNoKey()
    {
        var view = Render();

        await ChooseAsync(view, SupportAiConnectionKind.Neurolito);

        Assert.Equal(Address, view.Find("input[data-address]").GetAttribute("value"));
        Assert.Equal(SupportAssistantConnectionViewText.ApiKeyPlaceholder.Default, view.Find("[data-key-hint]").TextContent.Trim());
    }

    /// <summary>With both providers offered, each starts from its own address, not the other's.</summary>
    [Fact]
    public async Task ChoosingAnthropic_StartsFromAnthropicsAddress()
    {
        const string anthropicAddress = "https://api.anthropic.com";
        var view = Render(choices: new SupportAiConnectionChoices
        {
            Kinds = [SupportAiConnectionKind.Platform, SupportAiConnectionKind.Neurolito, SupportAiConnectionKind.Anthropic, SupportAiConnectionKind.Off],
            ProviderDefaults = [new SupportAiProviderDefault(SupportAiConnectionKind.Neurolito, Address), new SupportAiProviderDefault(SupportAiConnectionKind.Anthropic, anthropicAddress)]
        });

        await ChooseAsync(view, SupportAiConnectionKind.Anthropic);

        Assert.Equal(anthropicAddress, view.Find("input[data-address]").GetAttribute("value"));
    }

    [Fact]
    public async Task LoadingModels_SendsTheTypedKey_AndOffersWhatCameBack()
    {
        SupportAiProviderRequest sent = null;
        _connections.Setup(x => x.ListModelsAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default))
            .Callback<string, SupportAiProviderRequest, CancellationToken>((_, request, _) => sent = request)
            .ReturnsAsync(new SupportAiModelList { Models = [new SupportAiModel(Model, "Qwen3 Coder"), new SupportAiModel("claude-llama3:70b", "Llama 3")] });
        var view = Render();
        await ChooseAsync(view, SupportAiConnectionKind.Neurolito);
        view.Find("input[data-key]").Change(Key);

        view.Find("[data-load-models]").Click();

        Assert.Equal(SupportAiConnectionKind.Neurolito, sent.Kind);
        Assert.Equal(Key, sent.ApiKey);
        Assert.Equal(Address, sent.BaseAddress);
        view.WaitForAssertion(() => Assert.Equal(Model, view.FindComponent<RadzenDropDown<string>>().Instance.Value));
    }

    [Fact]
    public async Task AProblem_IsShownInWords()
    {
        _connections.Setup(x => x.ListModelsAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default))
            .ReturnsAsync(new SupportAiModelList { Problem = "The key was refused." });
        var view = Render();
        await ChooseAsync(view, SupportAiConnectionKind.Neurolito);

        view.Find("[data-load-models]").Click();

        view.WaitForAssertion(() => Assert.Contains("The key was refused.", view.Find("[data-problem]").TextContent));
    }

    [Fact]
    public void ATest_ShowsWhatTheModelSaid()
    {
        _connections.Setup(x => x.TestAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default)).ReturnsAsync(SupportAiConnectionResult.Success("I can answer."));
        var view = Render(Neurolito());

        view.Find("[data-test]").Click();

        view.WaitForAssertion(() => Assert.Contains("I can answer.", view.Find("[data-reply]").TextContent));
    }

    /// <summary>Kept out of the request when left as it is, so the service keeps the stored key; the typed one is never shown again after saving.</summary>
    [Fact]
    public void Saving_KeepsTheStoredKey_WhenNoneIsTyped_AndClearsATypedOne()
    {
        var requests = new List<SupportAiProviderRequest>();
        _connections.Setup(x => x.ConnectAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default))
            .Callback<string, SupportAiProviderRequest, CancellationToken>((_, request, _) => requests.Add(request))
            .ReturnsAsync(SupportAiConnectionResult.Success());
        var view = Render(Neurolito());

        view.Find("[data-save]").Click();
        view.WaitForAssertion(() => Assert.Single(requests));
        Assert.Null(requests[0].ApiKey);
        Assert.Equal(Model, requests[0].Model);

        view.Find("input[data-key]").Change(Key);
        view.Find("[data-save]").Click();

        view.WaitForAssertion(() => Assert.Equal(2, requests.Count));
        Assert.Equal(Key, requests[1].ApiKey);
        view.WaitForAssertion(() => Assert.Equal(SupportAssistantConnectionView.StoredKeyMask, view.FindComponent<RadzenPassword>().Instance.Value));
    }

    /// <summary>A stored key fills the field with a mask, so it does not look empty; the mask itself is never sent.</summary>
    [Fact]
    public void AStoredKey_ShowsAsAMask_ThatIsNeverSent()
    {
        SupportAiProviderRequest sent = null;
        _connections.Setup(x => x.ConnectAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default))
            .Callback<string, SupportAiProviderRequest, CancellationToken>((_, request, _) => sent = request)
            .ReturnsAsync(SupportAiConnectionResult.Success());
        var view = Render(Neurolito());

        Assert.Equal(SupportAssistantConnectionView.StoredKeyMask, view.Find("input[data-key]").GetAttribute("value"));

        view.Find("input[data-key]").Change(SupportAssistantConnectionView.StoredKeyMask);
        view.Find("[data-save]").Click();

        view.WaitForAssertion(() => Assert.NotNull(sent));
        Assert.Null(sent.ApiKey);
    }

    [Theory]
    [InlineData("••••••••••••" + Key)]
    [InlineData("••••" + Key + "••")]
    public void TypingIntoTheMask_SendsOnlyWhatWasTyped(string typed)
    {
        SupportAiProviderRequest sent = null;
        _connections.Setup(x => x.ConnectAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default))
            .Callback<string, SupportAiProviderRequest, CancellationToken>((_, request, _) => sent = request)
            .ReturnsAsync(SupportAiConnectionResult.Success());
        var view = Render(Neurolito());

        view.Find("input[data-key]").Change(typed);
        view.Find("[data-save]").Click();

        view.WaitForAssertion(() => Assert.Equal(Key, sent?.ApiKey));
    }

    /// <summary>A key is kept only for the address it was entered for, so another address starts with the field empty.</summary>
    [Fact]
    public void AnotherAddress_HasNoMask()
    {
        var view = Render(Neurolito());

        view.Find("input[data-address]").Change("https://elsewhere.example/");

        Assert.True(string.IsNullOrEmpty(view.Find("input[data-key]").GetAttribute("value")));
    }

    [Fact]
    public void ARefusedConnection_IsSaidSo_AndNothingIsReloaded()
    {
        _connections.Setup(x => x.ConnectAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default)).ReturnsAsync(SupportAiConnectionResult.Failure("The provider does not offer it."));
        var view = Render(Neurolito());

        view.Find("[data-save]").Click();

        view.WaitForAssertion(() => Assert.Contains("The provider does not offer it.", view.Find("[data-problem]").TextContent));
        _connections.Verify(x => x.GetConnectionAsync(TeamKey, default), Times.Once);
    }

    [Fact]
    public async Task ChoosingOff_AndSaving_TurnsTheAssistantOff()
    {
        var view = Render(Neurolito());
        await ChooseAsync(view, SupportAiConnectionKind.Off);

        view.Find("[data-save]").Click();

        view.WaitForAssertion(() => _connections.Verify(x => x.TurnOffAsync(TeamKey, default), Times.Once));
    }

    [Fact]
    public async Task ChoosingThePlatformsModel_AndSaving_UsesIt()
    {
        var view = Render(Neurolito());
        await ChooseAsync(view, SupportAiConnectionKind.Platform);

        view.Find("[data-save]").Click();

        view.WaitForAssertion(() => _connections.Verify(x => x.UsePlatformModelAsync(TeamKey, default), Times.Once));
    }

    [Fact]
    public void AReaderWithoutTheScope_IsToldSo()
    {
        _connections.Setup(x => x.GetChoicesAsync(TeamKey, default)).ThrowsAsync(new UnauthorizedAccessException());
        Services.AddSingleton(_connections.Object);
        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(TeamKey);
        var teamState = new Mock<ITeamStateService>();
        teamState.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(team.Object);
        Services.AddSingleton(teamState.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();

        var view = Render<SupportAssistantConnectionView>();

        Assert.Contains(SupportAssistantConnectionViewText.NotPermitted.Default, view.Markup);
    }

    [Fact]
    public void WithNoTeam_ItAsksForOne()
    {
        var view = Render(withTeam: false);

        Assert.Contains(SupportAssistantConnectionViewText.SelectTeam.Default, view.Markup);
    }
}
