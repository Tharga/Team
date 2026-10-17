using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A received mail's formatting is kept, sanitized and without its quoted thread, beside the text every other
/// reader uses; whenever keeping it would show more than the text does, it is dropped.
/// </summary>
public class EmailHtmlTests
{
    private const string TeamKey = "acme";
    private const string CaseId = "case-1";
    private const string Correspondent = "user@example.com";
    private const string ThreadId = "opening-1@example.org";

    [Fact]
    public async Task AnHtmlOnlyReply_KeepsItsFormatting_AndTheTextReadFromIt()
    {
        var (handler, store, _) = Build();

        await handler.HandleAsync(Reply() with { Body = "Any news?\n\nThanks", BodyHtml = "<p>Any <b>news</b>?</p><p>Thanks</p>" });

        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.BodyHtml == "<p>Any <b>news</b>?</p><p>Thanks</p>" && x.Body == "Any news?\n\nThanks"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AReplyWithTextAndHtml_KeepsTheSendersText_AndTheSanitizedHtml()
    {
        var (handler, store, _) = Build();

        await handler.HandleAsync(Reply() with { Body = "It *works* now.", BodyHtml = "<p onclick=\"x()\">It <b>works</b> now.<script>x()</script><img src=\"https://tracker.example/p.gif\"></p>" });

        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.BodyHtml == "<p>It <b>works</b> now.</p>" && x.Body == "It *works* now."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANewMailWithHtml_OpensACaseWhoseFirstEntryCarriesIt()
    {
        var (handler, store, _) = Build();
        SupportMessage first = null;
        await store.AddCaseAsync(Arg.Any<SupportCase>(), Arg.Do<SupportMessage>(x => first = x), Arg.Any<CancellationToken>());

        await handler.HandleAsync(NewThread() with { Body = "Steps:\n- Export", BodyHtml = "<p>Steps:</p><ol><li>Export</li></ol>" });

        Assert.Equal("<p>Steps:</p><ol><li>Export</li></ol>", first.BodyHtml);
    }

    [Fact]
    public async Task AnInlineImage_IsKeptAsTheReferenceToItsStoredFile()
    {
        var (handler, store, files) = Build();

        await handler.HandleAsync(Reply() with
        {
            Body = "Look:",
            BodyHtml = "<p>Look: <img src=\"cid:shot@example.com\" alt=\"screenshot\"></p>",
            Attachments = [new InboundAttachment("shot.png", "image/png", [1, 2, 3], true, "shot@example.com")]
        });

        Assert.Equal("shot@example.com", Assert.Single(files.Files).Attachment.ContentId);
        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.BodyHtml == "<p>Look: <img src=\"cid:shot@example.com\" alt=\"screenshot\"></p>" && x.Attachments.Single().ContentId == "shot@example.com"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheQuotedThread_IsCutFromTheHtml_AsFromTheText()
    {
        var (handler, store, _) = Build();

        await handler.HandleAsync(Reply() with
        {
            Body = "Fixed, thanks.\n\nOn Fri, 17 Oct 2026 at 09:00, Support <support@example.org> wrote:\n> Try again.",
            BodyHtml = "<div dir=\"ltr\">Fixed, <b>thanks</b>.</div><br><div class=\"gmail_quote\"><div class=\"gmail_attr\">On Fri, 17 Oct 2026 at 09:00, Support wrote:</div><blockquote>Try <i>again</i>.</blockquote></div>"
        });

        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.BodyHtml == "<div>Fixed, <b>thanks</b>.</div>" && x.Body == "Fixed, thanks."),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Showing every earlier message again under each reply is the failure the text trimming exists to prevent.</summary>
    [Fact]
    public async Task AQuoteTheTextLost_ButTheHtmlStillCarriesInAShapeNotRecognised_DropsTheHtml()
    {
        var (handler, store, _) = Build();

        await handler.HandleAsync(Reply() with
        {
            Body = "Fixed.\n\n> Try again.",
            BodyHtml = "<p><b>Fixed.</b></p><div class=\"some-other-client-quote\"><p>Try again.</p></div>"
        });

        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.BodyHtml == null && x.Body == "Fixed."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HtmlWithoutFormatting_IsNotKept()
    {
        var (handler, store, _) = Build();

        await handler.HandleAsync(Reply() with { Body = "Hi\nThanks", BodyHtml = "<div dir=\"ltr\">Hi<div>Thanks</div></div>" });

        await store.Received(1).AppendMessageAsync(TeamKey, CaseId, Arg.Is<SupportMessage>(x => x.BodyHtml == null && x.Body == "Hi\nThanks"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HtmlTooLongToKeep_IsDropped_AndTheTextKept()
    {
        var (handler, store, _) = Build();
        var html = string.Concat(Enumerable.Repeat("<p><b>x</b></p>", SupportCaseLimits.MaxMessageHtmlLength / 14 + 1));

        await handler.HandleAsync(Reply() with { Body = "x", BodyHtml = html });

        await store.Received(1).AppendMessageAsync(TeamKey, CaseId, Arg.Is<SupportMessage>(x => x.BodyHtml == null && x.Body == "x"), Arg.Any<CancellationToken>());
    }

    /// <summary>A reader shown the HTML never sees the text, so a file left out has to be said in both.</summary>
    [Fact]
    public async Task AFileNotKept_IsNotedInTheHtml_AsInTheText()
    {
        var (handler, store, _) = Build(new MailOptions { MaxAttachmentBytes = 2 });

        await handler.HandleAsync(Reply() with
        {
            Body = "See attached.",
            BodyHtml = "<p>See <b>attached</b>.</p>",
            Attachments = [new InboundAttachment("huge.zip", "application/zip", [1, 2, 3, 4])]
        });

        await store.Received(1).AppendMessageAsync(TeamKey, CaseId,
            Arg.Is<SupportMessage>(x => x.BodyHtml.StartsWith("<p>See <b>attached</b>.</p><p>[huge.zip was not stored") && x.Body.Contains("huge.zip was not stored")),
            Arg.Any<CancellationToken>());
    }

    private static InboundMail Reply() => new(
        MessageId: "reply-1@example.com",
        From: Correspondent,
        DeliveredTo: ["support@example.org"],
        Subject: "Re: Export is empty",
        Body: "See attached.",
        SentAt: new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero),
        InReplyTo: ThreadId,
        References: [ThreadId]);

    private static InboundMail NewThread() => Reply() with
    {
        MessageId = "first-1@example.com",
        From = "stranger@example.com",
        Subject = "Export is empty",
        InReplyTo = null,
        References = []
    };

    private static SupportCase Case() => new()
    {
        Id = CaseId,
        TeamKey = TeamKey,
        AuthorIdentity = "sub-1",
        AuthorName = "A User",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc),
        MessageCount = 1,
        Bindings = [new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = ThreadId, Address = Correspondent }]
    };

    private static (EmailEventHandler Handler, ISupportCaseStore Store, InMemorySupportAttachmentStore Files) Build(MailOptions options = null)
    {
        var store = Substitute.For<ISupportCaseStore>();
        store.GetCaseByBindingAsync(SupportChannelType.Email, ThreadId, Arg.Any<CancellationToken>()).Returns(Case());

        var ledger = Substitute.For<ISupportEventLedger>();
        ledger.TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var mailOptions = options ?? new MailOptions();
        mailOptions.FromAddress = "support@example.org";

        var files = new InMemorySupportAttachmentStore();

        return (new EmailEventHandler(store, ledger, Options.Create(mailOptions), TimeProvider.System, attachmentStore: files), store, files);
    }
}
