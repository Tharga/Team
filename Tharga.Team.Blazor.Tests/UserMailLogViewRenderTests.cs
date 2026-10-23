using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// "Mail log": every mail the application sent or tried to send, who it was for and what became of it, narrowed by filters, a
/// page at a time, with each user's own mail one click away, and a reader without the right told so.
/// </summary>
public class UserMailLogViewRenderTests : BunitContext
{
    private static readonly DateTime Queued = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    private static readonly UserMailCategory News = new() { Key = "news", Name = "News" };

    private static readonly UserMailHistoryItem Welcome = new()
    {
        Id = "m1",
        Kind = UserMailKind.Mail,
        Category = "news",
        Subject = "Welcome",
        Status = UserMailStatus.Sent,
        Address = "anna@example.org",
        QueuedAt = Queued,
        ChangedAt = Queued,
        RecipientUserKey = "user-1",
        RecipientName = "Anna Andersson"
    };

    private static readonly UserMailHistoryItem Invitation = new()
    {
        Id = "m2",
        Kind = UserMailKind.Invitation,
        Category = "account",
        Subject = "Join Acme",
        Status = UserMailStatus.Failed,
        Reason = "The mailbox is full.",
        Address = "bea@example.org",
        QueuedAt = Queued.AddMinutes(1),
        ChangedAt = Queued.AddMinutes(1),
        RecipientName = "Bea Berg",
        TeamKey = "acme"
    };

    private static readonly UserMailHistoryItem Newsletter = new()
    {
        Id = "m3",
        Kind = UserMailKind.Contact,
        Category = "news",
        Subject = "Newsletter",
        Status = UserMailStatus.Queued,
        QueuedAt = Queued.AddMinutes(2),
        ChangedAt = Queued.AddMinutes(2),
        RecipientName = "Cora Carlsson"
    };

    private readonly Mock<IUserMailHistoryService> _history = new();
    private readonly List<(UserMailFilter Filter, string Cursor, int Size)> _asked = [];

    private IRenderedComponent<UserMailLogView> RenderLog(UserMailHistoryPage first = null, UserMailHistoryPage second = null, bool permitted = true, bool framed = true, IThargaTextProvider texts = null)
    {
        first ??= new UserMailHistoryPage { Items = [Newsletter, Invitation, Welcome] };

        _history.Setup(x => x.ListAllAsync(It.IsAny<UserMailFilter>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((UserMailFilter filter, string cursor, int size, CancellationToken _) =>
            {
                _asked.Add((filter, cursor, size));
                if (!permitted) throw new UnauthorizedAccessException();

                return Task.FromResult(cursor == null ? first : second ?? new UserMailHistoryPage());
            });
        _history.Setup(x => x.GetForUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new UserMailHistoryPage());

        var preferences = new Mock<IUserMailPreferenceService>();
        preferences.Setup(x => x.GetCategories()).Returns([News, UserMailCategories.Account]);

        Services.AddSingleton(_history.Object);
        Services.AddSingleton(preferences.Object);
        Services.AddSingleton(Mock.Of<IUserService>());
        Services.AddSingleton(texts ?? new DefaultThargaTextProvider());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<UserMailLogView>(p => p.Add(x => x.Framed, framed));
    }

    private static string[] Subjects(IRenderedComponent<UserMailLogView> view)
        => [.. view.FindAll("[data-subject]").Select(x => x.TextContent.Trim())];

    [Fact]
    public void EveryMail_IsListed_WithWhoItWasFor_ItsKindCategoryAndResult_AndTheTeam()
    {
        var view = RenderLog();

        Assert.Equal(["Newsletter", "Join Acme", "Welcome"], Subjects(view));
        Assert.Equal("Anna Andersson", view.Find("[data-recipient='m1'] [data-name]").TextContent.Trim());
        Assert.Equal("anna@example.org", view.Find("[data-recipient='m1'] [data-who]").TextContent.Trim());
        Assert.Equal("Contact", view.Find("[data-kind='Contact']").TextContent.Trim());
        Assert.Equal("Invitation", view.Find("[data-kind='Invitation']").TextContent.Trim());
        Assert.Equal("Not sent", view.Find("[data-status='Failed']").TextContent.Trim());
        Assert.Equal("The mailbox is full.", view.Find("[data-reason]").TextContent.Trim());
        Assert.Equal("acme", view.Find("[data-team]:not(:empty)").TextContent.Trim());
        Assert.Equal("News", view.FindAll("[data-category='news']")[0].TextContent.Trim());
    }

    [Fact]
    public void TheFirstRead_AsksForEverything_AFirstPageAtATime()
    {
        RenderLog();

        var asked = Assert.Single(_asked);
        Assert.Equal(new UserMailFilter(), asked.Filter);
        Assert.Null(asked.Cursor);
        Assert.Equal(50, asked.Size);
    }

    [Fact]
    public void WhenNothingMatches_TheViewSaysSo()
    {
        var view = RenderLog(new UserMailHistoryPage());

        Assert.Equal(UserMailLogViewText.Empty.Default, view.Find("[data-empty]").TextContent.Trim());
        Assert.Empty(view.FindAll("[data-show-more]"));
    }

    [Fact]
    public async Task Searching_ReadsAgainFromTheStart_WithWhatWasChosenAndTyped()
    {
        var view = RenderLog();
        var kind = view.FindComponents<RadzenDropDown<UserMailKind?>>().Single();
        await view.InvokeAsync(() => kind.Instance.ValueChanged.InvokeAsync(UserMailKind.Invitation));
        view.Find("[data-filter='recipient']").Change("Bea@Example.org");
        view.Find("[data-filter='team']").Change(" acme ");

        view.Find("[data-search]").Click();

        view.WaitForAssertion(() => Assert.Equal(2, _asked.Count));
        var filter = _asked[1].Filter;
        Assert.Equal((UserMailKind.Invitation, "Bea@Example.org", "acme"), (filter.Kind, filter.RecipientAddress, filter.TeamKey));
        Assert.Null(_asked[1].Cursor);
    }

    [Fact]
    public void Searching_ShowsOnlyWhatTheNewReadReturns()
    {
        var view = RenderLog();
        _history.Setup(x => x.ListAllAsync(It.IsAny<UserMailFilter>(), null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserMailHistoryPage { Items = [Invitation] });

        view.Find("[data-search]").Click();

        view.WaitForAssertion(() => Assert.Equal(["Join Acme"], Subjects(view)));
    }

    [Fact]
    public void Clearing_ForgetsTheFilters_AndReadsEverythingAgain()
    {
        var view = RenderLog();
        view.Find("[data-filter='team']").Change("acme");
        view.Find("[data-search]").Click();
        view.WaitForAssertion(() => Assert.Equal("acme", _asked[1].Filter.TeamKey));

        view.Find("[data-clear]").Click();

        view.WaitForAssertion(() => Assert.Equal(3, _asked.Count));
        Assert.Equal(new UserMailFilter(), _asked[2].Filter);
    }

    [Fact]
    public void TheNextPage_IsReadWithTheCursor_UnderTheSameFilter_AndAddedBelow()
    {
        var view = RenderLog(new UserMailHistoryPage { Items = [Newsletter], NextCursor = "cursor-1" }, new UserMailHistoryPage { Items = [Welcome] });

        view.Find("[data-show-more]").Click();

        view.WaitForAssertion(() => Assert.Equal(["Newsletter", "Welcome"], Subjects(view)));
        Assert.Equal("cursor-1", _asked[1].Cursor);
        Assert.Empty(view.FindAll("[data-show-more]"));
    }

    [Fact]
    public void OnlyMailForAUser_OffersThatUsersMail()
    {
        var view = RenderLog();

        Assert.Single(view.FindAll("[data-user-mail]"));
        Assert.NotNull(view.Find("[data-user-mail='m1']"));
    }

    [Fact]
    public void ChoosingAUsersMail_ShowsThatUsersOwnHistory_InADialog()
    {
        var view = RenderLog();
        var dialog = Render<RadzenDialog>();

        view.Find("[data-user-mail='m1']").Click();

        dialog.WaitForAssertion(() => Assert.Contains("Mail sent — anna@example.org", dialog.Markup));
        _history.Verify(x => x.GetForUserAsync("user-1", null, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AReaderWithoutTheRight_IsToldSo_NotShownTheLog()
    {
        var view = RenderLog(permitted: false);

        Assert.Contains(UserMailLogViewText.NotPermitted.Default, view.Markup);
        Assert.Empty(view.FindAll("[data-filters]"));
    }

    [Fact]
    public void Unframed_TheViewDrawsNoTitle()
    {
        Assert.DoesNotContain(UserMailLogViewText.Title.Default, RenderLog(framed: false).Markup);
    }

    [Fact]
    public void Framed_TheViewDrawsItsTitle()
    {
        Assert.Contains(UserMailLogViewText.Title.Default, RenderLog().Markup);
    }

    [Fact]
    public void TheTexts_AreTranslatedThroughTheTextProvider()
    {
        var view = RenderLog(texts: new Translating(new Dictionary<string, string>
        {
            [UserMailLogViewText.Title.Key] = "Mejllogg",
            [UserMailHistoryViewText.KindContact.Key] = "Kontakt",
            [new TextKey(News.NameKey, News.Name).Key] = "Nyheter"
        }));

        Assert.Contains("Mejllogg", view.Markup);
        Assert.Equal("Kontakt", view.Find("[data-kind='Contact']").TextContent.Trim());
        Assert.Equal("Nyheter", view.FindAll("[data-category='news']")[0].TextContent.Trim());
    }

    private sealed class Translating(Dictionary<string, string> texts) : IThargaTextProvider
    {
        public Task<string> GetAsync(TextKey key) => Task.FromResult(texts.GetValueOrDefault(key.Key) ?? key.Default);
    }
}
