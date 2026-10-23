using System.Security.Claims;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>"Notices": each notice the user may receive with a switch, the delivery where the notice allows both, both saving at once, and why a notice may not arrive.</summary>
public class UserNoticePreferencesViewRenderTests : BunitContext
{
    private const string Me = "user-1";

    private static readonly UserMailCategory Work = new() { Key = "work", Name = "Notices about my work" };

    private static readonly UserNoticeType Assigned = new()
    {
        Key = "case.assigned",
        Name = "A case is assigned to me",
        Description = "When someone makes you responsible for a case.",
        Category = Work.Key,
        Audience = UserNoticeAudience.NamedOnly,
        Deliveries = [UserNoticeDelivery.AtOnce, UserNoticeDelivery.Digest],
        Subject = "{subject} was assigned to you"
    };

    private static readonly UserNoticeType Joined = new()
    {
        Key = "member.joined",
        Name = "Someone joins my team",
        Category = Work.Key,
        Audience = UserNoticeAudience.AtLeast(AccessLevel.Administrator),
        Subject = "{subject} joined"
    };

    private static readonly UserNoticeType Terms = new()
    {
        Key = "terms.changed",
        Name = "The terms of use change",
        Category = UserMailCategories.AccountKey,
        Audience = UserNoticeAudience.EveryMember,
        Required = true,
        Deliveries = [UserNoticeDelivery.AtOnce, UserNoticeDelivery.Digest],
        Subject = "{subject}: the terms of use change"
    };

    private readonly Mock<IUserNoticePreferenceService> _notices = new();
    private readonly Mock<IUserService> _users = new();

    private static UserNoticeChoice[] Choices(bool categoryReceives = true, bool varies = false) =>
    [
        new() { Notice = Assigned, Category = Work, Enabled = true, Delivery = UserNoticeDelivery.AtOnce, CategoryReceives = categoryReceives, VariesByTeam = varies },
        new() { Notice = Joined, Category = Work, Enabled = false, Delivery = UserNoticeDelivery.AtOnce, EnabledChosen = true, CategoryReceives = categoryReceives }
    ];

    private IRenderedComponent<UserNoticePreferencesView> Render(UserNoticeChoice[] choices = null, string userKey = null, IThargaTextProvider texts = null)
    {
        var user = new Mock<IUser>();
        user.SetupGet(x => x.Key).Returns(Me);
        _users.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user.Object);
        _notices.Setup(x => x.GetChoicesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(choices ?? Choices());
        _notices.Setup(x => x.SetChoiceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<UserNoticeDelivery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string key, bool enabled, UserNoticeDelivery delivery, CancellationToken _) => new UserNoticeChoice
            {
                Notice = new[] { Assigned, Terms }.FirstOrDefault(x => x.Key == key) ?? Joined,
                Category = Work,
                Enabled = enabled,
                Delivery = delivery,
                EnabledChosen = true,
                DeliveryChosen = true
            });

        Services.AddSingleton(_notices.Object);
        Services.AddSingleton(_users.Object);
        Services.AddSingleton(texts ?? new DefaultThargaTextProvider());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<UserNoticePreferencesView>(p => p.Add(x => x.UserKey, userKey));
    }

    private static AngleSharp.Dom.IElement Switch(IRenderedComponent<UserNoticePreferencesView> view, string notice)
        => view.Find($"[data-notice=\"{notice}\"] .rz-switch");

    [Fact]
    public void EveryNoticeTheUserMayReceive_IsListed_WithItsDescription()
    {
        var view = Render();

        Assert.Contains("Notices", view.Markup);
        Assert.Equal(["case.assigned", "member.joined"], view.FindAll("[data-notice]").Select(x => x.GetAttribute("data-notice")));
        Assert.Contains("When someone makes you responsible for a case.", view.Markup);
        Assert.Contains("rz-switch-checked", Switch(view, "case.assigned").ClassName);
        Assert.DoesNotContain("rz-switch-checked", Switch(view, "member.joined").ClassName);
        Assert.Equal("A case is assigned to me", Switch(view, "case.assigned").GetAttribute("aria-label"));
    }

    [Fact]
    public void TheDelivery_IsOfferedOnlyWhereTheNoticeAllowsBoth()
    {
        var view = Render();

        var bar = Assert.Single(view.FindComponents<RadzenSelectBar<UserNoticeDelivery>>());
        Assert.Equal(UserNoticeDelivery.AtOnce, bar.Instance.Value);
        Assert.Contains("At once", bar.Markup);
        Assert.Contains("Daily summary", bar.Markup);
        Assert.Empty(view.FindAll("[data-delivery=\"member.joined\"]"));
    }

    private static UserNoticeChoice[] WithRequired() =>
    [
        .. Choices(),
        new() { Notice = Terms, Category = UserMailCategories.Account, Enabled = true, Delivery = UserNoticeDelivery.AtOnce }
    ];

    [Fact]
    public void ARequiredNotice_IsShownAlwaysOn_WithADisabledSwitchAndWhy()
    {
        var view = Render(WithRequired());

        var required = Switch(view, "terms.changed");
        Assert.Contains("rz-switch-checked", required.ClassName);
        Assert.Contains("rz-state-disabled", required.ClassName);
        Assert.Contains("Always sent: part of the terms of use", view.Find("[data-required=\"terms.changed\"]").TextContent);

        Assert.DoesNotContain("rz-state-disabled", Switch(view, "case.assigned").ClassName);
        Assert.Single(view.FindAll("[data-required]"));
    }

    [Fact]
    public async Task TheDeliveryOfARequiredNotice_CanStillBeChosen()
    {
        var view = Render([WithRequired().Last()]);

        var bar = view.FindComponent<RadzenSelectBar<UserNoticeDelivery>>();
        Assert.False(bar.Instance.Disabled);

        await view.InvokeAsync(() => bar.Instance.Change.InvokeAsync(UserNoticeDelivery.Digest));

        _notices.Verify(x => x.SetChoiceAsync(Me, "terms.changed", true, UserNoticeDelivery.Digest, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void WithTermsOfUse_TheAlwaysSentLine_LinksToThem()
    {
        var preferences = new Mock<IUserMailPreferenceService>();
        preferences.Setup(x => x.GetTermsUrl()).Returns("/terms");
        Services.AddSingleton(preferences.Object);

        var view = Render(WithRequired());

        var link = view.Find("[data-required=\"terms.changed\"] a");
        Assert.Equal("/terms", link.GetAttribute("href"));
        Assert.Equal("Always sent: part of the terms of use", link.TextContent);
    }

    [Fact]
    public void WithoutTermsOfUse_TheAlwaysSentLine_IsNoLink()
    {
        var view = Render(WithRequired());

        Assert.Empty(view.FindAll("[data-required=\"terms.changed\"] a"));
    }

    [Fact]
    public void TheAlwaysSentLine_IsTranslatedThroughTheTextProvider()
    {
        var view = Render(WithRequired(), texts: new Translating(new Dictionary<string, string> { [UserMailText.AlwaysSent.Key] = "Skickas alltid: en del av användarvillkoren" }));

        Assert.Contains("Skickas alltid: en del av användarvillkoren", view.Find("[data-required=\"terms.changed\"]").TextContent);
    }

    [Fact]
    public async Task TurningANoticeOff_SavesAtOnce_KeepingItsDelivery()
    {
        var view = Render();

        await view.InvokeAsync(() => Switch(view, "case.assigned").Click());

        _notices.Verify(x => x.SetChoiceAsync(Me, "case.assigned", false, UserNoticeDelivery.AtOnce, It.IsAny<CancellationToken>()), Times.Once);
        view.WaitForAssertion(() => Assert.DoesNotContain("rz-switch-checked", Switch(view, "case.assigned").ClassName));
    }

    [Fact]
    public async Task ChoosingTheDailySummary_SavesAtOnce()
    {
        var view = Render();

        var bar = view.FindComponent<RadzenSelectBar<UserNoticeDelivery>>();
        await view.InvokeAsync(() => bar.Instance.Change.InvokeAsync(UserNoticeDelivery.Digest));

        _notices.Verify(x => x.SetChoiceAsync(Me, "case.assigned", true, UserNoticeDelivery.Digest, It.IsAny<CancellationToken>()), Times.Once);
        view.WaitForAssertion(() => Assert.Equal(UserNoticeDelivery.Digest, view.FindComponent<RadzenSelectBar<UserNoticeDelivery>>().Instance.Value));
    }

    [Fact]
    public void ANoticeWhoseMailCategoryIsOff_SaysSo_AndOneWhoseTeamsDisagree_SaysThat()
    {
        var view = Render(Choices(categoryReceives: false, varies: true));

        Assert.Contains("Not sent while Notices about my work is turned off in the mail you receive.", view.Find("[data-category-off=\"case.assigned\"]").TextContent);
        Assert.NotNull(view.Find("[data-varies=\"case.assigned\"]"));
        Assert.Empty(view.FindAll("[data-varies=\"member.joined\"]"));
    }

    [Fact]
    public void AUserWithNoNotices_IsToldSo()
    {
        var view = Render([]);

        Assert.Contains("There are no notices for you.", view.Find("[data-empty]").TextContent);
    }

    [Fact]
    public void AnotherUsersChoices_AreAskedFor_WhenNamed_AndARefusalIsShown()
    {
        _notices.Setup(x => x.GetChoicesAsync("user-9", It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        Services.AddSingleton(_notices.Object);
        Services.AddSingleton(_users.Object);
        Services.AddSingleton<IThargaTextProvider>(new DefaultThargaTextProvider());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render<UserNoticePreferencesView>(p => p.Add(x => x.UserKey, "user-9"));

        Assert.Contains("You cannot see or change this user's notice choices.", view.Markup);
        Assert.Empty(view.FindAll("[data-notice]"));
        _users.Verify(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>()), Times.Never);
    }

    [Fact]
    public async Task ASaveThatFails_IsReported_AndTheSwitchShowsWhatIsStillInForce()
    {
        var view = Render();
        _notices.Setup(x => x.SetChoiceAsync(Me, "case.assigned", false, UserNoticeDelivery.AtOnce, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

        await view.InvokeAsync(() => Switch(view, "case.assigned").Click());

        Assert.Contains(Services.GetRequiredService<NotificationService>().Messages, x => x.Summary == "Your choice could not be saved." || x.Detail == "Your choice could not be saved.");
        view.WaitForAssertion(() => Assert.Contains("rz-switch-checked", Switch(view, "case.assigned").ClassName));
    }

    [Fact]
    public void NoticeNames_AndTheViewsOwnText_AreTranslatedThroughTheTextProvider()
    {
        var view = Render(texts: new Translating(new Dictionary<string, string>
        {
            [Assigned.NameKey] = "Ett ärende tilldelas mig",
            [UserNoticePreferencesViewText.Title.Key] = "Aviseringar",
            [UserNoticeText.Digest.Key] = "Daglig sammanfattning"
        }));

        Assert.Contains("Aviseringar", view.Markup);
        Assert.Contains("Ett ärende tilldelas mig", view.Markup);
        Assert.Contains("Daglig sammanfattning", view.Markup);
    }

    private sealed class Translating(Dictionary<string, string> texts) : IThargaTextProvider
    {
        public Task<string> GetAsync(TextKey key) => Task.FromResult(texts.GetValueOrDefault(key.Key) ?? key.Default);
    }
}
