using MimeKit;
using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Reading a received mail: who sent it, which address it was delivered to, and whether a person sent it at
/// all.
/// </summary>
/// <remarks>
/// <b>The delivery-header chain is the part that decides whether two sites can share a mailbox.</b> IMAP
/// exposes no envelope, so which address a mail was addressed to is whatever the receiving server chose to
/// record — and <c>To</c> is a last resort that says nothing about a bcc'd or forwarded mail.
/// </remarks>
public class InboundMailReaderTests
{
    private static MimeMessage Message(string body = "Any news?", Action<MimeMessage> customize = null)
    {
        var message = new MimeMessage
        {
            Subject = "Re: Export is empty",
            Body = new TextPart("plain") { Text = body },
            MessageId = "reply-1@example.com",
            Date = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero)
        };

        message.From.Add(new MailboxAddress("A User", "user@example.com"));
        message.To.Add(new MailboxAddress("Support", "support@fortdocs.se"));

        customize?.Invoke(message);

        return message;
    }

    [Fact]
    public void AReply_IsReadIntoItsParts()
    {
        var mail = InboundMailReader.Read(Message());

        Assert.Equal("reply-1@example.com", mail.MessageId);
        Assert.Equal("user@example.com", mail.From);
        Assert.Equal("Re: Export is empty", mail.Subject);
        Assert.Equal("Any news?", mail.Body);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero), mail.SentAt);
        Assert.False(mail.IsAutomated);
        Assert.False(mail.HadAttachments);
    }

    [Fact]
    public void TheSenderAddress_IsLowerCasedAndStrippedOfItsDisplayName()
    {
        var mail = InboundMailReader.Read(Message(customize: m =>
        {
            m.From.Clear();
            m.From.Add(new MailboxAddress("A User", "User@Example.COM"));
        }));

        Assert.Equal("user@example.com", mail.From);
    }

    [Fact]
    public void TheThreadHeaders_AreCarried()
    {
        var mail = InboundMailReader.Read(Message(customize: m =>
        {
            m.InReplyTo = "opening@fortdocs.se";
            m.References.Add("opening@fortdocs.se");
        }));

        Assert.Equal("opening@fortdocs.se", mail.InReplyTo);
        Assert.Equal(["opening@fortdocs.se"], mail.References);
    }

    [Fact]
    public void DeliveredTo_IsPreferredOverTheToHeader()
    {
        var mail = InboundMailReader.Read(Message(customize: m =>
            m.Headers.Add("Delivered-To", "support+64f1c2@fortdocs.se")));

        Assert.Equal("support+64f1c2@fortdocs.se", mail.DeliveredTo[0]);
        Assert.Contains("support@fortdocs.se", mail.DeliveredTo);
    }

    [Theory]
    [InlineData("X-Original-To")]
    [InlineData("Envelope-To")]
    public void TheOtherDeliveryHeaders_AreHonoured(string header)
    {
        var mail = InboundMailReader.Read(Message(customize: m => m.Headers.Add(header, "billing@eplicta.se")));

        Assert.Equal("billing@eplicta.se", mail.DeliveredTo[0]);
    }

    /// <summary>
    /// A delivery header may carry a full mailbox rather than a bare address, and the filter compares
    /// addresses.
    /// </summary>
    [Fact]
    public void ADeliveryHeaderWithADisplayName_YieldsTheAddress()
    {
        var mail = InboundMailReader.Read(Message(customize: m =>
            m.Headers.Add("Delivered-To", "\"FortDocs\" <Support@FortDocs.se>")));

        Assert.Equal("support@fortdocs.se", mail.DeliveredTo[0]);
    }

    [Fact]
    public void CcRecipients_AreIncluded()
    {
        var mail = InboundMailReader.Read(Message(customize: m =>
            m.Cc.Add(new MailboxAddress("Other", "other@eplicta.se"))));

        Assert.Contains("other@eplicta.se", mail.DeliveredTo);
    }

    [Fact]
    public void WithNoDeliveryHeaderAtAll_TheToHeaderIsAllThereIs()
    {
        var mail = InboundMailReader.Read(Message());

        Assert.Equal(["support@fortdocs.se"], mail.DeliveredTo);
    }

    [Theory]
    [InlineData("Auto-Submitted", "auto-replied")]
    [InlineData("X-Auto-Response-Suppress", "All")]
    [InlineData("Precedence", "bulk")]
    [InlineData("List-Id", "<announce.example.com>")]
    [InlineData("List-Unsubscribe", "<mailto:x@example.com>")]
    public void MailThatAnnouncesItselfAsAutomated_IsRecognised(string header, string value)
    {
        var mail = InboundMailReader.Read(Message(customize: m => m.Headers.Add(header, value)));

        Assert.True(mail.IsAutomated);
    }

    /// <summary>
    /// <c>Auto-Submitted: no</c> is the explicit way of saying a person sent it, so it must not be read as
    /// the header merely being present.
    /// </summary>
    [Fact]
    public void AutoSubmittedNo_MeansAPersonSentIt()
    {
        var mail = InboundMailReader.Read(Message(customize: m => m.Headers.Add("Auto-Submitted", "no")));

        Assert.False(mail.IsAutomated);
    }

    /// <summary>An empty return path is how a bounce identifies itself.</summary>
    [Fact]
    public void ABounce_IsRecognisedByItsEmptyReturnPath()
    {
        var mail = InboundMailReader.Read(Message(customize: m => m.Headers.Add(HeaderId.ReturnPath, "<>")));

        Assert.True(mail.IsAutomated);
    }

    [Fact]
    public void AnOrdinaryReturnPath_IsNotAutomation()
    {
        var mail = InboundMailReader.Read(Message(customize: m => m.Headers.Add(HeaderId.ReturnPath, "<user@example.com>")));

        Assert.False(mail.IsAutomated);
    }

    [Fact]
    public void AnHtmlOnlyMail_IsFlattenedToText()
    {
        var mail = InboundMailReader.Read(Message(customize: m =>
            m.Body = new TextPart("html") { Text = "<p>Any <b>news</b>?</p><p>Thanks</p>" }));

        Assert.Equal("Any news?\n\nThanks", mail.Body);
    }

    [Fact]
    public void AMailWithAnAttachment_SaysSo_ButDoesNotCarryIt()
    {
        var mail = InboundMailReader.Read(Message(customize: m =>
        {
            var attachment = new MimePart("application", "pdf")
            {
                Content = new MimeContent(new MemoryStream("not really a pdf"u8.ToArray())),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                FileName = "report.pdf"
            };

            m.Body = new Multipart("mixed") { new TextPart("plain") { Text = "See attached." }, attachment };
        }));

        Assert.True(mail.HadAttachments);
        Assert.Equal("See attached.", mail.Body);
    }
}
