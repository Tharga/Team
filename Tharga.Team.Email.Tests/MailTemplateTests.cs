using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Tests;

/// <summary>The host's template around every HTML mail: the content placed and the rest filled once, escaped where it is text, a text-only mail given paragraphs, and no template meaning exactly what was sent before.</summary>
public class MailTemplateTests
{
    private const string Frame = "<html><head><title>{{subject}}</title></head><body><span class=\"pre\">{{preheader}}</span><main>{{content}}</main><footer>{{footer}}</footer></body></html>";

    private static MailTemplate Template(string html) => new(Options.Create(new MailTemplateOptions { Html = html }));

    [Fact]
    public void WithoutATemplate_TheHtmlIsSentAsItWas_WithTheFooterAfterARule()
    {
        Assert.Equal("<p>Hi</p>", MailTemplate.None.Render("S", "Hi", "<p>Hi</p>"));
        Assert.Equal("<p>Hi</p><hr><p>Why</p>", MailTemplate.None.Render("S", "Hi", "<p>Hi</p>", "<p>Why</p>"));
    }

    [Fact]
    public void WithoutATemplate_ATextOnlyMailStaysTextOnly()
    {
        Assert.Null(MailTemplate.None.Render("S", "Hi", null, "<p>Why</p>"));
        Assert.False(MailTemplate.None.IsSet);
    }

    [Fact]
    public void TheTemplate_TakesTheContent_TheEscapedSubject_TheFooter_AndThePreheader()
    {
        var html = Template(Frame).Render("Price < cost & more", "Hello there.", "<p>Hello <b>there</b>.</p>", "<p>Why <a href=\"https://x.example\">stop</a></p>");

        Assert.Equal("<html><head><title>Price &lt; cost &amp; more</title></head><body><span class=\"pre\">Hello there.</span>" +
                     "<main><p>Hello <b>there</b>.</p></main><footer><p>Why <a href=\"https://x.example\">stop</a></p></footer></body></html>", html);
    }

    [Fact]
    public void AFooterPlaceholderWithNoFooter_IsLeftEmpty()
    {
        Assert.Contains("<footer></footer>", Template(Frame).Render("S", "T", "<p>T</p>"));
    }

    [Fact]
    public void ATemplateWithNoFooterPlaceholder_StillCarriesTheFooter_AfterTheContent()
    {
        var html = Template("<div>{{content}}</div>").Render("S", "T", "<p>T</p>", "<p>Why</p>");

        Assert.Equal("<div><p>T</p><hr><p>Why</p></div>", html);
    }

    [Fact]
    public void ATextOnlyMail_GetsItsTextAsEscapedParagraphs_WithLineBreaksAndLinks()
    {
        var html = Template("<div>{{content}}</div>").Render("S", "Hi <you>,\r\nwelcome.\n\nOpen https://app.example.com/i?x=1&y=2.\n\n\nBye", null);

        Assert.Equal("<div><p>Hi &lt;you&gt;,<br>welcome.</p>" +
                     "<p>Open <a href=\"https://app.example.com/i?x=1&amp;y=2\">https://app.example.com/i?x=1&amp;y=2</a>.</p>" +
                     "<p>Bye</p></div>", html);
    }

    [Fact]
    public void APlaceholderInsideTheContent_IsLeftAsText()
    {
        var html = Template("<div>{{content}}</div><i>{{footer}}</i>").Render("S", "T", "<p>{{footer}} and {{subject}}</p>", "<b>F</b>");

        Assert.Equal("<div><p>{{footer}} and {{subject}}</p></div><i><b>F</b></i>", html);
    }

    [Theory]
    [InlineData("<div>{{ content }}</div>")]
    [InlineData("<div>{{Content}}</div>")]
    [InlineData("<div>{{CONTENT  }}</div>")]
    public void APlaceholder_IsFoundWhateverItsCaseAndSpacing(string template)
    {
        Assert.Equal("<div><p>T</p></div>", Template(template).Render("S", "T", "<p>T</p>"));
    }

    [Fact]
    public void AnUnknownPlaceholder_IsLeftAlone()
    {
        Assert.Equal("<div>{{name}}<p>T</p></div>", Template("<div>{{name}}{{content}}</div>").Render("S", "T", "<p>T</p>"));
    }

    [Fact]
    public void ALongPreheader_IsCutAtAWord_OnOneLine()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 60)).Replace("word word", "word\nword");

        var preheader = MailTemplate.Preheader(text);

        Assert.True(preheader.Length <= 141, preheader);
        Assert.EndsWith("word…", preheader);
        Assert.DoesNotContain('\n', preheader);
    }

    [Fact]
    public void ATemplateWithoutContent_IsRefused_AtRegistration_AndWhenBuilt()
    {
        var registration = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddThargaMailTemplate(o => o.Html = "<div>{{subject}}</div>"));
        Assert.Contains(MailTemplateOptions.ContentPlaceholder, registration.Message);

        Assert.Throws<InvalidOperationException>(() => Template("<div>no content</div>"));
    }

    [Fact]
    public void TheRegisteredRenderer_UsesTheRegisteredTemplate()
    {
        var services = new ServiceCollection().AddThargaMailTemplate(o => o.Html = "<div>{{content}}</div>");

        var template = services.BuildServiceProvider().GetRequiredService<MailTemplate>();

        Assert.True(template.IsSet);
        Assert.Equal("<div><p>T</p></div>", template.Render("S", "T", null));
    }

    [Fact]
    public void ASupportTemplate_FramesSupportAnswersAlone_AndWithoutOneTheyUseTheMailTemplate()
    {
        var both = new MailTemplate(Options.Create(new MailTemplateOptions { Html = "<div>{{content}}</div>", SupportHtml = "<section>{{content}}<i>{{footer}}</i></section>" }));
        var one = Template("<div>{{content}}</div>");

        Assert.Equal("<div><p>T</p></div>", both.Render("S", "T", "<p>T</p>"));
        Assert.Equal("<section><p>T</p><i></i></section>", both.Support.Render("S", "T", "<p>T</p>"));
        Assert.Same(one, one.Support);
        Assert.Same(MailTemplate.None, MailTemplate.None.Support);
    }

    [Fact]
    public void ASupportTemplateAlone_FramesSupportAnswers_AndLeavesOtherMailUnframed()
    {
        var template = new MailTemplate(Options.Create(new MailTemplateOptions { SupportHtml = "<section>{{content}}</section>" }));

        Assert.False(template.IsSet);
        Assert.True(template.Support.IsSet);
        Assert.Equal("<section><p>T</p></section>", template.Support.Render("S", "T", null));
    }

    [Fact]
    public void ASupportTemplateWithoutContent_IsRefused_NamingIt()
    {
        var registration = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddThargaMailTemplate(o => o.SupportHtml = "<div>{{subject}}</div>"));

        Assert.Contains(nameof(MailTemplateOptions.SupportHtml), registration.Message);
        Assert.Throws<InvalidOperationException>(() => new MailTemplate(Options.Create(new MailTemplateOptions { SupportHtml = "<div></div>" })));
    }

    [Fact]
    public void AnEmptyTemplate_FramesNothing()
    {
        Assert.False(Template("   ").IsSet);
        Assert.Equal("<p>T</p>", Template(null).Render("S", "T", "<p>T</p>"));
    }
}
