using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A formatted answer is mailed as HTML with the conversation so far quoted in blockquotes, and with exactly the plain
/// text a plain answer would have had as its alternative; a plain answer is mailed as text alone, as before.
/// </summary>
public class EmailSupportChannelHtmlTests
{
    private const string OpeningId = "opening-1@example.org";
    private const string Correspondent = "user@example.com";
    private const string CaseId = "case-1";

    private static readonly DateTime First = new(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AFormattedAnswer_IsMailedAsHtml_WithTheTextAsItsAlternative()
    {
        var earlier = new[] { Customer(1, "It is <empty>.\nReally.") };
        var (channel, mail) = Build(earlier);
        var answer = Answer("<p>Try <b>this</b>.</p>");

        await channel.PostWithResultAsync(Binding(), answer);

        await mail.Received(1).SendAsync(Arg.Is<OutboundMail>(x =>
                x.Body == EmailSupportChannel.Body(answer, earlier) &&
                x.BodyHtml == "<p>Support Agent:</p><p>Try <b>this</b>.</p><p>2026-10-17 08:00 UTC, A User:</p><blockquote>It is &lt;empty&gt;.<br>Really.</blockquote>"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APlainAnswer_IsMailedAsTextAlone()
    {
        var (channel, mail) = Build([Customer(1, "Help")]);

        await channel.PostWithResultAsync(Binding(), Answer(null));

        await mail.Received(1).SendAsync(Arg.Is<OutboundMail>(x => x.BodyHtml == null && x.Body.StartsWith("Support Agent:\n\nTry this.")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AnEarlierFormattedMessage_IsQuotedAsItsOwnHtml_NewestFirst()
    {
        var earlier = new[]
        {
            Customer(2, "Still empty", "<p>Still <i>empty</i></p>"),
            Customer(1, "Help")
        };

        var html = EmailSupportChannel.Html(Answer("<p><b>Fixed</b></p>"), earlier);

        Assert.Equal("<p>Support Agent:</p><p><b>Fixed</b></p>" +
                     "<p>2026-10-17 08:01 UTC, A User:</p><blockquote><p>Still <i>empty</i></p></blockquote>" +
                     "<p>2026-10-17 08:00 UTC, A User:</p><blockquote>Help</blockquote>", html);
    }

    /// <summary>Whatever was stored, what leaves is sanitized again.</summary>
    [Fact]
    public void WhatIsMailed_IsSanitizedAgain()
    {
        var html = EmailSupportChannel.Html(Answer("<p onclick=\"x()\"><b>Fixed</b><script>x()</script></p>"), [Customer(1, "Help", "<p><b>Help</b><iframe src=\"https://evil.example\"></iframe></p>")]);

        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("script", html);
        Assert.DoesNotContain("iframe", html);
    }

    [Fact]
    public void AnAuthorNameIsEscaped()
    {
        var html = EmailSupportChannel.Html(Answer("<p><b>Fixed</b></p>") with { AuthorName = "<b>Eve</b>" }, []);

        Assert.StartsWith("<p>&lt;b&gt;Eve&lt;/b&gt;:</p>", html);
    }

    [Fact]
    public void AnEarlierMessagesImages_AreNamedRatherThanSentAgain()
    {
        var earlier = Customer(1, "Look", "<p>Look <img src=\"cid:shot@x\"></p>") with
        {
            Attachments = [new SupportAttachment { Id = "a1", FileName = "shot.png", ContentType = "image/png", Size = 3, Inline = true, ContentId = "shot@x" }]
        };

        var html = EmailSupportChannel.Html(Answer("<p><b>Fixed</b></p>"), [earlier]);

        Assert.Contains("<blockquote><p>Look shot.png</p></blockquote>", html);
        Assert.DoesNotContain("cid:", html);
    }

    [Fact]
    public async Task AnImageTheAnswerCarries_KeepsItsReference_AndTravelsWithItsContentId()
    {
        var files = new InMemorySupportAttachmentStore();
        var saved = await files.SaveAsync(CaseId, new SupportAttachmentUpload { FileName = "chart.png", ContentType = "image/png", Content = new MemoryStream([1, 2]), Inline = true, ContentId = "chart@x" });
        var (channel, mail) = Build([], files);

        await channel.PostWithResultAsync(Binding(), Answer("<p><b>See</b> <img src=\"cid:chart@x\"></p>") with { Attachments = [saved] });

        await mail.Received(1).SendAsync(Arg.Is<OutboundMail>(x =>
                x.BodyHtml.Contains("<img src=\"cid:chart@x\">") &&
                x.Attachments.Single().ContentId == "chart@x"),
            Arg.Any<CancellationToken>());
    }

    private static SupportMessage Customer(int sequence, string body, string html = null) => new()
    {
        Sequence = sequence,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "sub-1",
        AuthorName = "A User",
        Body = body,
        BodyHtml = html,
        SentAt = First.AddMinutes(sequence - 1),
        Source = SupportChannelType.Email,
        Delivery = SupportMessageDelivery.Sent
    };

    private static SupportMessage Answer(string html) => new()
    {
        Sequence = 3,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "sub-2",
        AuthorName = "Support Agent",
        Body = "Try this.",
        BodyHtml = html,
        SentAt = First.AddMinutes(5),
        AsSupport = true
    };

    private static SupportChannelBinding Binding() => new()
    {
        ChannelType = SupportChannelType.Email,
        ExternalId = OpeningId,
        Address = Correspondent
    };

    private static (ISupportChannel Channel, IMailClient Mail) Build(IReadOnlyList<SupportMessage> earlier, InMemorySupportAttachmentStore files = null)
    {
        var mail = Substitute.For<IMailClient>();
        mail.CanSend.Returns(true);
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("answer-1@example.org"));

        var store = Substitute.For<ISupportCaseStore>();
        store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId, Arg.Any<CancellationToken>()).Returns(new SupportCase
        {
            Id = CaseId,
            TeamKey = "acme",
            AuthorIdentity = "sub-1",
            AuthorName = "A User",
            Subject = "Export is empty",
            Status = SupportCaseStatus.Open,
            CreatedAt = First,
            MessageCount = earlier.Count + 1
        });
        store.GetMessagesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new SupportMessagePage { Items = [.. earlier] });

        var options = Options.Create(new MailOptions { FromAddress = "support@example.org" });

        return (new EmailSupportChannel(mail, store, options, files ?? new InMemorySupportAttachmentStore()), mail);
    }
}
