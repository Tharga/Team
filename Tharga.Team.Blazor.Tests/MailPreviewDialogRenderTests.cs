using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The preview of a mail: the headers the recipient sees, then the body in a frame sandboxed with no permissions, so
/// the host template's styles stay inside it and nothing in it runs; a text-only mail shown as text.
/// </summary>
public class MailPreviewDialogRenderTests : BunitContext
{
    private const string Html = "<html><head><style>body { color: red; }</style></head><body><p>Hello <b>there</b></p><script>alert(1)</script></body></html>";

    private IRenderedComponent<MailPreviewDialog> Render(MailPreview preview, string[] files = null, int? recipients = null)
    {
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();

        return Render<MailPreviewDialog>(p =>
        {
            p.Add(x => x.Preview, preview);
            p.Add(x => x.FileNames, files ?? []);
            p.Add(x => x.RecipientCount, recipients);
        });
    }

    [Fact]
    public void TheBody_IsShownInAFrame_SandboxedWithNoPermissions()
    {
        var view = Render(new MailPreview { Subject = "S", Html = Html, Text = "Hello there" });

        var frame = view.Find("iframe[data-mail-frame]");
        Assert.True(frame.HasAttribute("sandbox"));
        Assert.Equal(string.Empty, frame.GetAttribute("sandbox"));
        Assert.Equal(Html, frame.GetAttribute("srcdoc"));
        Assert.Equal(MailPreviewText.Frame.Default, frame.GetAttribute("title"));
        Assert.Empty(view.FindAll("script"));
        Assert.Empty(view.FindAll("[data-mail-text]"));
    }

    [Fact]
    public void TheHeaders_AreTheOnesTheRecipientSees()
    {
        var view = Render(new MailPreview { From = "Example <hello@example.com>", ReplyTo = "support@example.com", To = "user@customer.example", Subject = "Re: Export", Html = "<p>x</p>" }, ["plan.pdf", "chart.png"]);

        Assert.Contains("Example <hello@example.com>", view.Find("[data-preview-header='from']").TextContent);
        Assert.Contains("user@customer.example", view.Find("[data-preview-header='to']").TextContent);
        Assert.Contains("support@example.com", view.Find("[data-preview-header='reply-to']").TextContent);
        Assert.Contains("Re: Export", view.Find("[data-preview-header='subject']").TextContent);
        Assert.Contains("plan.pdf, chart.png", view.Find("[data-preview-header='files']").TextContent);
    }

    [Fact]
    public void AMailToMany_SaysHowManyInPlaceOfAnAddress()
    {
        var view = Render(new MailPreview { Subject = "S", Html = "<p>x</p>" }, recipients: 12);

        Assert.Contains("Each of the 12 people, by name", view.Find("[data-preview-header='to']").TextContent);
        Assert.Empty(view.FindAll("[data-preview-header='reply-to']"));
    }

    [Fact]
    public void ATextOnlyMail_IsShownAsText_NotAsMarkup()
    {
        var view = Render(new MailPreview { Subject = "S", Text = "Hi <b>there</b>\nBye" });

        Assert.Empty(view.FindAll("iframe"));
        Assert.Equal("Hi <b>there</b>\nBye", view.Find("[data-mail-text]").TextContent);
        Assert.Contains(MailPreviewText.TextOnly.Default, view.Find("[data-text-only]").TextContent);
    }

    [Fact]
    public async Task Opened_ItShowsTheMail_AndCloseClosesIt()
    {
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        var host = Render<Radzen.Blazor.RadzenDialog>();
        var dialogs = Services.GetRequiredService<DialogService>();

        Task shown = null;
        await host.InvokeAsync(() => { shown = MailPreviewDialog.ShowAsync(dialogs, "Preview", new MailPreview { Subject = "S", Html = "<p>x</p>" }, ["a.txt"], 3); });
        Assert.NotNull(host.Find("iframe[data-mail-frame]"));

        await host.InvokeAsync(() => host.Find("[data-close-preview]").Click());

        Assert.True(shown.IsCompleted);
    }
}
