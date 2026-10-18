using System.Security.Claims;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// "Mail I receive", "Notices" and "Mail sent" draw their own card and title by default; unframed, a host places them in
/// tabs of its own and they show everything else as before: the explanation, the content, and the empty and refused states.
/// </summary>
public class FramedUserViewsRenderTests : BunitContext
{
    private const string Me = "user-1";

    private readonly Mock<IUserMailPreferenceService> _preferences = new();
    private readonly Mock<IUserNoticePreferenceService> _notices = new();
    private readonly Mock<IUserMailHistoryService> _history = new();
    private readonly Mock<IUserService> _users = new();

    public FramedUserViewsRenderTests()
    {
        var user = new Mock<IUser>();
        user.SetupGet(x => x.Key).Returns(Me);
        _users.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user.Object);
        _preferences.Setup(x => x.GetCategories()).Returns([UserMailCategories.Account, UserMailCategories.Service]);
        _preferences.Setup(x => x.GetChoicesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new UserMailChoice { Category = UserMailCategories.Account, Receives = true, IsDefault = true },
            new UserMailChoice { Category = UserMailCategories.Service, Receives = true, IsDefault = true }
        ]);
        _notices.Setup(x => x.GetChoicesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _history.Setup(x => x.GetMineAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new UserMailHistoryPage { Items = [] });

        Services.AddSingleton(_preferences.Object);
        Services.AddSingleton(_notices.Object);
        Services.AddSingleton(_history.Object);
        Services.AddSingleton(_users.Object);
        Services.AddSingleton<IThargaTextProvider>(new DefaultThargaTextProvider());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ByDefault_MailIReceive_IsACardWithItsTitle()
    {
        var view = Render<UserMailPreferencesView>();

        Assert.Single(view.FindAll(".rz-card"));
        Assert.Contains("Mail I receive", view.Find(".rz-text-h6").TextContent);
    }

    [Fact]
    public void Unframed_MailIReceive_HasNoCardOrTitle_AndShowsTheRest()
    {
        var view = Render<UserMailPreferencesView>(p => p.Add(x => x.Framed, false));

        Assert.Empty(view.FindAll(".rz-card"));
        Assert.Empty(view.FindAll(".rz-text-h6"));
        Assert.DoesNotContain("Mail I receive", view.Markup);
        Assert.Contains("Choose which mail you want. A change is saved at once.", view.Markup);
        Assert.Equal(["account", "service"], view.FindAll("[data-category]").Select(x => x.GetAttribute("data-category")));
    }

    [Fact]
    public void Unframed_MailIReceive_StillSaysWhenNobodyIsSignedIn()
    {
        _users.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync((IUser)null);

        var view = Render<UserMailPreferencesView>(p => p.Add(x => x.Framed, false));

        Assert.Empty(view.FindAll(".rz-card"));
        Assert.Contains("Sign in to choose which mail you receive.", view.Markup);
    }

    [Fact]
    public void ByDefault_Notices_IsACardWithItsTitle()
    {
        var view = Render<UserNoticePreferencesView>();

        Assert.Single(view.FindAll(".rz-card"));
        Assert.Contains("Notices", view.Find(".rz-text-h6").TextContent);
    }

    [Fact]
    public void Unframed_Notices_HasNoCardOrTitle_AndShowsTheExplanationAndItsEmptyState()
    {
        var view = Render<UserNoticePreferencesView>(p => p.Add(x => x.Framed, false));

        Assert.Empty(view.FindAll(".rz-card"));
        Assert.Empty(view.FindAll(".rz-text-h6"));
        Assert.Contains("Choose which notices you receive and how. A change is saved at once.", view.Markup);
        Assert.Contains("There are no notices for you.", view.Find("[data-empty]").TextContent);
    }

    [Fact]
    public void ByDefault_MailSent_IsACardWithItsTitle()
    {
        var view = Render<UserMailHistoryView>();

        Assert.Single(view.FindAll(".rz-card"));
        Assert.Contains("Mail sent", view.Find(".rz-text-h6").TextContent);
    }

    [Fact]
    public void Unframed_MailSent_HasNoCardOrTitle_AndShowsTheExplanationAndItsEmptyState()
    {
        var view = Render<UserMailHistoryView>(p => p.Add(x => x.Framed, false));

        Assert.Empty(view.FindAll(".rz-card"));
        Assert.Empty(view.FindAll(".rz-text-h6"));
        Assert.DoesNotContain("Mail sent", view.Markup);
        Assert.Contains("The mail sent to this account, newest first.", view.Markup);
        Assert.Contains("No mail has been sent.", view.Find("[data-empty]").TextContent);
    }

    [Fact]
    public void Unframed_MailSent_ListsTheMail()
    {
        _history.Setup(x => x.GetMineAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new UserMailHistoryPage
        {
            Items = [new UserMailHistoryItem { Id = "1", Kind = UserMailKind.Message, Category = UserMailCategories.ServiceKey, Subject = "Planned maintenance", Status = UserMailStatus.Sent, QueuedAt = DateTime.UtcNow, ChangedAt = DateTime.UtcNow }]
        });

        var view = Render<UserMailHistoryView>(p => p.Add(x => x.Framed, false));

        Assert.Contains("Planned maintenance", view.Find("[data-history-item='1']").TextContent);
    }
}
