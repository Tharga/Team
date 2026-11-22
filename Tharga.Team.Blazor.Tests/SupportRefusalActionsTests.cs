using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What a team's administrators can do when the assistant has stopped answering: another model where the account behind this one is refused, and their own
/// subscription. A member sees none of it, and the choice is only ever made by a click.
/// </summary>
public class SupportRefusalActionsTests : BunitContext
{
    private const string TeamKey = "acme";

    private readonly Mock<ISupportAiConnectionService> _connections = new();

    private static readonly SupportAiPlatformModelChoice Big = new() { Id = "big", DisplayName = "Big model", IsDefault = true };
    private static readonly SupportAiPlatformModelChoice Small = new() { Id = "small", DisplayName = "Small model" };

    private static SupportAiConnectionChoices Choices(bool own = true, params SupportAiPlatformModelChoice[] models) => new()
    {
        Kinds = [SupportAiConnectionKind.Platform, .. own ? [SupportAiConnectionKind.Neurolito] : Array.Empty<SupportAiConnectionKind>(), SupportAiConnectionKind.Off],
        PlatformModels = models
    };

    private static SupportAssistantAllowance Allowance() => new()
    {
        TeamKey = TeamKey,
        Applies = true,
        Display = SupportAllowanceDisplay.Units,
        Unit = "WU",
        Limit = 2500,
        Used = 400,
        Models =
        [
            new SupportAssistantModelAllowance { ModelId = "big", DisplayName = "Big model", Current = true, CostPerAnswer = 500, QuestionsLeft = 4 },
            new SupportAssistantModelAllowance { ModelId = "small", DisplayName = "Small model", CostPerAnswer = 50, QuestionsLeft = 42 }
        ]
    };

    private IRenderedComponent<SupportRefusalActions> Render(
        SupportAssistantReadiness readiness,
        SupportAiConnectionChoices choices = null,
        SupportAiConnection connection = null,
        bool permitted = true,
        bool registered = true,
        SupportAssistantAllowance allowance = null,
        Action changed = null,
        bool supportOffered = true)
    {
        if (permitted)
        {
            _connections.Setup(x => x.GetChoicesAsync(TeamKey, default)).ReturnsAsync(choices ?? Choices(true, Big, Small));
            _connections.Setup(x => x.GetConnectionAsync(TeamKey, default)).ReturnsAsync(connection ?? new SupportAiConnection());
        }
        else
        {
            _connections.Setup(x => x.GetChoicesAsync(TeamKey, default)).ThrowsAsync(new UnauthorizedAccessException());
        }

        if (registered) Services.AddSingleton(_connections.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportRefusalActions>(p => p
            .Add(x => x.TeamKey, TeamKey)
            .Add(x => x.Readiness, readiness)
            .Add(x => x.Allowance, allowance)
            .Add(x => x.SupportOffered, supportOffered)
            .Add(x => x.Changed, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => changed?.Invoke())));
    }

    [Fact]
    public void AMember_SeesNothing()
    {
        var view = Render(SupportAssistantReadiness.ProviderRefused, permitted: false);

        Assert.Empty(view.FindAll("[data-refusal-actions]"));
    }

    /// <summary>With nobody to write to, a member is told whom to ask; an administrator is the one who is asked, so is not told to ask.</summary>
    [Fact]
    public void AMemberOfATeamWithoutSupport_IsToldToAskAnAdministrator()
    {
        var view = Render(SupportAssistantReadiness.AllowanceUsed, permitted: false, supportOffered: false);

        Assert.Equal(SupportRefusalActionsText.AskAdministrator.Default, view.Find("[data-ask-administrator]").TextContent.Trim());
    }

    [Fact]
    public void AMemberOfATeamWithSupport_IsNotToldToAskAnAdministrator_SupportIsTheWayOn()
    {
        var view = Render(SupportAssistantReadiness.AllowanceUsed, permitted: false, supportOffered: true);

        Assert.Empty(view.FindAll("[data-ask-administrator]"));
    }

    [Fact]
    public void AnAdministratorWithoutSupport_IsNotToldToAskAnAdministrator()
    {
        var view = Render(SupportAssistantReadiness.AllowanceUsed, supportOffered: false);

        Assert.Empty(view.FindAll("[data-ask-administrator]"));
        Assert.NotNull(view.Find("[data-use-own]"));
    }

    /// <summary>Without the service there is no telling a member from an administrator, so nobody is pointed at somebody who may not be able to help.</summary>
    [Fact]
    public void WithoutAConnectionService_NobodyIsToldToAsk()
    {
        var view = Render(SupportAssistantReadiness.AllowanceUsed, registered: false, supportOffered: false);

        Assert.Empty(view.FindAll("[data-ask-administrator]"));
    }

    [Fact]
    public void WithoutAConnectionService_NothingIsOffered()
    {
        var view = Render(SupportAssistantReadiness.ProviderRefused, registered: false);

        Assert.Empty(view.FindAll("[data-refusal-actions]"));
    }

    /// <summary>The model in use is not offered back: the other models are, each with what an answer costs and how many questions that leaves.</summary>
    [Fact]
    public void WhereTheAccountWasRefused_TheOtherModels_AreOffered_WithWhatTheyCost()
    {
        var view = Render(SupportAssistantReadiness.ProviderRefused, allowance: Allowance());

        Assert.Empty(view.FindAll("[data-switch-model='big']"));
        Assert.Contains("Small model", view.Find("[data-switch-model='small']").TextContent);
        Assert.Contains("50 WU", view.Find("[data-model-detail='small']").TextContent);
        Assert.Contains("42", view.Find("[data-model-detail='small']").TextContent);
    }

    [Fact]
    public void AModelTheTeamChose_IsTheOneNotOfferedBack()
    {
        var view = Render(SupportAssistantReadiness.ProviderRefused,
            connection: new SupportAiConnection { Kind = SupportAiConnectionKind.Platform, PlatformModelId = "small" });

        Assert.Empty(view.FindAll("[data-switch-model='small']"));
        Assert.NotNull(view.Find("[data-switch-model='big']"));
    }

    [Fact]
    public void WithoutAllowanceAmounts_ModelsAreOffered_WithoutACost()
    {
        var view = Render(SupportAssistantReadiness.ProviderRefused);

        Assert.NotNull(view.Find("[data-switch-model='small']"));
        Assert.Empty(view.FindAll("[data-model-detail]"));
    }

    /// <summary>An allowance is used up on any model, so choosing another is no way out of it; the team's own subscription, which nothing limits, is.</summary>
    [Fact]
    public void WhereTheAllowanceIsUsedUp_NoOtherModelIsOffered_ButTheTeamsOwnSubscriptionIs()
    {
        var view = Render(SupportAssistantReadiness.AllowanceUsed, allowance: Allowance());

        Assert.Empty(view.FindAll("[data-switch-model]"));
        Assert.NotNull(view.Find("[data-use-own]"));
    }

    [Fact]
    public void ATeamOnItsOwnProvider_IsNotOfferedThePlatformsModels()
    {
        var view = Render(SupportAssistantReadiness.ProviderRefused, connection: new SupportAiConnection { Kind = SupportAiConnectionKind.Neurolito });

        Assert.Empty(view.FindAll("[data-switch-model]"));
    }

    [Fact]
    public void WhenTheHostOffersNoProvider_ThereIsNoOwnSubscriptionToConnect()
    {
        var view = Render(SupportAssistantReadiness.AllowanceUsed, choices: Choices(false, Big, Small));

        Assert.Empty(view.FindAll("[data-use-own]"));
    }

    [Fact]
    public void ClickingAModel_ChoosesIt_AndTellsWhoShowsTheAssistant()
    {
        var changed = 0;
        var view = Render(SupportAssistantReadiness.ProviderRefused, changed: () => changed++);

        view.Find("[data-switch-model='small']").Click();

        _connections.Verify(x => x.UsePlatformModelAsync(TeamKey, "small", default), Times.Once);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void AChoiceTheServiceRefuses_SaysSo_AndChangesNothing()
    {
        var changed = 0;
        _connections.Setup(x => x.UsePlatformModelAsync(TeamKey, "small", default)).ThrowsAsync(new InvalidOperationException("That model is not offered."));
        var view = Render(SupportAssistantReadiness.ProviderRefused, changed: () => changed++);

        view.Find("[data-switch-model='small']").Click();

        Assert.NotNull(view.Find("[data-refusal-failed]"));
        Assert.Equal(0, changed);
    }

    /// <summary>The key is given where it always is, in the connection view, in a dialog, so nothing about it is shown or sent from here.</summary>
    [Fact]
    public void TheOwnSubscription_IsConnectedInTheConnectionView_InADialog()
    {
        var opened = new List<(string Title, Type Type, IDictionary<string, object> Parameters)>();
        var view = Render(SupportAssistantReadiness.AllowanceUsed);
        Services.GetRequiredService<DialogService>().OnOpen += (title, type, parameters, _) => opened.Add((title, type, parameters));

        view.Find("[data-use-own]").Click();

        var dialog = Assert.Single(opened);
        Assert.Equal(typeof(SupportAssistantConnectionView), dialog.Type);
        Assert.Equal(TeamKey, dialog.Parameters[nameof(SupportAssistantConnectionView.TeamKey)]);
    }
}
