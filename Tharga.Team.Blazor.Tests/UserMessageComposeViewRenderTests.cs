using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.Messages;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>Staff write a message: teams or all teams, who in them, a category, subject, rich text and files, the counts as they choose, and a send that states the count before it goes.</summary>
public class UserMessageComposeViewRenderTests : BunitContext
{
    private static readonly UserMailCategory News = new() { Key = "news", Name = "News" };

    private readonly Mock<IUserMessageService> _messages = new();

    private static UserMessageChoices Choices() => new()
    {
        Teams = [new UserMessageTeam { Key = "alpha", Name = "Alpha" }, new UserMessageTeam { Key = "beta", Name = "Beta" }],
        Users = [new UserMessageCandidate { UserKey = "anna", Name = "Anna", TeamKeys = ["alpha"] }, new UserMessageCandidate { UserKey = "dan", Name = "Dan", TeamKeys = ["beta"] }],
        Roles = ["Editor"],
        AccessLevels = [AccessLevel.Owner, AccessLevel.User],
        Categories = [UserMailCategories.Service, News],
        Limits = new UserMessageLimits { MaxAttachmentCount = 3, MaxAttachmentBytes = 1000, MaxBodyLength = 5000 }
    };

    private IRenderedComponent<UserMessageComposeView> Render(Action<SentUserMessage> sent = null, UserMessagePreview preview = null, IThargaTextProvider texts = null)
    {
        _messages.Setup(x => x.GetChoicesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Choices());
        _messages.Setup(x => x.PreviewAsync(It.IsAny<UserMessageRecipients>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(preview ?? new UserMessagePreview { Recipients = 4, LeftOut = 0 });
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
        await view.InvokeAsync(() => view.FindComponent<RadzenTextBox>().Instance.ValueChanged.InvokeAsync("Planned maintenance"));
        await view.InvokeAsync(() => view.FindComponent<RichTextEditor>().Instance.ValueChanged.InvokeAsync("<p>Sunday</p>"));
    }

    private static AngleSharp.Dom.IElement SendButton(IRenderedComponent<UserMessageComposeView> view) => view.Find("[data-send]");

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
    public void BeforeAnyTeamIsChosen_NothingIsCounted_AndNothingCanBeSent()
    {
        var view = Render();

        Assert.Contains(UserMessageComposeViewText.NoTeams.Default, view.Find("[data-no-teams]").TextContent);
        Assert.True(SendButton(view).HasAttribute("disabled"));
        _messages.Verify(x => x.PreviewAsync(It.IsAny<UserMessageRecipients>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task ChoosingATeam_CountsWhoTheSelectionReaches()
    {
        var view = Render();

        await SwitchAsync(view, 1, true);
        await ChooseTeamsAsync(view, "alpha");

        _messages.Verify(x => x.PreviewAsync(It.Is<UserMessageRecipients>(r => !r.AllTeams && r.TeamKeys.SequenceEqual(new[] { "alpha" }) && r.EveryMember), "service", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("4", view.Find("[data-reaches]").GetAttribute("data-reaches"));
        Assert.Contains("Reaches 4 people.", view.Markup);
    }

    [Fact]
    public async Task AllTeams_HidesTheTeamPicker_AndCountsAcrossEveryTeam()
    {
        var view = Render();

        await SwitchAsync(view, 0, true);

        Assert.Empty(view.FindAll("[data-teams]"));
        _messages.Verify(x => x.PreviewAsync(It.Is<UserMessageRecipients>(r => r.AllTeams && r.TeamKeys.Length == 0), "service", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ThoseWhoTurnedTheCategoryOff_AreCounted_NeverNamed()
    {
        var view = Render(preview: new UserMessagePreview { Recipients = 5, LeftOut = 2 });

        await view.InvokeAsync(() => view.FindComponent<RadzenDropDown<string>>().Instance.ValueChanged.InvokeAsync("news"));
        await SwitchAsync(view, 0, true);

        Assert.Contains("2 of them have turned off News and will not receive it.", view.Find("[data-left-out]").TextContent);
    }

    [Fact]
    public async Task UsersOffered_AreThoseInTheChosenTeams()
    {
        var view = Render();

        await ChooseTeamsAsync(view, "beta");

        var users = view.FindComponents<RadzenDropDown<IEnumerable<string>>>().Last();
        Assert.Equal(["Dan"], ((IEnumerable<object>)users.Instance.Data).Select(x => x.GetType().GetProperty("Text")!.GetValue(x)));
    }

    [Fact]
    public async Task Everyone_HidesTheFinerChoices()
    {
        var view = Render();

        Assert.NotEmpty(view.FindAll("[data-users]"));
        await SwitchAsync(view, 1, true);

        Assert.Empty(view.FindAll("[data-users]"));
        Assert.Empty(view.FindAll("[data-roles]"));
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
        await WriteAsync(view);
        await view.InvokeAsync(() => SendButton(view).Click());
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Cancel").Click());

        _messages.Verify(x => x.SendAsync(It.IsAny<UserMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ASelectionReachingNobody_CannotBeSent()
    {
        var view = Render(preview: new UserMessagePreview { Recipients = 0, LeftOut = 0 });

        await SwitchAsync(view, 0, true);
        await WriteAsync(view);

        Assert.True(SendButton(view).HasAttribute("disabled"));
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
