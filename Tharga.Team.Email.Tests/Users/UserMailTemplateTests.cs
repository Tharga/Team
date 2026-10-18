using Microsoft.Extensions.Options;
using Tharga.Team.Email.Invitations;
using Tharga.Team.Email.Users;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>Mail to users in the host's template: messages, notices and invitations all go through the one composer, the footer lands in the template, a text-only mail gets an HTML part, and the text alternative is what it always was.</summary>
public class UserMailTemplateTests
{
    private const string Frame = "<html><body><h1>{{subject}}</h1>{{content}}<div class=\"foot\">{{footer}}</div></body></html>";

    private static MailTemplate Template(string html = Frame) => new(Options.Create(new MailTemplateOptions { Html = html }));

    [Fact]
    public async Task AnOptionalCategorysMail_IsFramed_WithTheFooterInItsPlace_AndTheTextUnchanged()
    {
        var plain = new UserMailFixture();
        await plain.QueueAsync();
        await plain.Dispatcher().SendNextAsync("me", TestContext.Current.CancellationToken);

        var framed = new UserMailFixture { Template = Template() };
        await framed.QueueAsync();
        await framed.Dispatcher().SendNextAsync("me", TestContext.Current.CancellationToken);

        var sent = Assert.Single(framed.Mail.Sent);
        Assert.StartsWith("<html><body><h1>Hello</h1><p>Something <b>new</b>.</p><div class=\"foot\"><p>", sent.BodyHtml);
        Assert.Contains(sent.UnsubscribeUrl.Replace("&", "&amp;"), sent.BodyHtml);
        Assert.DoesNotContain("<hr>", sent.BodyHtml);
        Assert.Equal(WithoutToken(Assert.Single(plain.Mail.Sent).Body), WithoutToken(sent.Body));
    }

    [Fact]
    public async Task ARequiredCategorysMail_IsFramed_WithAnEmptyFooter()
    {
        var fixture = new UserMailFixture { Template = Template() };
        await fixture.QueueAsync(UserMailCategories.ServiceKey);

        await fixture.Dispatcher().SendNextAsync("me", TestContext.Current.CancellationToken);

        Assert.Equal("<html><body><h1>Hello</h1><p>Something <b>new</b>.</p><div class=\"foot\"></div></body></html>", Assert.Single(fixture.Mail.Sent).BodyHtml);
    }

    [Fact]
    public async Task ATextOnlyMail_GetsAnHtmlPart_OnlyWhenATemplateIsSet()
    {
        var plain = new UserMailFixture();
        await plain.Queue.QueueAsync(new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = UserMailCategories.ServiceKey, Subject = "S", Body = "Line one.\n\nLine <two>." });
        await plain.Dispatcher().SendNextAsync("me", TestContext.Current.CancellationToken);

        var framed = new UserMailFixture { Template = Template() };
        await framed.Queue.QueueAsync(new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = UserMailCategories.ServiceKey, Subject = "S", Body = "Line one.\n\nLine <two>." });
        await framed.Dispatcher().SendNextAsync("me", TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(plain.Mail.Sent).BodyHtml);
        var sent = Assert.Single(framed.Mail.Sent);
        Assert.Equal("<html><body><h1>S</h1><p>Line one.</p><p>Line &lt;two&gt;.</p><div class=\"foot\"></div></body></html>", sent.BodyHtml);
        Assert.Equal("Line one.\n\nLine <two>.", sent.Body);
    }

    [Fact]
    public async Task AnInvitation_SentAsMailToUsers_IsFramed()
    {
        var fixture = new UserMailFixture { Template = Template() };
        var queue = new UserMailQueue(fixture.Categories, fixture.Outbox, fixture.Wrapped, fixture.Time, fixture.Dispatcher());
        var sender = new UserMailInvitationSender(queue, new UserMailText(), fixture.Wrapped);

        await sender.SendInviteAsync(new TeamInviteMail { RecipientEmail = "new@example.org", RecipientName = "New", InviteLink = "https://app.example.com/invite/abc", TeamKey = "t", TeamName = "Alpha" });

        var sent = Assert.Single(fixture.Mail.Sent);
        Assert.StartsWith("<html><body><h1>", sent.BodyHtml);
        Assert.Contains("<a href=\"https://app.example.com/invite/abc\">https://app.example.com/invite/abc</a>", sent.BodyHtml);
    }

    [Fact]
    public void AnInvitation_SentWithoutMailToUsers_IsFramed_OnlyWhenATemplateIsSet()
    {
        var policy = Substitute.For<IOutboundMailPolicy>();
        policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(x => new OutboundMailDecision(OutboundMailAction.Deliver, x.ArgAt<string>(0), x.ArgAt<string>(1)));
        var settings = Options.Create(new EmailOptions { SmtpHost = "smtp.example.com", FromAddress = "hello@example.com" });

        var plain = new InvitationMailSender(settings, policy).CreateMail("new@example.org", "New", "https://example.com/i", "Alpha");
        var framed = new InvitationMailSender(settings, policy, template: Template("<div>{{content}}</div>")).CreateMail("new@example.org", "New", "https://example.com/i", "Alpha");

        Assert.Null(plain.Value.BodyHtml);
        Assert.StartsWith("<div><p>Hi New,</p>", framed.Value.BodyHtml);
        Assert.Contains("<a href=\"https://example.com/i\">https://example.com/i</a>", framed.Value.BodyHtml);
        Assert.Equal(plain.Value.Body, framed.Value.Body);
    }

    private static string WithoutToken(string body) => System.Text.RegularExpressions.Regex.Replace(body ?? string.Empty, "token=[^\\s\"&]+", "token=*");
}
