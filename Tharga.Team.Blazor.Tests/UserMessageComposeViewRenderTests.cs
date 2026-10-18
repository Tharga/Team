using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.Messages;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Staff write a message: a framed filter of teams and who in them, users added by name whatever the filter says, and
/// beside them the list of who that reaches — by filter or added, the added removable, those who turned the category
/// off marked — then a category, subject, rich text and files, a preview of the mail, and a send that states the count
/// before it goes.
/// </summary>
public class UserMessageComposeViewRenderTests : BunitContext
{
    private static readonly UserMailCategory News = new() { Key = "news", Name = "News" };

    private readonly Mock<IUserMessageService> _messages = new();

    private static UserMessageChoices Choices() => new()
    {
        Teams = [new UserMessageTeam { Key = "alpha", Name = "Alpha" }, new UserMessageTeam { Key = "beta", Name = "Beta" }],
        Roles = ["Editor"],
        AccessLevels = [AccessLevel.Owner, AccessLevel.User],
        Categories = [UserMailCategories.Service, News],
        Limits = new UserMessageLimits { MaxAttachmentCount = 3, MaxAttachmentBytes = 1000, MaxBodyLength = 5000 }
    };

    private static readonly UserMessageAddressee Anna = new() { UserKey = "anna", Name = "Anna", EMail = "anna@example.org", TeamKeys = ["alpha", "beta"], ByFilter = true };
    private static readonly UserMessageAddressee Bo = new() { UserKey = "bo", Name = "Bo", EMail = "bo@example.org", TeamKeys = ["alpha"], ByFilter = true, OptedOut = true };
    private static readonly UserMessageAddressee DanAdded = new() { UserKey = "dan", Name = "Dan", EMail = "dan@example.org", TeamKeys = ["beta"], Added = true };
    private static readonly UserMessageCandidate Dan = new() { UserKey = "dan", Name = "Dan", EMail = "dan@example.org", TeamKeys = ["beta"] };

    private IRenderedComponent<UserMessageComposeView> Render(Action<SentUserMessage> sent = null, Func<UserMessageRecipients, int, UserMessageAddresseePage> list = null, IThargaTextProvider texts = null)
    {
        list ??= (selection, _) => selection.UserKeys.Contains("dan")
            ? new UserMessageAddresseePage { Items = [Anna, DanAdded], Total = 2 }
            : new UserMessageAddresseePage { Items = [Anna, Bo], Total = 4, LeftOut = 1 };

        _messages.Setup(x => x.GetChoicesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Choices());
        _messages.Setup(x => x.GetAddresseesAsync(It.IsAny<UserMessageRecipients>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserMessageRecipients selection, string _, int skip, int _, CancellationToken _) => list(selection, skip));
        _messages.Setup(x => x.SendAsync(It.IsAny<UserMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserMessage m, CancellationToken _) => new SentUserMessage
            {
                Id = "m-1",
                Recipients = m.Recipients,
                Category = m.Category,
                Subject = m.Subject,
                SentAt = DateTime.UtcNow,
                RecipientCount = 4,
                Outcomes = new UserMailOutcomes { Queued = 4 }
            });
        _messages.Setup(x => x.PreviewMailAsync(It.IsAny<UserMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserMessage m, CancellationToken _) => new MailPreview { From = "Example <hello@example.com>", Subject = m.Subject, Html = "<html><body><p>Framed</p></body></html>", Text = "Framed" });

        Services.AddSingleton(_messages.Object);
        Services.AddSingleton(texts ?? new DefaultThargaTextProvider());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<UserMessageComposeView>(p => p.Add(x => x.MessageSent, sent ?? (_ => { })));
    }

    private static Task SwitchAsync(IRenderedComponent<UserMessageComposeView> view, int index, bool value)
        => view.InvokeAsync(() => view.FindComponents<RadzenSwitch>()[index].Instance.Change.InvokeAsync(value));

    private static Task ChooseTeamsAsync(IRenderedComponent<UserMessageComposeView> view, params string[] teams)
        => view.InvokeAsync(() => view.FindComponents<RadzenDropDown<IEnumerable<string>>>()[0].Instance.ValueChanged.InvokeAsync(teams));

    private static async Task WriteAsync(IRenderedComponent<UserMessageComposeView> view)
    {
        var subject = view.FindComponents<RadzenTextBox>().Single(x => x.Instance.Attributes?.ContainsKey("data-subject") == true);
        await view.InvokeAsync(() => subject.Instance.ValueChanged.InvokeAsync("Planned maintenance"));
        await view.InvokeAsync(() => view.FindComponent<RichTextEditor>().Instance.ValueChanged.InvokeAsync("<p>Sunday</p>"));
    }

    private async Task AddDanAsync(IRenderedComponent<UserMessageComposeView> view)
    {
        _messages.Setup(x => x.SearchUsersAsync(It.IsAny<UserMessageUserSearch>(), It.IsAny<CancellationToken>())).ReturnsAsync([Dan]);
        view.Find("input[data-user-search]").Input("da");
        await view.InvokeAsync(() => view.Find("[data-user-match='dan']").Click());
    }

    private static AngleSharp.Dom.IElement SendButton(IRenderedComponent<UserMessageComposeView> view) => view.Find("[data-send]");

    private void VerifyListed(Func<UserMessageRecipients, bool> selection, Times times)
        => _messages.Verify(x => x.GetAddresseesAsync(It.Is<UserMessageRecipients>(r => selection(r)), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public void AReaderWithoutTheScope_IsToldSo()
    {
        _messages.Setup(x => x.GetChoicesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        Services.AddSingleton(_messages.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();

        var view = Render<UserMessageComposeView>();

        Assert.Contains(UserMessageComposeViewText.NotPermitted.Default, view.Markup);
        Assert.Empty(view.FindAll("[data-compose]"));
    }

    [Fact]
    public void BeforeAnythingIsChosen_NobodyIsListed_AndNothingCanBeSent()
    {
        var view = Render();

        Assert.Contains(UserMessageComposeViewText.NoTeams.Default, view.Find("[data-no-teams]").TextContent);
        Assert.Empty(view.FindAll("[data-addressee]"));
        Assert.True(SendButton(view).HasAttribute("disabled"));
        VerifyListed(_ => true, Times.Never());
    }

    [Fact]
    public void TheFilter_IsFramed_WithTheTeamsAndWhoInThem_AndTheUserSearchStandsOutsideIt()
    {
        var view = Render();

        var filter = view.Find("[data-filter]");
        Assert.Contains(UserMessageComposeViewText.Filter.Default, filter.TextContent);
        Assert.NotNull(filter.QuerySelector("[data-all-teams]"));
        Assert.NotNull(filter.QuerySelector("[data-everyone]"));
        Assert.Null(filter.QuerySelector("[data-user-search]"));
        Assert.NotNull(view.Find("[data-add-users] [data-user-search]"));
    }

    [Fact]
    public void TheListStandsBesideTheSelection_AndBelowItOnANarrowScreen()
    {
        var view = Render();

        var columns = view.FindComponents<RadzenColumn>();
        Assert.Equal(2, columns.Count);
        Assert.All(columns, x => Assert.Equal(12, x.Instance.Size));
        Assert.NotNull(columns[0].Find("[data-filter]"));
        Assert.NotNull(columns[1].Find("[data-recipient-list]"));
    }

    [Fact]
    public void TheCategories_AreTheOnesStaffMayUse_ByName()
    {
        var view = Render();

        var dropDown = view.FindComponent<RadzenDropDown<string>>();
        Assert.Equal("service", dropDown.Instance.Value);
        Assert.Equal(2, ((IEnumerable<object>)dropDown.Instance.Data).Count());
    }

    [Fact]
    public async Task ChoosingTeamsAndEveryone_ListsWhoItReaches_WithAddressTeamsAndHowTheyCameIn()
    {
        var view = Render();

        await SwitchAsync(view, 1, true);
        await ChooseTeamsAsync(view, "alpha");

        VerifyListed(r => !r.AllTeams && r.TeamKeys.SequenceEqual(new[] { "alpha" }) && r.EveryMember && r.UserKeys.Length == 0, Times.Once());
        Assert.Equal("4", view.Find("[data-reaches]").GetAttribute("data-reaches"));
        Assert.Contains("Reaches 4 people.", view.Markup);
        var anna = view.Find("[data-addressee='anna']");
        Assert.Contains("Anna", anna.TextContent);
        Assert.Equal("anna@example.org", anna.QuerySelector("[data-addressee-address]")!.TextContent.Trim());
        Assert.Equal("Alpha, Beta", anna.QuerySelector("[data-addressee-teams]")!.TextContent.Trim());
        Assert.NotNull(anna.QuerySelector("[data-by-filter]"));
        Assert.Null(anna.QuerySelector("[data-added]"));
        Assert.Null(anna.QuerySelector("[data-remove-added]"));
    }

    [Fact]
    public async Task AllTeams_HidesTheTeamPicker_AndListsAcrossEveryTeam()
    {
        var view = Render();

        await SwitchAsync(view, 0, true);

        Assert.Empty(view.FindAll("[data-teams]"));
        VerifyListed(r => r.AllTeams && r.TeamKeys.Length == 0, Times.Once());
    }

    [Fact]
    public async Task ARecipientWhoTurnedTheCategoryOff_IsMarked_AndTheLeftOutAreCounted()
    {
        var view = Render();

        await view.InvokeAsync(() => view.FindComponent<RadzenDropDown<string>>().Instance.ValueChanged.InvokeAsync("news"));
        await SwitchAsync(view, 0, true);

        var badge = view.Find("[data-addressee='bo'] [data-opted-out]");
        Assert.Contains(UserMessageComposeViewText.OptedOut.Default, badge.TextContent);
        Assert.Equal("Has turned off News.", badge.GetAttribute("title"));
        Assert.Empty(view.FindAll("[data-addressee='anna'] [data-opted-out]"));
        Assert.Contains("1 of them have turned off News and will not receive it.", view.Find("[data-left-out]").TextContent);
        _messages.Verify(x => x.GetAddresseesAsync(It.IsAny<UserMessageRecipients>(), "news", 0, 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Typing_SearchesEveryTeam_WhateverTheFilterSays()
    {
        _messages.Setup(x => x.SearchUsersAsync(It.IsAny<UserMessageUserSearch>(), It.IsAny<CancellationToken>())).ReturnsAsync([Dan]);
        var view = Render();

        view.Find("input[data-user-search]").Input("da");

        _messages.Verify(x => x.SearchUsersAsync(It.Is<UserMessageUserSearch>(s => s.Text == "da" && s.AllTeams && s.Limit == 20), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Dan (dan@example.org)", view.Find("[data-user-match='dan']").TextContent.Trim());
    }

    [Fact]
    public void ASearchNobodyMatches_SaysSo()
    {
        _messages.Setup(x => x.SearchUsersAsync(It.IsAny<UserMessageUserSearch>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var view = Render();

        view.Find("input[data-user-search]").Input("zz");

        Assert.Contains(UserMessageComposeViewText.NoUserMatches.Default, view.Find("[data-no-matches]").TextContent);
    }

    [Fact]
    public async Task AUserAdded_WithNoTeamChosen_IsListed_AndTheMessageGoesToThemAlone()
    {
        var view = Render();
        var dialogs = Render<RadzenDialog>();

        await AddDanAsync(view);
        await WriteAsync(view);

        VerifyListed(r => !r.AllTeams && r.TeamKeys.Length == 0 && r.UserKeys.SequenceEqual(new[] { "dan" }), Times.Once());
        var dan = view.Find("[data-addressee='dan']");
        Assert.NotNull(dan.QuerySelector("[data-added]"));
        Assert.Null(dan.QuerySelector("[data-by-filter]"));
        Assert.Empty(view.FindAll("[data-user-match]"));
        Assert.False(SendButton(view).HasAttribute("disabled"));

        await view.InvokeAsync(() => SendButton(view).Click());
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Send").Click());

        _messages.Verify(x => x.SendAsync(It.Is<UserMessage>(m => m.Recipients.UserKeys.SequenceEqual(new[] { "dan" }) && m.Recipients.TeamKeys.Length == 0 && !m.Recipients.AllTeams), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UsersAdded_StayOnTheMessage_WhenEveryoneInTheTeamsIsChosen()
    {
        var view = Render();

        await AddDanAsync(view);
        await SwitchAsync(view, 1, true);
        await ChooseTeamsAsync(view, "alpha");

        VerifyListed(r => r.EveryMember && r.TeamKeys.SequenceEqual(new[] { "alpha" }) && r.UserKeys.SequenceEqual(new[] { "dan" }), Times.Once());
        Assert.NotEmpty(view.FindAll("[data-user-search]"));
    }

    [Fact]
    public async Task RemovingAnAddedUserFromTheList_TakesThemOffTheMessage()
    {
        var view = Render();
        await AddDanAsync(view);

        await view.InvokeAsync(() => view.Find("[data-remove-added='dan']").Click());

        Assert.Empty(view.FindAll("[data-chosen-user]"));
        Assert.Empty(view.FindAll("[data-addressee]"));
        Assert.NotNull(view.Find("[data-no-teams]"));
    }

    [Fact]
    public async Task RemovingAnAddedUserByTheirChip_TakesThemOffTheMessage()
    {
        var view = Render();
        await AddDanAsync(view);
        await ChooseTeamsAsync(view, "beta");
        await SwitchAsync(view, 1, true);

        await view.InvokeAsync(() => view.Find("[data-chosen-user='dan']").Click());

        VerifyListed(r => r.UserKeys.Length == 0 && r.TeamKeys.SequenceEqual(new[] { "beta" }) && r.EveryMember, Times.Once());
    }

    [Fact]
    public async Task Everyone_HidesTheLevelsAndRoles()
    {
        var view = Render();

        Assert.NotEmpty(view.FindAll("[data-roles]"));
        await SwitchAsync(view, 1, true);

        Assert.Empty(view.FindAll("[data-roles]"));
        Assert.Empty(view.FindAll("[data-levels]"));
    }

    [Fact]
    public async Task ALongList_IsReadAPageAtATime()
    {
        var page = Enumerable.Range(0, 25).Select(i => new UserMessageAddressee { UserKey = $"u-{i:D2}", Name = $"Person {i:D2}", ByFilter = true }).ToArray();
        var next = Enumerable.Range(25, 10).Select(i => new UserMessageAddressee { UserKey = $"u-{i:D2}", Name = $"Person {i:D2}", ByFilter = true }).ToArray();
        var view = Render(list: (_, skip) => new UserMessageAddresseePage { Items = skip == 0 ? page : next, Total = 35 });

        await SwitchAsync(view, 0, true);
        await SwitchAsync(view, 1, true);
        Assert.Equal(25, view.FindAll("[data-addressee]").Count);

        await view.InvokeAsync(() => view.Find("[data-more-addressees]").Click());

        Assert.Equal(35, view.FindAll("[data-addressee]").Count);
        Assert.Empty(view.FindAll("[data-more-addressees]"));
        _messages.Verify(x => x.GetAddresseesAsync(It.IsAny<UserMessageRecipients>(), It.IsAny<string>(), 25, 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void TheFilePicker_IsHeldToTheMessagesLimits()
    {
        var view = Render();

        var limits = view.FindComponent<SupportAttachmentPicker>().Instance.Limits;
        Assert.Equal((3, 1000L, 1000L), (limits.MaxCount, limits.MaxFileBytes, limits.MaxTotalBytes));
    }

    [Fact]
    public async Task Sending_AsksFirst_StatingTheCount_ThenSendsTheSelection()
    {
        SentUserMessage raised = null;
        var view = Render(x => raised = x);
        var dialogs = Render<RadzenDialog>();

        await SwitchAsync(view, 1, true);
        await ChooseTeamsAsync(view, "alpha", "beta");
        await WriteAsync(view);
        Assert.False(SendButton(view).HasAttribute("disabled"));

        await view.InvokeAsync(() => SendButton(view).Click());

        Assert.Contains("Send \"Planned maintenance\" to 4 people?", dialogs.Markup);
        _messages.Verify(x => x.SendAsync(It.IsAny<UserMessage>(), It.IsAny<CancellationToken>()), Times.Never);

        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Send").Click());

        _messages.Verify(x => x.SendAsync(It.Is<UserMessage>(m =>
            m.Recipients.EveryMember && m.Recipients.TeamKeys.SequenceEqual(new[] { "alpha", "beta" })
            && m.Category == "service" && m.Subject == "Planned maintenance" && m.BodyHtml == "<p>Sunday</p>"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("m-1", raised?.Id);
    }

    [Fact]
    public async Task CancellingTheQuestion_SendsNothing()
    {
        var view = Render();
        var dialogs = Render<RadzenDialog>();

        await SwitchAsync(view, 0, true);
        await SwitchAsync(view, 1, true);
        await WriteAsync(view);
        await view.InvokeAsync(() => SendButton(view).Click());
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Cancel").Click());

        _messages.Verify(x => x.SendAsync(It.IsAny<UserMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ASelectionReachingNobody_SaysSo_AndCannotBeSent()
    {
        var view = Render(list: (_, _) => new UserMessageAddresseePage { Items = [], Total = 0 });

        await SwitchAsync(view, 0, true);
        await WriteAsync(view);

        Assert.Contains(UserMessageComposeViewText.NoRecipients.Default, view.Find("[data-no-recipients]").TextContent);
        Assert.True(SendButton(view).HasAttribute("disabled"));
    }

    [Fact]
    public async Task ThePreview_ShowsTheMailAsTheServiceRendersIt_InASandboxedFrame()
    {
        var view = Render();
        var dialogs = Render<RadzenDialog>();
        await SwitchAsync(view, 0, true);
        await SwitchAsync(view, 1, true);
        await WriteAsync(view);

        await view.InvokeAsync(() => view.Find("[data-preview-mail]").Click());

        _messages.Verify(x => x.PreviewMailAsync(It.Is<UserMessage>(m => m.Category == "service" && m.Subject == "Planned maintenance" && m.BodyHtml == "<p>Sunday</p>" && m.Recipients.AllTeams), It.IsAny<CancellationToken>()), Times.Once);
        var frame = dialogs.Find("iframe[data-mail-frame]");
        Assert.Equal(string.Empty, frame.GetAttribute("sandbox"));
        Assert.Equal("<html><body><p>Framed</p></body></html>", frame.GetAttribute("srcdoc"));
        Assert.Contains("Each of the 4 people, by name", dialogs.Find("[data-preview-header='to']").TextContent);
        _messages.Verify(x => x.SendAsync(It.IsAny<UserMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task APreviewRefused_SaysWhy_AndOpensNothing()
    {
        var view = Render();
        var dialogs = Render<RadzenDialog>();
        _messages.Setup(x => x.PreviewMailAsync(It.IsAny<UserMessage>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("No such category."));

        await view.InvokeAsync(() => view.Find("[data-preview-mail]").Click());

        Assert.Empty(dialogs.FindAll("iframe"));
    }

    [Fact]
    public void EveryString_ComesFromTheCatalogue()
    {
        var provider = new Mock<IThargaTextProvider>();
        provider.Setup(x => x.GetAsync(It.IsAny<TextKey>())).ReturnsAsync((TextKey key) => key == UserMessageComposeViewText.AllTeams ? "Alla organisationer" : key.Default);
        var view = Render(texts: provider.Object);

        Assert.Contains("Alla organisationer", view.Markup);
    }
}
