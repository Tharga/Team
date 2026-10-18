using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// An answer mail speaks for support and names no staff member: no author heading, the host's signature after the answer
/// and before the quoted history, earlier support entries attributed to support's name in a localizable line with the
/// host's time zone, and the host's support frame around it.
/// </summary>
public class SupportAnswerSigningTests
{
    private const string OpeningId = "opening-1@example.org";
    private const string CaseId = "case-1";

    private static readonly DateTime First = new(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);

    private readonly IMailClient _mail = Substitute.For<IMailClient>();
    private readonly ISupportCaseStore _store = Substitute.For<ISupportCaseStore>();
    private OutboundMail _sent;

    public SupportAnswerSigningTests()
    {
        _mail.CanSend.Returns(true);
        _mail.SendAsync(Arg.Do<OutboundMail>(x => _sent = x), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("answer-1@example.org"));
    }

    [Fact]
    public async Task AnAnswer_CarriesNoAuthorHeading_InItsTextOrItsHtml()
    {
        await PostAsync(Channel([Customer(1, "Help")]), Answer("<p>Try <b>this</b>.</p>"));

        Assert.StartsWith("Try this.", _sent.Body);
        Assert.StartsWith("<p>Try <b>this</b>.</p>", _sent.BodyHtml);
        Assert.DoesNotContain("Sam", _sent.Body);
        Assert.DoesNotContain("Sam", _sent.BodyHtml);
    }

    [Fact]
    public async Task TheSignature_FollowsTheAnswer_BeforeTheQuotedHistory_InTheText()
    {
        await PostAsync(Channel([Customer(1, "Help")], new SupportAnswerOptions { Signature = ["Kind regards", "Example Support"] }), Answer(null));

        Assert.Equal("Try this.\n\nKind regards\nExample Support\n\nOn 2026-10-17 08:00 UTC, A User wrote:\n> Help", _sent.Body);
        Assert.Null(_sent.BodyHtml);
    }

    [Fact]
    public async Task TheSignature_IsOneEncodedParagraphOfItsOwnClass_BeforeTheQuotedHistory_InTheHtml()
    {
        await PostAsync(Channel([Customer(1, "Help")], new SupportAnswerOptions { Signature = ["Kind regards", "Example <Support> & co"] }), Answer("<p>Try <b>this</b>.</p>"));

        Assert.Equal("<p>Try <b>this</b>.</p><p class=\"signature\">Kind regards<br>Example &lt;Support&gt; &amp; co</p>" +
                     "<p>On 2026-10-17 08:00 UTC, A User wrote:</p><blockquote>Help</blockquote>", _sent.BodyHtml);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("|  ")]
    public async Task WithNoSignature_NothingIsAppended(string lines)
    {
        var signature = lines?.Split('|').Where(x => lines.Length > 0).ToArray();

        await PostAsync(Channel([], new SupportAnswerOptions { Signature = signature }), Answer("<p>Try <b>this</b>.</p>"));

        Assert.Equal("Try this.", _sent.Body);
        Assert.Equal("<p>Try <b>this</b>.</p>", _sent.BodyHtml);
    }

    [Fact]
    public async Task AnEarlierSupportEntry_IsAttributedToSupportsName_AndTheCustomersToTheirs()
    {
        var earlier = new[]
        {
            Support(3, "Have you tried again?") with { Kind = SupportMessageKind.Assistant, AsSupport = false, AuthorName = "Helper bot" },
            Support(2, "Which export?"),
            Customer(1, "Help")
        };

        await PostAsync(Channel(earlier, new SupportAnswerOptions { SupportName = "Example Support" }), Answer("<p>Fixed.</p>") with { Sequence = 4 });

        Assert.Contains("On 2026-10-17 08:02 UTC, Example Support wrote:\n> Have you tried again?", _sent.Body);
        Assert.Contains("On 2026-10-17 08:01 UTC, Example Support wrote:\n> Which export?", _sent.Body);
        Assert.Contains("On 2026-10-17 08:00 UTC, A User wrote:\n> Help", _sent.Body);
        Assert.Contains("<p>On 2026-10-17 08:01 UTC, Example Support wrote:</p>", _sent.BodyHtml);
        Assert.DoesNotContain("Sam", _sent.Body + _sent.BodyHtml);
        Assert.DoesNotContain("Helper bot", _sent.Body + _sent.BodyHtml);
    }

    [Fact]
    public async Task WithoutASupportName_SupportIsNamedByItsTextKey()
    {
        await PostAsync(Channel([Support(2, "Which export?"), Customer(1, "Help")]), Answer(null) with { Sequence = 3 });

        Assert.Contains("On 2026-10-17 08:01 UTC, Support wrote:", _sent.Body);
    }

    [Fact]
    public async Task TheAttributionAndTheSupportName_AreTranslatedThroughTheHostsTexts()
    {
        var texts = new Texts(new Dictionary<string, string>
        {
            [SupportMailTexts.QuoteAttributionKey] = "Den {0} skrev {1}:",
            [SupportMailTexts.SupportNameKey] = "Supporten"
        });

        await PostAsync(Channel([Support(2, "Which export?"), Customer(1, "Help")], texts: texts), Answer(null) with { Sequence = 3 });

        Assert.Contains("Den 2026-10-17 08:01 UTC skrev Supporten:\n> Which export?", _sent.Body);
        Assert.Contains("Den 2026-10-17 08:00 UTC skrev A User:\n> Help", _sent.Body);
    }

    [Fact]
    public async Task ATranslationThatDoesNotFormat_FallsBackToTheEnglishLine()
    {
        var texts = new Texts(new Dictionary<string, string> { [SupportMailTexts.QuoteAttributionKey] = "Den {0} skrev {2}:" });

        await PostAsync(Channel([Customer(1, "Help")], texts: texts), Answer(null));

        Assert.Contains("On 2026-10-17 08:00 UTC, A User wrote:", _sent.Body);
    }

    [Fact]
    public async Task InTheHostsTimeZone_TheQuotedDateIsLocal_AndDoesNotSayUtc()
    {
        await PostAsync(Channel([Customer(1, "Help")], new SupportAnswerOptions { TimeZoneId = "Europe/Stockholm" }), Answer("<p>Fixed.</p>"));

        Assert.Contains("On 2026-10-17 10:00, A User wrote:", _sent.Body);
        Assert.Contains("<p>On 2026-10-17 10:00, A User wrote:</p>", _sent.BodyHtml);
        Assert.DoesNotContain("UTC", _sent.Body);
    }

    [Fact]
    public void ATimeZoneTheSystemDoesNotKnow_IsRefusedAtRegistration()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddThargaSupportCases(o => o.Answers.TimeZoneId = "Nowhere/Never"));

        Assert.Contains(nameof(SupportAnswerOptions.TimeZoneId), refused.Message);
    }

    [Fact]
    public async Task AnAnswer_IsFramedInTheSupportTemplate_AndItsPreviewIsTheSame()
    {
        var template = Template(new MailTemplateOptions
        {
            Html = "<div class=\"users\">{{content}}<i>{{footer}}</i></div>",
            SupportHtml = "<section><h1>{{subject}}</h1>{{content}}<i>{{footer}}</i></section>"
        });
        var channel = Channel([Customer(1, "Help")], new SupportAnswerOptions { Signature = ["Example Support"] }, template: template);
        var answer = Answer("<p>Fixed.</p>");

        var preview = await channel.PreviewAsync(await _store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId), Binding(), answer, TestContext.Current.CancellationToken);
        await PostAsync(channel, answer);

        Assert.Equal("<section><h1>Re: Export is empty</h1><p>Fixed.</p><p class=\"signature\">Example Support</p>" +
                     "<p>On 2026-10-17 08:00 UTC, A User wrote:</p><blockquote>Help</blockquote><i></i></section>", _sent.BodyHtml);
        Assert.Equal(_sent.BodyHtml, preview.Html);
        Assert.Equal(_sent.Body, preview.Text);
    }

    [Fact]
    public async Task WithoutASupportTemplate_AnAnswerIsFramedInTheMailTemplate_AsBefore()
    {
        var channel = Channel([], template: Template(new MailTemplateOptions { Html = "<div class=\"users\">{{content}}</div>" }));

        await PostAsync(channel, Answer(null));

        Assert.Equal("<div class=\"users\"><p>Try this.</p></div>", _sent.BodyHtml);
    }

    [Fact]
    public void ASupportTemplateWithoutContent_IsRefused_AtRegistration_AndWhenBuilt()
    {
        var registration = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddThargaMailTemplate(o => o.SupportHtml = "<div>{{subject}}</div>"));
        Assert.Contains(nameof(MailTemplateOptions.SupportHtml), registration.Message);

        Assert.Throws<InvalidOperationException>(() => Template(new MailTemplateOptions { Html = "<div>{{content}}</div>", SupportHtml = "<div>none</div>" }));
    }

    [Fact]
    public async Task TheSupportRegistration_SignsAnswersWithTheHostsOptions()
    {
        var services = new ServiceCollection();
        services.AddThargaMailTemplate(o => o.SupportHtml = "<section>{{content}}</section>");
        services.AddThargaSupportCases(o =>
        {
            o.Email.FromAddress = "support@example.org";
            o.Email.Smtp.Host = "smtp.example.org";
            o.Answers.SupportName = "Example Support";
            o.Answers.Signature = ["Example Support"];
        });
        services.AddSingleton(_store);
        Arrange([Support(2, "Which export?"), Customer(1, "Help")]);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mail = scope.ServiceProvider.GetServices<ISupportChannel>().OfType<ISupportAnswerMail>().Single();

        var preview = await mail.PreviewAsync(await _store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId), Binding(), Answer("<p>Fixed.</p>") with { Sequence = 3 }, TestContext.Current.CancellationToken);

        Assert.StartsWith("<section><p>Fixed.</p><p class=\"signature\">Example Support</p><p>On 2026-10-17 08:01 UTC, Example Support wrote:</p>", preview.Html);
        Assert.Equal("Example Support", provider.GetRequiredService<IOptions<SupportCaseOptions>>().Value.Answers.SupportName);
    }

    private async Task PostAsync(ISupportChannel channel, SupportMessage answer)
    {
        var result = await channel.PostWithResultAsync(Binding(), answer, TestContext.Current.CancellationToken);
        Assert.True(result.Delivered, result.Error);
    }

    private EmailSupportChannel Channel(IReadOnlyList<SupportMessage> earlier, SupportAnswerOptions answers = null, ITextResolver texts = null, MailTemplate template = null)
    {
        Arrange(earlier);

        return new EmailSupportChannel(_mail, _store, Options.Create(new MailOptions { FromAddress = "support@example.org" }), new InMemorySupportAttachmentStore(),
            template: template, answers: answers, texts: texts);
    }

    private void Arrange(IReadOnlyList<SupportMessage> earlier)
    {
        _store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId, Arg.Any<CancellationToken>()).Returns(new SupportCase
        {
            Id = CaseId,
            TeamKey = "team-a",
            AuthorIdentity = null,
            AuthorName = "A User",
            Subject = "Export is empty",
            Status = SupportCaseStatus.Open,
            CreatedAt = First,
            MessageCount = earlier.Count + 1
        });
        _store.GetMessagesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new SupportMessagePage { Items = [.. earlier] });
    }

    private static MailTemplate Template(MailTemplateOptions options) => new(Options.Create(options));

    private static SupportChannelBinding Binding() => new() { ChannelType = SupportChannelType.Email, ExternalId = OpeningId, Address = "user@customer.example" };

    private static SupportMessage Customer(int sequence, string body) => new()
    {
        Sequence = sequence,
        Kind = SupportMessageKind.User,
        AuthorName = "A User",
        Body = body,
        SentAt = First.AddMinutes(sequence - 1),
        Source = SupportChannelType.Email,
        Delivery = SupportMessageDelivery.Sent
    };

    private static SupportMessage Support(int sequence, string body) => new()
    {
        Sequence = sequence,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "sub-sam",
        AuthorName = "Sam",
        Body = body,
        SentAt = First.AddMinutes(sequence - 1),
        AsSupport = true,
        Delivery = SupportMessageDelivery.Sent
    };

    private static SupportMessage Answer(string html) => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "sub-sam",
        AuthorName = "Sam",
        Body = html == null ? "Try this." : SafeText(html),
        BodyHtml = html,
        SentAt = First.AddMinutes(30),
        AsSupport = true
    };

    private static string SafeText(string html) => html switch
    {
        "<p>Try <b>this</b>.</p>" => "Try this.",
        "<p>Fixed.</p>" => "Fixed.",
        _ => html
    };

    private sealed class Texts(Dictionary<string, string> texts) : ITextResolver
    {
        public Task<string> GetAsync(string key, string englishDefault) => Task.FromResult(texts.GetValueOrDefault(key) ?? englishDefault);
    }
}
