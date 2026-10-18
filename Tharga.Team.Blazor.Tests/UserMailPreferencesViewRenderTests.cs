using System.Security.Claims;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>"Mail I receive": every category with its description, required ones always on with why, optional ones a switch that saves at once.</summary>
public class UserMailPreferencesViewRenderTests : BunitContext
{
    private const string Me = "user-1";

    private static readonly UserMailCategory News = new() { Key = "news", Name = "News", Description = "What is new in the product." };
    private static readonly UserMailCategory Digest = new() { Key = "digest", Name = "Weekly digest", OnByDefault = false };

    private readonly Mock<IUserMailPreferenceService> _preferences = new();
    private readonly Mock<IUserService> _users = new();

    private static IReadOnlyList<UserMailChoice> Choices(bool newsOn = true) =>
    [
        new() { Category = UserMailCategories.Account, Receives = true, IsDefault = true },
        new() { Category = UserMailCategories.Service, Receives = true, IsDefault = true },
        new() { Category = News, Receives = newsOn, IsDefault = newsOn },
        new() { Category = Digest, Receives = false, IsDefault = true }
    ];

    private IRenderedComponent<UserMailPreferencesView> Render(string userKey = null, IThargaTextProvider texts = null, IReadOnlyList<UserMailChoice> choices = null)
    {
        var user = new Mock<IUser>();
        user.SetupGet(x => x.Key).Returns(Me);
        _users.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user.Object);
        _preferences.Setup(x => x.GetChoicesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(choices ?? Choices());
        _preferences.Setup(x => x.SetChoiceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string category, bool receives, CancellationToken _) => new UserMailChoice { Category = category == News.Key ? News : Digest, Receives = receives, IsDefault = false });

        Services.AddSingleton(_preferences.Object);
        Services.AddSingleton(_users.Object);
        Services.AddSingleton(texts ?? new DefaultThargaTextProvider());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<UserMailPreferencesView>(p => p.Add(x => x.UserKey, userKey));
    }

    private static AngleSharp.Dom.IElement Switch(IRenderedComponent<UserMailPreferencesView> view, string category)
        => view.Find($"[data-category=\"{category}\"] .rz-switch");

    [Fact]
    public void EveryCategory_IsListed_WithItsDescription()
    {
        var view = Render();

        Assert.Contains("Mail I receive", view.Markup);
        Assert.Equal(["account", "service", "news", "digest"], view.FindAll("[data-category]").Select(x => x.GetAttribute("data-category")));
        Assert.Contains("Account and security", view.Markup);
        Assert.Contains("Service messages", view.Markup);
        Assert.Contains("What is new in the product.", view.Markup);
        Assert.Contains("Weekly digest", view.Markup);
    }

    [Fact]
    public void ARequiredCategory_IsShownAlwaysOn_WithADisabledSwitchAndWhy()
    {
        var view = Render();

        var account = Switch(view, "account");
        Assert.Contains("rz-switch-checked", account.ClassName);
        Assert.Contains("rz-state-disabled", account.ClassName);
        Assert.NotNull(view.Find("[data-required=\"account\"]"));
        Assert.Contains("Always sent: part of the terms of use", view.Find("[data-required=\"account\"]").TextContent);

        Assert.DoesNotContain("rz-state-disabled", Switch(view, "news").ClassName);
        Assert.Empty(view.FindAll("[data-required=\"news\"]"));
    }

    [Fact]
    public void WithoutTermsOfUse_TheAlwaysSentLine_IsNoLink()
    {
        var view = Render();

        Assert.Empty(view.FindAll("[data-required=\"account\"] a"));
    }

    [Fact]
    public void WithTermsOfUse_TheAlwaysSentLine_LinksToThem()
    {
        _preferences.Setup(x => x.GetTermsUrl()).Returns("/terms");

        var view = Render();

        var link = view.Find("[data-required=\"account\"] a");
        Assert.Equal("/terms", link.GetAttribute("href"));
        Assert.Equal("Always sent: part of the terms of use", link.TextContent);
    }

    [Fact]
    public void ServiceMessagesTheHostMadeOptional_AreASwitch_WithNoAlwaysSentLine()
    {
        var view = Render(choices:
        [
            new UserMailChoice { Category = UserMailCategories.Account, Receives = true, IsDefault = true },
            new UserMailChoice { Category = UserMailCategories.Service with { Required = false }, Receives = true, IsDefault = true }
        ]);

        Assert.DoesNotContain("rz-state-disabled", Switch(view, "service").ClassName);
        Assert.Empty(view.FindAll("[data-required=\"service\"]"));
        Assert.Single(view.FindAll("[data-required]"));
    }

    [Fact]
    public void TheAlwaysSentLine_IsTranslatedThroughTheTextProvider()
    {
        var view = Render(texts: new Translating(new Dictionary<string, string> { [UserMailText.AlwaysSent.Key] = "Skickas alltid: en del av användarvillkoren" }));

        Assert.Contains("Skickas alltid: en del av användarvillkoren", view.Find("[data-required=\"account\"]").TextContent);
    }

    [Fact]
    public void EachSwitch_ShowsTheUsersEffectiveChoice_AndIsNamedForScreenReaders()
    {
        var view = Render();

        Assert.Contains("rz-switch-checked", Switch(view, "news").ClassName);
        Assert.DoesNotContain("rz-switch-checked", Switch(view, "digest").ClassName);
        Assert.Equal("News", Switch(view, "news").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task TurningAnOptionalCategoryOff_SavesAtOnce_ForTheSignedInUser()
    {
        var view = Render();

        await view.InvokeAsync(() => Switch(view, "news").Click());

        _preferences.Verify(x => x.SetChoiceAsync(Me, "news", false, It.IsAny<CancellationToken>()), Times.Once);
        view.WaitForAssertion(() => Assert.DoesNotContain("rz-switch-checked", Switch(view, "news").ClassName));
    }

    [Fact]
    public async Task ARequiredCategory_CannotBeToggled()
    {
        var view = Render();

        await view.InvokeAsync(() => Switch(view, "service").Click());

        _preferences.Verify(x => x.SetChoiceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void AnotherUsersChoices_AreShown_WhenNamed()
    {
        Render(userKey: "user-9");

        _preferences.Verify(x => x.GetChoicesAsync("user-9", It.IsAny<CancellationToken>()), Times.Once);
        _users.Verify(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>()), Times.Never);
    }

    [Fact]
    public void AReaderWhoMayNotSeeTheChoices_IsToldSo()
    {
        _preferences.Setup(x => x.GetChoicesAsync("user-9", It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        var user = new Mock<IUser>();
        user.SetupGet(x => x.Key).Returns(Me);
        _users.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user.Object);
        Services.AddSingleton(_preferences.Object);
        Services.AddSingleton(_users.Object);
        Services.AddSingleton<IThargaTextProvider>(new DefaultThargaTextProvider());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render<UserMailPreferencesView>(p => p.Add(x => x.UserKey, "user-9"));

        Assert.Contains("You cannot see or change this user's mail choices.", view.Markup);
        Assert.Empty(view.FindAll("[data-category]"));
    }

    [Fact]
    public async Task ASaveThatFails_IsReported_AndTheSwitchShowsWhatIsStillInForce()
    {
        var view = Render();
        _preferences.Setup(x => x.SetChoiceAsync(Me, "news", false, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

        await view.InvokeAsync(() => Switch(view, "news").Click());

        Assert.Contains(Services.GetRequiredService<NotificationService>().Messages, x => x.Summary == "Your choice could not be saved." || x.Detail == "Your choice could not be saved.");
        view.WaitForAssertion(() => Assert.Contains("rz-switch-checked", Switch(view, "news").ClassName));
    }

    [Fact]
    public void CategoryNamesAndDescriptions_AreTranslatedThroughTheTextProvider()
    {
        var view = Render(texts: new Translating(new Dictionary<string, string>
        {
            [News.NameKey] = "Nyheter",
            [News.DescriptionKey] = "Vad som är nytt.",
            [UserMailPreferencesViewText.Title.Key] = "Mejl jag får"
        }));

        Assert.Contains("Mejl jag får", view.Markup);
        Assert.Contains("Nyheter", view.Markup);
        Assert.Contains("Vad som är nytt.", view.Markup);
    }

    private sealed class Translating(Dictionary<string, string> texts) : IThargaTextProvider
    {
        public Task<string> GetAsync(TextKey key) => Task.FromResult(texts.GetValueOrDefault(key.Key) ?? key.Default);
    }
}
