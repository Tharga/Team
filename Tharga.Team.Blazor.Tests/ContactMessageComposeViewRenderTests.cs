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
/// Staff write to the contacts a host lists: the view shows the list as the service checks it in the chosen category —
/// each address once, who opted out marked and counted, rows the host left out marked with its reason, values that are no
/// address marked — then a category, subject, rich text and files, a preview of the mail, and a send that states the count
/// before it goes and hands the outcome back to the host.
/// </summary>
public class ContactMessageComposeViewRenderTests : BunitContext
{
    private static readonly ContactMailCategory Offers = new() { Key = "offers", Name = "Offers" };
    private static readonly ContactMailCategory Events = new() { Key = "events", Name = "Events" };

    private static readonly ContactAddressee[] Rows =
    [
        new() { Address = "kim@customer.example", Name = "Kim", Detail = "Customer Ltd", Reference = "c-1" },
        new() { Address = "lee@customer.example", Reference = "c-2" },
        new() { Address = "max@customer.example", Name = "Max", Reference = "c-3", ExcludedReason = "Already a customer" },
        new() { Address = "nope", Reference = "c-4" }
    ];

    private static ContactAddresseeList Checked(bool leeOptedOutOfAll = false) => ContactList(
        new ContactAddresseeCheck { Address = "kim@customer.example", Name = "Kim", Detail = "Customer Ltd", References = ["c-1"], Status = ContactAddresseeStatus.Reached },
        new ContactAddresseeCheck { Address = "lee@customer.example", References = ["c-2"], Status = ContactAddresseeStatus.OptedOut, OptedOutOfAll = leeOptedOutOfAll },
        new ContactAddresseeCheck { Address = "max@customer.example", Name = "Max", References = ["c-3"], Status = ContactAddresseeStatus.LeftOut, ExcludedReason = "Already a customer" },
        new ContactAddresseeCheck { Address = "nope", References = ["c-4"], Status = ContactAddresseeStatus.LeftOut, InvalidAddress = true });

    private static ContactAddresseeList ContactList(params ContactAddresseeCheck[] items) => new()
    {
        Items = items,
        Reached = items.Count(x => x.Status == ContactAddresseeStatus.Reached),
        OptedOut = items.Count(x => x.Status == ContactAddresseeStatus.OptedOut),
        LeftOut = items.Count(x => x.Status == ContactAddresseeStatus.LeftOut)
    };

    private readonly Mock<IContactMessageService> _contacts = new();

    private IRenderedComponent<ContactMessageComposeView> Render(IReadOnlyList<ContactAddressee> rows = null, Action<SentContactMessage> sent = null, Func<string, ContactAddresseeList> list = null, int pageSize = 50, IThargaTextProvider texts = null)
    {
        list ??= category => Checked(category == "events");

        _contacts.Setup(x => x.GetChoicesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ContactMessageChoices
        {
            Categories = [Offers, Events],
            Limits = new UserMessageLimits { MaxAttachmentCount = 3, MaxAttachmentBytes = 1000, MaxBodyLength = 5000 },
            MaxAddressees = 100
        });
        _contacts.Setup(x => x.GetAddresseesAsync(It.IsAny<ContactAddressee[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContactAddressee[] _, string category, CancellationToken _) => list(category));
        _contacts.Setup(x => x.SendAsync(It.IsAny<ContactMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContactMessage m, CancellationToken _) => new SentContactMessage
            {
                Id = "m-1",
                Category = m.Category,
                Subject = m.Subject,
                SentAt = DateTime.UtcNow,
                RecipientCount = 1,
                OptedOutCount = 1,
                LeftOutCount = 2,
                Outcomes = new UserMailOutcomes { Queued = 1 },
                Recipients = [new ContactMessageRecipient { Address = "kim@customer.example", References = ["c-1"], Status = ContactRecipientStatus.Queued, ChangedAt = DateTime.UtcNow }]
            });
        _contacts.Setup(x => x.PreviewMailAsync(It.IsAny<ContactMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContactMessage m, CancellationToken _) => new MailPreview { From = "Example <hello@example.com>", Subject = m.Subject, Html = "<html><body><p>Framed</p></body></html>", Text = "Framed" });

        Services.AddSingleton(_contacts.Object);
        Services.AddSingleton(texts ?? new DefaultThargaTextProvider());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<ContactMessageComposeView>(p => p
            .Add(x => x.Addressees, rows ?? Rows)
            .Add(x => x.MessageSent, sent ?? (_ => { }))
            .Add(x => x.RecipientPageSize, pageSize));
    }

    private static async Task WriteAsync(IRenderedComponent<ContactMessageComposeView> view)
    {
        var subject = view.FindComponents<RadzenTextBox>().Single(x => x.Instance.Attributes?.ContainsKey("data-subject") == true);
        await view.InvokeAsync(() => subject.Instance.ValueChanged.InvokeAsync("Something for you"));
        await view.InvokeAsync(() => view.FindComponent<RichTextEditor>().Instance.ValueChanged.InvokeAsync("<p>An offer</p>"));
    }

    private static AngleSharp.Dom.IElement SendButton(IRenderedComponent<ContactMessageComposeView> view) => view.Find("[data-send]");

    [Fact]
    public void AReaderWithoutTheScope_IsToldSo()
    {
        _contacts.Setup(x => x.GetChoicesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        Services.AddSingleton(_contacts.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();

        var view = Render<ContactMessageComposeView>(p => p.Add(x => x.Addressees, Rows));

        Assert.Contains(ContactMessageComposeViewText.NotPermitted.Default, view.Markup);
        Assert.Empty(view.FindAll("[data-contact-compose]"));
        _contacts.Verify(x => x.GetAddresseesAsync(It.IsAny<ContactAddressee[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TheHostsList_IsCheckedInTheFirstCategory_AndListedWithItsCounts()
    {
        var view = Render();

        _contacts.Verify(x => x.GetAddresseesAsync(It.Is<ContactAddressee[]>(a => a.Length == 4 && a[0].Reference == "c-1" && a[2].ExcludedReason == "Already a customer"), "offers", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(["kim@customer.example", "lee@customer.example", "max@customer.example", "nope"], view.FindAll("[data-addressee]").Select(x => x.GetAttribute("data-addressee")));
        Assert.Equal("1", view.Find("[data-reaches]").GetAttribute("data-reaches"));
        Assert.Contains("Reaches 1 addresses.", view.Markup);
        Assert.Contains("1 have opted out and will not receive it.", view.Find("[data-opted-out-count]").TextContent);
        Assert.Contains("2 are left out.", view.Find("[data-left-out-count]").TextContent);
    }

    [Fact]
    public void ARow_ShowsItsNameAddressAndDetail()
    {
        var view = Render();

        var kim = view.Find("[data-addressee='kim@customer.example']");
        Assert.Contains("Kim", kim.TextContent);
        Assert.Equal("kim@customer.example", kim.QuerySelector("[data-addressee-address]")!.TextContent.Trim());
        Assert.Equal("Customer Ltd", kim.QuerySelector("[data-addressee-detail]")!.TextContent.Trim());
        Assert.Null(kim.QuerySelector("[data-opted-out]"));
        Assert.Null(kim.QuerySelector("[data-left-out]"));
    }

    [Fact]
    public async Task AnAddressThatOptedOut_IsMarked_WithWhatItOptedOutOf()
    {
        var view = Render();

        var badge = view.Find("[data-addressee='lee@customer.example'] [data-opted-out]");
        Assert.Contains(ContactMessageComposeViewText.OptedOut.Default, badge.TextContent);
        Assert.Equal("Has opted out of Offers.", badge.GetAttribute("title"));

        await view.InvokeAsync(() => view.FindComponent<RadzenDropDown<string>>().Instance.ValueChanged.InvokeAsync("events"));

        Assert.Equal(ContactMessageComposeViewText.OptedOutOfAllDetail.Default, view.Find("[data-addressee='lee@customer.example'] [data-opted-out]").GetAttribute("title"));
        _contacts.Verify(x => x.GetAddresseesAsync(It.IsAny<ContactAddressee[]>(), "events", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ARowTheHostLeftOut_SaysWhy_AndAValueThatIsNoAddress_SaysSo()
    {
        var view = Render();

        Assert.Equal("Left out: Already a customer", view.Find("[data-addressee='max@customer.example'] [data-left-out]").TextContent.Trim());
        Assert.Equal(ContactMessageComposeViewText.InvalidAddress.Default, view.Find("[data-addressee='nope'] [data-left-out]").TextContent.Trim());
    }

    [Fact]
    public void ANewListFromTheHost_IsCheckedAgain()
    {
        var view = Render();
        ContactAddressee[] next = [new() { Address = "zoe@customer.example" }];

        view.Render(p => p.Add(x => x.Addressees, next));

        _contacts.Verify(x => x.GetAddresseesAsync(It.Is<ContactAddressee[]>(a => a.Length == 1 && a[0].Address == "zoe@customer.example"), "offers", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AnEmptyList_SaysNobodyIsListed_AndCannotBeSent()
    {
        var view = Render(rows: [], list: _ => ContactList());

        Assert.Contains(ContactMessageComposeViewText.NoAddressees.Default, view.Find("[data-no-addressees]").TextContent);
        Assert.True(SendButton(view).HasAttribute("disabled"));
    }

    [Fact]
    public void AListTheServiceRefuses_SaysWhy()
    {
        _contacts.Setup(x => x.GetAddresseesAsync(It.IsAny<ContactAddressee[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Too many."));
        Services.AddSingleton(_contacts.Object);
        _contacts.Setup(x => x.GetChoicesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ContactMessageChoices { Categories = [Offers], Limits = new UserMessageLimits { MaxAttachmentCount = 1, MaxAttachmentBytes = 1, MaxBodyLength = 1 } });
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render<ContactMessageComposeView>(p => p.Add(x => x.Addressees, Rows));

        Assert.Equal("The list cannot be checked. Too many.", view.Find("[data-list-failed]").TextContent.Trim());
        Assert.True(SendButton(view).HasAttribute("disabled"));
    }

    [Fact]
    public async Task ALongList_IsShownAPageAtATime()
    {
        var items = Enumerable.Range(0, 7).Select(i => new ContactAddresseeCheck { Address = $"p{i}@customer.example", Status = ContactAddresseeStatus.Reached }).ToArray();
        var view = Render(list: _ => ContactList(items), pageSize: 5);

        Assert.Equal(5, view.FindAll("[data-addressee]").Count);

        await view.InvokeAsync(() => view.Find("[data-more-addressees]").Click());

        Assert.Equal(7, view.FindAll("[data-addressee]").Count);
        Assert.Empty(view.FindAll("[data-more-addressees]"));
    }

    [Fact]
    public void TheCategories_AreTheContactOnes_ByName_AndTheFilesAreHeldToTheLimits()
    {
        var view = Render();

        var dropDown = view.FindComponent<RadzenDropDown<string>>();
        Assert.Equal("offers", dropDown.Instance.Value);
        Assert.Equal(2, ((IEnumerable<object>)dropDown.Instance.Data).Count());
        var limits = view.FindComponent<SupportAttachmentPicker>().Instance.Limits;
        Assert.Equal((3, 1000L, 1000L), (limits.MaxCount, limits.MaxFileBytes, limits.MaxTotalBytes));
    }

    [Fact]
    public async Task Sending_AsksFirst_StatingTheCount_ThenSendsTheHostsRows_AndHandsTheOutcomeBack()
    {
        SentContactMessage raised = null;
        var view = Render(sent: x => raised = x);
        var dialogs = Render<RadzenDialog>();
        await WriteAsync(view);
        Assert.False(SendButton(view).HasAttribute("disabled"));

        await view.InvokeAsync(() => SendButton(view).Click());

        Assert.Contains("Send \"Something for you\" to 1 addresses?", dialogs.Markup);
        Assert.Contains("1 have opted out and will not receive it.", dialogs.Markup);
        _contacts.Verify(x => x.SendAsync(It.IsAny<ContactMessage>(), It.IsAny<CancellationToken>()), Times.Never);

        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Send").Click());

        _contacts.Verify(x => x.SendAsync(It.Is<ContactMessage>(m =>
            m.Addressees.Length == 4 && m.Addressees[0].Reference == "c-1" && m.Addressees[2].ExcludedReason == "Already a customer"
            && m.Category == "offers" && m.Subject == "Something for you" && m.BodyHtml == "<p>An offer</p>"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("m-1", raised?.Id);
        Assert.Equal(["c-1"], raised.Recipients.Single().References);
    }

    [Fact]
    public async Task CancellingTheQuestion_SendsNothing()
    {
        var view = Render();
        var dialogs = Render<RadzenDialog>();
        await WriteAsync(view);

        await view.InvokeAsync(() => SendButton(view).Click());
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Cancel").Click());

        _contacts.Verify(x => x.SendAsync(It.IsAny<ContactMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AListNobodyOnCanBeMailed_CannotBeSent()
    {
        var view = Render(list: _ => ContactList(new ContactAddresseeCheck { Address = "lee@customer.example", Status = ContactAddresseeStatus.OptedOut }));
        await WriteAsync(view);

        Assert.True(SendButton(view).HasAttribute("disabled"));
    }

    [Fact]
    public async Task ThePreview_ShowsTheMailAsTheServiceRendersIt_InASandboxedFrame()
    {
        var view = Render();
        var dialogs = Render<RadzenDialog>();
        await WriteAsync(view);

        await view.InvokeAsync(() => view.Find("[data-preview-mail]").Click());

        _contacts.Verify(x => x.PreviewMailAsync(It.Is<ContactMessage>(m => m.Category == "offers" && m.Subject == "Something for you" && m.BodyHtml == "<p>An offer</p>"), It.IsAny<CancellationToken>()), Times.Once);
        var frame = dialogs.Find("iframe[data-mail-frame]");
        Assert.Equal(string.Empty, frame.GetAttribute("sandbox"));
        Assert.Equal("<html><body><p>Framed</p></body></html>", frame.GetAttribute("srcdoc"));
        _contacts.Verify(x => x.SendAsync(It.IsAny<ContactMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void EveryString_ComesFromTheCatalogue()
    {
        var provider = new Mock<IThargaTextProvider>();
        provider.Setup(x => x.GetAsync(It.IsAny<TextKey>())).ReturnsAsync((TextKey key) => key == ContactMessageComposeViewText.RecipientList ? "Mottagare" : key.Default);

        var view = Render(texts: provider.Object);

        Assert.Contains("Mottagare", view.Markup);
    }
}
