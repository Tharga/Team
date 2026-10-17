using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>"Mail sent": the mail sent to one user, newest first, each with its kind, category, subject, status, time and why, the signed-in user's own or a named user's.</summary>
public class UserMailHistoryViewRenderTests : BunitContext
{
    private const string Me = "user-1";
    private static readonly DateTime At = new(2026, 10, 17, 9, 30, 0, DateTimeKind.Utc);
    private static readonly UserMailCategory News = new() { Key = "news", Name = "News" };

    private readonly Mock<IUserMailHistoryService> _history = new();
    private readonly Mock<IUserMailPreferenceService> _preferences = new();
    private readonly Mock<IUserService> _users = new();

    private static UserMailHistoryItem Item(string id, string subject, UserMailKind kind, UserMailStatus status, string category = "news", string reason = null, string address = null) => new()
    {
        Id = id,
        Kind = kind,
        Category = category,
        Subject = subject,
        Status = status,
        Reason = reason,
        Address = address,
        QueuedAt = At,
        ChangedAt = At.AddMinutes(1)
    };

    private IRenderedComponent<UserMailHistoryView> Render(string userKey = null, bool signedIn = true)
    {
        var user = new Mock<IUser>();
        user.SetupGet(x => x.Key).Returns(Me);
        _users.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(signedIn ? user.Object : null);
        _preferences.Setup(x => x.GetCategories()).Returns([UserMailCategories.Account, UserMailCategories.Service, News]);

        Services.AddSingleton(_history.Object);
        Services.AddSingleton(_preferences.Object);
        Services.AddSingleton(_users.Object);
        Services.AddSingleton<IThargaTextProvider>(new DefaultThargaTextProvider());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        RenderFragment<DateTime> time = value => builder => builder.AddContent(0, $"at {value:yyyy-MM-dd HH:mm}");

        return Render<UserMailHistoryView>(p => p.Add(x => x.UserKey, userKey).Add(x => x.TimeTemplate, time));
    }

    [Fact]
    public void TheSignedInUsersOwnMail_IsListed_WithKindCategoryStatusAndTime()
    {
        _history.Setup(x => x.GetMineAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new UserMailHistoryPage
        {
            Items =
            [
                Item("3", "You are invited", UserMailKind.Invitation, UserMailStatus.Sent, UserMailCategories.AccountKey, address: "anna@example.org"),
                Item("2", "Planned maintenance", UserMailKind.Message, UserMailStatus.Failed, UserMailCategories.ServiceKey, reason: "The user record holds no address."),
                Item("1", "Something new", UserMailKind.Notice, UserMailStatus.Skipped)
            ]
        });

        var view = Render();

        Assert.Contains("Mail sent", view.Markup);
        Assert.Equal(["3", "2", "1"], view.FindAll("[data-history-item]").Select(x => x.GetAttribute("data-history-item")));

        var invitation = view.Find("[data-history-item='3']");
        Assert.Contains("You are invited", invitation.TextContent);
        Assert.Equal("Invitation", invitation.QuerySelector("[data-kind]")!.TextContent.Trim());
        Assert.Equal("Sent", invitation.QuerySelector("[data-status]")!.TextContent.Trim());
        Assert.Equal("Account and security", invitation.QuerySelector("[data-category]")!.TextContent.Trim());
        Assert.Equal("To anna@example.org", invitation.QuerySelector("[data-address]")!.TextContent.Trim());
        Assert.Contains("at 2026-10-17 09:31", invitation.QuerySelector("[data-time]")!.TextContent);

        var message = view.Find("[data-history-item='2']");
        Assert.Equal("Message", message.QuerySelector("[data-kind]")!.TextContent.Trim());
        Assert.Equal("Not sent", message.QuerySelector("[data-status]")!.TextContent.Trim());
        Assert.Contains("The user record holds no address.", message.QuerySelector("[data-reason]")!.TextContent);

        var notice = view.Find("[data-history-item='1']");
        Assert.Equal("Notice", notice.QuerySelector("[data-kind]")!.TextContent.Trim());
        Assert.Equal("Turned off", notice.QuerySelector("[data-status]")!.TextContent.Trim());
        Assert.Equal("News", notice.QuerySelector("[data-category]")!.TextContent.Trim());

        _history.Verify(x => x.GetForUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ANamedUsersMail_IsReadForThatUser()
    {
        _history.Setup(x => x.GetForUserAsync("user-2", null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserMailHistoryPage { Items = [Item("1", "Hello", UserMailKind.Mail, UserMailStatus.Queued)] });

        var view = Render("user-2");

        Assert.Equal("Mail", view.Find("[data-kind]").TextContent.Trim());
        Assert.Equal("Waiting", view.Find("[data-status]").TextContent.Trim());
        _history.Verify(x => x.GetMineAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ShowMore_ReadsTheNextPage()
    {
        _history.Setup(x => x.GetMineAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserMailHistoryPage { Items = [Item("2", "Second", UserMailKind.Mail, UserMailStatus.Sent)], NextCursor = "c-1" });
        _history.Setup(x => x.GetMineAsync("c-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserMailHistoryPage { Items = [Item("1", "First", UserMailKind.Mail, UserMailStatus.Sent)] });
        var view = Render();

        await view.InvokeAsync(() => view.Find("[data-show-more]").Click());

        Assert.Equal(["2", "1"], view.FindAll("[data-history-item]").Select(x => x.GetAttribute("data-history-item")));
        Assert.Empty(view.FindAll("[data-show-more]"));
    }

    [Fact]
    public void NoMail_SaysSo()
    {
        _history.Setup(x => x.GetMineAsync(null, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new UserMailHistoryPage());

        var view = Render();

        Assert.Contains(UserMailHistoryViewText.Empty.Default, view.Find("[data-empty]").TextContent);
    }

    [Fact]
    public void AReaderWhoMayNotSeeAnotherUsersMail_IsToldSo()
    {
        _history.Setup(x => x.GetForUserAsync("user-2", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());

        var view = Render("user-2");

        Assert.Contains(UserMailHistoryViewText.NotPermitted.Default, view.Markup);
        Assert.Empty(view.FindAll("[data-mail-history]"));
    }

    [Fact]
    public void NobodySignedIn_AndNoUserNamed_IsAskedToSignIn()
    {
        var view = Render(signedIn: false);

        Assert.Contains(UserMailHistoryViewText.NoUser.Default, view.Markup);
        _history.Verify(x => x.GetMineAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
