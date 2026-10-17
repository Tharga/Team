using Blazored.LocalStorage;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Messages;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>The sent log: each message with its category, reach, sender and time, and once opened who it went to, what became of its mails and why any was not sent.</summary>
public class UserMessageLogViewRenderTests : BunitContext
{
    private static readonly DateTime SentAt = new(2026, 10, 17, 9, 30, 0, DateTimeKind.Utc);
    private static readonly UserMailCategory News = new() { Key = "news", Name = "News" };

    private readonly Mock<IUserMessageService> _messages = new();

    private static SentUserMessage Message(string id, string subject, UserMailOutcomes outcomes = null) => new()
    {
        Id = id,
        Recipients = new UserMessageRecipients { TeamKeys = ["alpha"], AccessLevels = [AccessLevel.Owner], Roles = ["Editor"], UserKeys = ["anna"] },
        Category = "news",
        Subject = subject,
        AttachmentCount = 2,
        SentByName = "Sam Staff",
        SentAt = SentAt,
        RecipientCount = 3,
        Outcomes = outcomes ?? new UserMailOutcomes { Sent = 3 },
        ChosenUsers = [new UserMessageCandidate { UserKey = "anna", Name = "Anna" }]
    };

    private IRenderedComponent<UserMessageLogView> Render(params SentUserMessagePage[] pages)
    {
        _messages.Setup(x => x.GetChoicesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new UserMessageChoices
        {
            Teams = [new UserMessageTeam { Key = "alpha", Name = "Alpha" }],
            Categories = [News],
            Limits = new UserMessageLimits { MaxAttachmentCount = 1, MaxAttachmentBytes = 1, MaxBodyLength = 1 }
        });

        var sequence = _messages.SetupSequence(x => x.GetSentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()));
        foreach (var page in pages) sequence = sequence.ReturnsAsync(page);

        Services.AddSingleton(_messages.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("Server", true));

        RenderFragment<DateTime> time = value => builder => builder.AddContent(0, $"at {value:yyyy-MM-dd HH:mm}");

        return Render<UserMessageLogView>(p => p.Add(x => x.TimeTemplate, time));
    }

    private static void Open(IRenderedComponent<UserMessageLogView> view, string id)
        => view.Find($"[data-message='{id}'] [style*='cursor: pointer']").Click();

    [Fact]
    public void EachMessage_ShowsItsCategoryReachSenderAndTime()
    {
        var view = Render(new SentUserMessagePage { Items = [Message("m-1", "Maintenance")] });

        Assert.Contains("Maintenance", view.Find("[data-message='m-1']").TextContent);
        Assert.Equal("News", view.Find("[data-category='m-1']").TextContent.Trim());
        Assert.Contains("3 recipients", view.Find("[data-recipients='m-1']").TextContent);
        Assert.Contains("Sent by Sam Staff", view.Find("[data-sent-by='m-1']").TextContent);
        Assert.Contains("at 2026-10-17 09:30", view.Find("[data-time='m-1']").TextContent);
        Assert.Empty(view.FindAll("[data-failed='m-1']"));
    }

    [Fact]
    public void AMessageWithFailedMail_SaysSoInItsHeader()
    {
        var view = Render(new SentUserMessagePage { Items = [Message("m-1", "Maintenance", new UserMailOutcomes { Sent = 2, Failed = 1 })] });

        Assert.Contains("1 Failed", view.Find("[data-failed='m-1']").TextContent);
    }

    [Fact]
    public void ADetail_IsReadOnlyWhenItsMessageIsOpened()
    {
        Render(new SentUserMessagePage { Items = [Message("m-1", "Maintenance")] });

        _messages.Verify(x => x.GetSentMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static UserMessageRecipient Recipient(string userKey, string name, UserMailStatus status, string address = null, string reason = null) => new()
    {
        UserKey = userKey,
        Name = name,
        Address = address,
        Status = status,
        Reason = reason,
        ChangedAt = SentAt.AddMinutes(2)
    };

    private void Recipients(string messageId, UserMailStatus? status, string cursor, UserMessageRecipientPage page)
        => _messages.Setup(x => x.GetRecipientsAsync(messageId, status, cursor, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(page);

    [Fact]
    public void AnOpenedMessage_ShowsWhoItWentTo_WhatBecameOfItsMails_AndEachRecipient()
    {
        _messages.Setup(x => x.GetSentMessageAsync("m-1", It.IsAny<CancellationToken>())).ReturnsAsync(Message("m-1", "Maintenance", new UserMailOutcomes { Sent = 1, Skipped = 1, Failed = 1 }));
        Recipients("m-1", null, null, new UserMessageRecipientPage
        {
            Items =
            [
                Recipient("anna", "Anna", UserMailStatus.Sent, "anna@example.org"),
                Recipient("bo", "Bo", UserMailStatus.Skipped, reason: "The recipient does not receive this category."),
                Recipient("cia", null, UserMailStatus.Failed, reason: "The user record holds no address.")
            ]
        });
        var view = Render(new SentUserMessagePage { Items = [Message("m-1", "Maintenance")] });

        Open(view, "m-1");

        var details = view.Find("[data-message-details='m-1']");
        var selection = details.QuerySelectorAll("[data-selection]").Select(x => x.TextContent.Trim()).ToArray();
        Assert.Equal(["Teams: Alpha", "Access levels: Owner", "Roles: Editor", "Users: Anna"], selection);
        Assert.Contains("2 files attached", details.TextContent);
        Assert.Equal("1", details.QuerySelector("[data-outcome='sent']")!.TextContent.Trim()[..1]);
        Assert.Equal("1", details.QuerySelector("[data-outcome='skipped']")!.TextContent.Trim()[..1]);
        Assert.Equal("1", details.QuerySelector("[data-outcome='failed']")!.TextContent.Trim()[..1]);

        Assert.Equal(["anna", "bo", "cia"], details.QuerySelectorAll("[data-recipient]").Select(x => x.GetAttribute("data-recipient")));
        var anna = details.QuerySelector("[data-recipient='anna']")!;
        Assert.Contains("Anna", anna.TextContent);
        Assert.Equal("anna@example.org", anna.QuerySelector("[data-recipient-address]")!.TextContent.Trim());
        Assert.Equal("Sent", anna.QuerySelector("[data-recipient-status]")!.TextContent.Trim());
        Assert.Contains("at 2026-10-17 09:32", anna.QuerySelector("[data-recipient-time]")!.TextContent);
        Assert.Equal("Turned off", details.QuerySelector("[data-recipient='bo'] [data-recipient-status]")!.TextContent.Trim());
        var cia = details.QuerySelector("[data-recipient='cia']")!;
        Assert.Contains("cia", cia.TextContent);
        Assert.Contains("The user record holds no address.", cia.QuerySelector("[data-recipient-reason]")!.TextContent);
        Assert.Null(cia.QuerySelector("[data-recipient-address]"));
        Assert.Empty(details.QuerySelectorAll("[data-more-recipients]"));
    }

    [Fact]
    public async Task TheRecipients_AreFilteredByStatus_ReadAgainFromTheFirst()
    {
        _messages.Setup(x => x.GetSentMessageAsync("m-1", It.IsAny<CancellationToken>())).ReturnsAsync(Message("m-1", "Maintenance"));
        Recipients("m-1", null, null, new UserMessageRecipientPage { Items = [Recipient("anna", "Anna", UserMailStatus.Sent), Recipient("cia", "Cia", UserMailStatus.Failed)] });
        Recipients("m-1", UserMailStatus.Failed, null, new UserMessageRecipientPage { Items = [Recipient("cia", "Cia", UserMailStatus.Failed)] });
        var view = Render(new SentUserMessagePage { Items = [Message("m-1", "Maintenance")] });
        Open(view, "m-1");

        var filter = view.FindComponent<Radzen.Blazor.RadzenDropDown<string>>();
        await view.InvokeAsync(() => filter.Instance.ValueChanged.InvokeAsync(nameof(UserMailStatus.Failed)));

        view.WaitForAssertion(() => Assert.Equal(["cia"], view.FindAll("[data-recipient]").Select(x => x.GetAttribute("data-recipient"))));
        _messages.Verify(x => x.GetRecipientsAsync("m-1", UserMailStatus.Failed, null, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MoreRecipients_AreReadByTheCursor()
    {
        _messages.Setup(x => x.GetSentMessageAsync("m-1", It.IsAny<CancellationToken>())).ReturnsAsync(Message("m-1", "Maintenance"));
        Recipients("m-1", null, null, new UserMessageRecipientPage { Items = [Recipient("anna", "Anna", UserMailStatus.Sent)], NextCursor = "r-1" });
        Recipients("m-1", null, "r-1", new UserMessageRecipientPage { Items = [Recipient("bo", "Bo", UserMailStatus.Queued)] });
        var view = Render(new SentUserMessagePage { Items = [Message("m-1", "Maintenance")] });
        Open(view, "m-1");

        await view.InvokeAsync(() => view.Find("[data-more-recipients]").Click());

        Assert.Equal(["anna", "bo"], view.FindAll("[data-recipient]").Select(x => x.GetAttribute("data-recipient")));
        Assert.Equal("Waiting", view.Find("[data-recipient='bo'] [data-recipient-status]").TextContent.Trim());
        Assert.Empty(view.FindAll("[data-more-recipients]"));
    }

    [Fact]
    public void BouncedMail_IsCountedInTheHeaderAndTheDetails_AndNamedOnTheRecipient()
    {
        var outcomes = new UserMailOutcomes { Sent = 2, Bounced = 1 };
        _messages.Setup(x => x.GetSentMessageAsync("m-1", It.IsAny<CancellationToken>())).ReturnsAsync(Message("m-1", "Maintenance", outcomes));
        Recipients("m-1", null, null, new UserMessageRecipientPage { Items = [Recipient("anna", "Anna", UserMailStatus.Bounced, "anna@example.org", "550 5.1.1 Recipient address rejected")] });
        var view = Render(new SentUserMessagePage { Items = [Message("m-1", "Maintenance", outcomes)] });

        Assert.Contains("1 Bounced", view.Find("[data-bounced='m-1']").TextContent);

        Open(view, "m-1");

        Assert.Equal("1", view.Find("[data-outcome='bounced']").TextContent.Trim()[..1]);
        Assert.Equal("Bounced", view.Find("[data-recipient='anna'] [data-recipient-status]").TextContent.Trim());
        Assert.Contains("550 5.1.1", view.Find("[data-recipient='anna'] [data-recipient-reason]").TextContent);
    }

    [Fact]
    public void AFilterNobodyMatches_SaysSo()
    {
        _messages.Setup(x => x.GetSentMessageAsync("m-1", It.IsAny<CancellationToken>())).ReturnsAsync(Message("m-1", "Maintenance"));
        Recipients("m-1", null, null, new UserMessageRecipientPage());
        var view = Render(new SentUserMessagePage { Items = [Message("m-1", "Maintenance")] });

        Open(view, "m-1");

        Assert.Contains(UserMessageLogViewText.NoRecipients.Default, view.Find("[data-no-recipients]").TextContent);
    }

    [Fact]
    public void AMessageToEveryoneInEveryTeam_SaysSo()
    {
        _messages.Setup(x => x.GetSentMessageAsync("m-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Message("m-1", "All") with { Recipients = new UserMessageRecipients { AllTeams = true, EveryMember = true } });
        var view = Render(new SentUserMessagePage { Items = [Message("m-1", "All")] });

        Open(view, "m-1");

        Assert.Equal(["All teams", "Everyone in the teams"], view.FindAll("[data-selection]").Select(x => x.TextContent.Trim()));
    }

    [Fact]
    public async Task ShowMore_ReadsTheNextPage()
    {
        var view = Render(
            new SentUserMessagePage { Items = [Message("m-2", "Second")], NextCursor = "c-1" },
            new SentUserMessagePage { Items = [Message("m-1", "First")] });

        await view.InvokeAsync(() => view.Find("[data-show-more]").Click());

        Assert.Equal(["m-2", "m-1"], view.FindAll("[data-message]").Select(x => x.GetAttribute("data-message")));
        _messages.Verify(x => x.GetSentAsync("c-1", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(view.FindAll("[data-show-more]"));
    }

    [Fact]
    public async Task Refreshing_ReadsTheLogAgainFromItsNewest()
    {
        var view = Render(
            new SentUserMessagePage { Items = [Message("m-1", "First")] },
            new SentUserMessagePage { Items = [Message("m-2", "Second"), Message("m-1", "First")] });

        await view.Instance.RefreshAsync();

        view.WaitForAssertion(() => Assert.Equal(["m-2", "m-1"], view.FindAll("[data-message]").Select(x => x.GetAttribute("data-message"))));
    }

    [Fact]
    public void AnEmptyLog_SaysSo()
    {
        var view = Render(new SentUserMessagePage());

        Assert.Contains(UserMessageLogViewText.Empty.Default, view.Find("[data-empty]").TextContent);
    }

    [Fact]
    public void AReaderWithoutTheScope_IsToldSo()
    {
        _messages.Setup(x => x.GetChoicesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        Services.AddSingleton(_messages.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();

        var view = Render<UserMessageLogView>();

        Assert.Contains(UserMessageLogViewText.NotPermitted.Default, view.Markup);
    }
}
