using Blazored.LocalStorage;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Messages;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>The log of messages to contacts: each with its category, reach, sender and time, and once opened how many stand where and every address with what became of it and why.</summary>
public class ContactMessageLogViewRenderTests : BunitContext
{
    private static readonly DateTime SentAt = new(2026, 10, 18, 9, 30, 0, DateTimeKind.Utc);

    private readonly Mock<IContactMessageService> _contacts = new();

    private static SentContactMessage Message(string id, string subject, UserMailOutcomes outcomes = null) => new()
    {
        Id = id,
        Category = "offers",
        Subject = subject,
        AttachmentCount = 1,
        SentByName = "Sam Staff",
        SentAt = SentAt,
        RecipientCount = 2,
        OptedOutCount = 1,
        LeftOutCount = 1,
        Outcomes = outcomes ?? new UserMailOutcomes { Sent = 2 }
    };

    private static ContactMessageRecipient Recipient(string address, ContactRecipientStatus status, string name = null, string reason = null, bool all = false, bool invalid = false) => new()
    {
        Address = address,
        Name = name,
        Detail = name == null ? null : "Customer Ltd",
        Status = status,
        Reason = reason,
        OptedOutOfAll = all,
        InvalidAddress = invalid,
        ChangedAt = SentAt.AddMinutes(2)
    };

    private IRenderedComponent<ContactMessageLogView> Render(params SentContactMessagePage[] pages)
    {
        _contacts.Setup(x => x.GetChoicesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ContactMessageChoices
        {
            Categories = [new ContactMailCategory { Key = "offers", Name = "Offers" }],
            Limits = new UserMessageLimits { MaxAttachmentCount = 1, MaxAttachmentBytes = 1, MaxBodyLength = 1 }
        });

        var sequence = _contacts.SetupSequence(x => x.GetSentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()));
        foreach (var page in pages) sequence = sequence.ReturnsAsync(page);

        Services.AddSingleton(_contacts.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("Server", true));

        RenderFragment<DateTime> time = value => builder => builder.AddContent(0, $"at {value:yyyy-MM-dd HH:mm}");

        return Render<ContactMessageLogView>(p => p.Add(x => x.TimeTemplate, time));
    }

    private static void Open(IRenderedComponent<ContactMessageLogView> view, string id)
        => view.Find($"[data-message='{id}'] [style*='cursor: pointer']").Click();

    private void Recipients(string messageId, ContactRecipientStatus? status, string cursor, ContactMessageRecipientPage page)
        => _contacts.Setup(x => x.GetRecipientsAsync(messageId, status, cursor, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(page);

    [Fact]
    public void AReaderWithoutTheScope_IsToldSo()
    {
        _contacts.Setup(x => x.GetChoicesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        Services.AddSingleton(_contacts.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();

        var view = Render<ContactMessageLogView>();

        Assert.Contains(ContactMessageLogViewText.NotPermitted.Default, view.Markup);
    }

    [Fact]
    public void NothingSent_SaysSo()
    {
        var view = Render(new SentContactMessagePage());

        Assert.Contains(ContactMessageLogViewText.Empty.Default, view.Find("[data-empty]").TextContent);
    }

    [Fact]
    public void EachMessage_ShowsItsCategoryReachSenderAndTime_AndItsDetailsAreReadOnlyWhenOpened()
    {
        var view = Render(new SentContactMessagePage { Items = [Message("m-1", "Something for you", new UserMailOutcomes { Sent = 1, Failed = 1, Bounced = 1 })] });

        Assert.Contains("Something for you", view.Find("[data-message='m-1']").TextContent);
        Assert.Equal("Offers", view.Find("[data-category='m-1']").TextContent.Trim());
        Assert.Contains("2 recipients", view.Find("[data-recipients='m-1']").TextContent);
        Assert.Contains("1 Failed", view.Find("[data-failed='m-1']").TextContent);
        Assert.Contains("1 Bounced", view.Find("[data-bounced='m-1']").TextContent);
        Assert.Contains("Sent by Sam Staff", view.Find("[data-sent-by='m-1']").TextContent);
        Assert.Contains("at 2026-10-18 09:30", view.Find("[data-time='m-1']").TextContent);
        _contacts.Verify(x => x.GetSentMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void AnOpenedMessage_ShowsHowManyStandWhere_AndEachAddressWithWhatBecameOfIt()
    {
        _contacts.Setup(x => x.GetSentMessageAsync("m-1", It.IsAny<CancellationToken>())).ReturnsAsync(Message("m-1", "Something for you", new UserMailOutcomes { Sent = 1, Skipped = 1 }));
        Recipients("m-1", null, null, new ContactMessageRecipientPage
        {
            Items =
            [
                Recipient("kim@customer.example", ContactRecipientStatus.Sent, "Kim"),
                Recipient("lee@customer.example", ContactRecipientStatus.OptedOut, all: true),
                Recipient("max@customer.example", ContactRecipientStatus.LeftOut, reason: "Already a customer"),
                Recipient("nope", ContactRecipientStatus.LeftOut, invalid: true)
            ]
        });
        var view = Render(new SentContactMessagePage { Items = [Message("m-1", "Something for you")] });

        Open(view, "m-1");

        var details = view.Find("[data-contact-details='m-1']");
        Assert.Contains("1 files attached", details.TextContent);
        Assert.Equal("1", details.QuerySelector("[data-outcome='sent']")!.TextContent.Trim()[..1]);
        Assert.Equal("2", details.QuerySelector("[data-outcome='opted-out']")!.TextContent.Trim()[..1]);
        Assert.Equal("1", details.QuerySelector("[data-outcome='left-out']")!.TextContent.Trim()[..1]);
        Assert.Equal(["kim@customer.example", "lee@customer.example", "max@customer.example", "nope"], details.QuerySelectorAll("[data-recipient]").Select(x => x.GetAttribute("data-recipient")));

        var kim = details.QuerySelector("[data-recipient='kim@customer.example']")!;
        Assert.Contains("Kim", kim.TextContent);
        Assert.Equal("kim@customer.example", kim.QuerySelector("[data-recipient-address]")!.TextContent.Trim());
        Assert.Equal("Customer Ltd", kim.QuerySelector("[data-recipient-detail]")!.TextContent.Trim());
        Assert.Equal("Sent", kim.QuerySelector("[data-recipient-status]")!.TextContent.Trim());
        Assert.Contains("at 2026-10-18 09:32", kim.QuerySelector("[data-recipient-time]")!.TextContent);
        Assert.Equal(ContactMessageLogViewText.OfEveryCategory.Default, details.QuerySelector("[data-recipient='lee@customer.example'] [data-recipient-reason]")!.TextContent.Trim());
        Assert.Equal("Already a customer", details.QuerySelector("[data-recipient='max@customer.example'] [data-recipient-reason]")!.TextContent.Trim());
        Assert.Equal(ContactMessageLogViewText.InvalidAddress.Default, details.QuerySelector("[data-recipient='nope'] [data-recipient-reason]")!.TextContent.Trim());
        Assert.Equal("Left out", details.QuerySelector("[data-recipient='nope'] [data-recipient-status]")!.TextContent.Trim());
    }

    [Fact]
    public async Task TheAddresses_AreFilteredByStatus_AndMoreAreReadByTheCursor()
    {
        _contacts.Setup(x => x.GetSentMessageAsync("m-1", It.IsAny<CancellationToken>())).ReturnsAsync(Message("m-1", "Something for you"));
        Recipients("m-1", null, null, new ContactMessageRecipientPage { Items = [Recipient("kim@customer.example", ContactRecipientStatus.Sent)], NextCursor = "q:1" });
        Recipients("m-1", null, "q:1", new ContactMessageRecipientPage { Items = [Recipient("max@customer.example", ContactRecipientStatus.LeftOut, reason: "Already a customer")] });
        Recipients("m-1", ContactRecipientStatus.LeftOut, null, new ContactMessageRecipientPage { Items = [Recipient("max@customer.example", ContactRecipientStatus.LeftOut, reason: "Already a customer")] });
        var view = Render(new SentContactMessagePage { Items = [Message("m-1", "Something for you")] });
        Open(view, "m-1");

        await view.InvokeAsync(() => view.Find("[data-more-recipients]").Click());
        Assert.Equal(["kim@customer.example", "max@customer.example"], view.FindAll("[data-recipient]").Select(x => x.GetAttribute("data-recipient")));
        Assert.Empty(view.FindAll("[data-more-recipients]"));

        var filter = view.FindComponent<Radzen.Blazor.RadzenDropDown<string>>();
        await view.InvokeAsync(() => filter.Instance.ValueChanged.InvokeAsync(nameof(ContactRecipientStatus.LeftOut)));

        view.WaitForAssertion(() => Assert.Equal(["max@customer.example"], view.FindAll("[data-recipient]").Select(x => x.GetAttribute("data-recipient"))));
        _contacts.Verify(x => x.GetRecipientsAsync("m-1", ContactRecipientStatus.LeftOut, null, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MoreMessages_AreReadByTheCursor_AndRefreshReadsFromTheNewest()
    {
        var view = Render(
            new SentContactMessagePage { Items = [Message("m-2", "Second")], NextCursor = "c-1" },
            new SentContactMessagePage { Items = [Message("m-1", "First")] },
            new SentContactMessagePage { Items = [Message("m-3", "Third"), Message("m-2", "Second")] });

        await view.InvokeAsync(() => view.Find("[data-show-more]").Click());
        Assert.Equal(["m-2", "m-1"], view.FindAll("[data-message]").Select(x => x.GetAttribute("data-message")));

        await view.Instance.RefreshAsync();

        view.WaitForAssertion(() => Assert.Equal(["m-3", "m-2"], view.FindAll("[data-message]").Select(x => x.GetAttribute("data-message"))));
    }
}
