using System.Text;
using MimeKit;
using Tharga.Team.Email;

namespace Tharga.Team.Email.Tests;

/// <summary>
/// Reading a bounce: that it is one, which mail it reports, and why that mail did not arrive.
/// </summary>
/// <remarks>
/// The samples are shaped as Exim sends them, because that is what the support mailbox's own host runs and
/// what returned an answer the day this was written: the receiving server refused it for failing SPF.
/// </remarks>
public class BounceReaderTests
{
    internal const string AnswerId = "answer-2@fortdocs.se";

    /// <summary>An RFC 3464 report, as a current Exim writes one.</summary>
    internal static readonly string EximReport = Lf("""
        Return-path: <>
        Envelope-to: support@fortdocs.se
        Delivery-date: Sat, 17 Oct 2026 10:12:03 +0200
        Received: from mailnull by s1.hostup.se with local (Exim 4.98)
        	id 1tXyZa-0001Ab-2C
        	for support@fortdocs.se; Sat, 17 Oct 2026 10:12:03 +0200
        X-Failed-Recipients: kund@kommun.se
        Auto-Submitted: auto-replied
        From: Mail Delivery System <Mailer-Daemon@s1.hostup.se>
        To: support@fortdocs.se
        References: <answer-2@fortdocs.se>
        Content-Type: multipart/report; report-type=delivery-status; boundary=1729152723-eximdsn-1804289383
        MIME-Version: 1.0
        Subject: Mail delivery failed: returning message to sender
        Message-Id: <E1tXyZa-0001Ab-2C@s1.hostup.se>
        Date: Sat, 17 Oct 2026 10:12:03 +0200

        --1729152723-eximdsn-1804289383
        Content-type: text/plain; charset=us-ascii

        This message was created automatically by mail delivery software.

        A message that you sent could not be delivered to one or more of its
        recipients. This is a permanent error. The following address(es) failed:

          kund@kommun.se
            host mx1.kommun.se [192.0.2.10]
            SMTP error from remote mail server after RCPT TO:<kund@kommun.se>:
            554 5.7.23 SPF validation failed

        --1729152723-eximdsn-1804289383
        Content-type: message/delivery-status

        Reporting-MTA: dns; s1.hostup.se

        Action: failed
        Final-Recipient: rfc822;kund@kommun.se
        Status: 5.7.23
        Remote-MTA: dns; mx1.kommun.se
        Diagnostic-Code: smtp; 554 5.7.23 SPF validation failed

        --1729152723-eximdsn-1804289383
        Content-type: message/rfc822

        Return-path: <support@fortdocs.se>
        From: FortDocs Support <support@fortdocs.se>
        To: kund@kommun.se
        Subject: Re: Export is empty
        Message-Id: <answer-2@fortdocs.se>
        In-Reply-To: <opening-1@kommun.se>
        References: <opening-1@kommun.se>
        Date: Sat, 17 Oct 2026 10:12:01 +0200
        Content-Type: text/plain; charset=utf-8

        Sam Support:

        Looking into it.

        --1729152723-eximdsn-1804289383--
        """);

    /// <summary>The older plain-text Exim bounce: no report, the original's headers pasted under the explanation.</summary>
    internal static readonly string EximPlain = Lf("""
        Return-path: <>
        From: Mail Delivery System <Mailer-Daemon@s1.hostup.se>
        To: support@fortdocs.se
        Subject: Mail delivery failed: returning message to sender
        Message-Id: <E1tXyZb-0001Ac-3D@s1.hostup.se>
        Date: Sat, 17 Oct 2026 10:14:03 +0200
        Content-Type: text/plain; charset=us-ascii

        This message was created automatically by mail delivery software.

        A message that you sent could not be delivered to one or more of its
        recipients. This is a permanent error. The following address(es) failed:

          kund@kommun.se
            SMTP error from remote mail server after RCPT TO:<kund@kommun.se>:
            554 5.7.23 SPF validation failed

        ------ This is a copy of the message, including all the headers. ------

        Return-path: <support@fortdocs.se>
        From: FortDocs Support <support@fortdocs.se>
        To: kund@kommun.se
        Subject: Re: Export is empty
        Message-Id: <answer-2@fortdocs.se>
        Date: Sat, 17 Oct 2026 10:14:01 +0200

        Sam Support:

        Looking into it.
        """);

    // A checkout may carry either line ending, and the samples are edited by replacing whole lines.
    private static string Lf(string text) => text.Replace("\r\n", "\n");

    internal static MimeMessage Parse(string raw)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw.Replace("\r\n", "\n").Replace("\n", "\r\n")));

        return MimeMessage.Load(stream);
    }

    [Fact]
    public void AnEximReport_NamesTheAnswerThatBounced()
    {
        var mail = InboundMailReader.Read(Parse(EximReport));

        Assert.Equal(AnswerId, mail.BouncedMessageId);
    }

    [Fact]
    public void AnEximReport_GivesTheReceivingServersOwnWords()
    {
        var mail = InboundMailReader.Read(Parse(EximReport));

        Assert.Equal("554 5.7.23 SPF validation failed", mail.BounceReason);
    }

    [Fact]
    public void ABounce_IsAlsoAutomated()
    {
        Assert.True(InboundMailReader.Read(Parse(EximReport)).IsAutomated);
    }

    [Fact]
    public void APlainEximBounce_IsRecognisedFromThePastedHeaders()
    {
        var mail = InboundMailReader.Read(Parse(EximPlain));

        Assert.Equal(AnswerId, mail.BouncedMessageId);
        Assert.StartsWith("SMTP error from remote mail server after RCPT TO:<kund@kommun.se>:", mail.BounceReason);
        Assert.EndsWith("554 5.7.23 SPF validation failed", mail.BounceReason);
        Assert.DoesNotContain("This is a copy", mail.BounceReason);
    }

    [Fact]
    public void AReportWithNoDiagnostic_FallsBackToTheStatus()
    {
        var mail = InboundMailReader.Read(Parse(EximReport.Replace("Diagnostic-Code: smtp; 554 5.7.23 SPF validation failed\n", "")));

        Assert.Equal("Status 5.7.23", mail.BounceReason);
    }

    [Fact]
    public void AReportWithoutTheOriginalAttached_FallsBackToItsReferences()
    {
        var withoutOriginal = EximReport[..EximReport.IndexOf("--1729152723-eximdsn-1804289383\nContent-type: message/rfc822", StringComparison.Ordinal)]
                              + "--1729152723-eximdsn-1804289383--\n";

        var mail = InboundMailReader.Read(Parse(withoutOriginal));

        Assert.Equal(AnswerId, mail.BouncedMessageId);
    }

    [Fact]
    public void AnOverlongReason_IsTrimmed()
    {
        var reason = "554 5.7.1 " + new string('x', 2000);
        var mail = InboundMailReader.Read(Parse(EximReport.Replace("smtp; 554 5.7.23 SPF validation failed", "smtp; " + reason)));

        Assert.True(mail.BounceReason.Length <= BounceReader.MaxReasonLength);
        Assert.StartsWith("554 5.7.1 xxx", mail.BounceReason);
    }

    [Fact]
    public void AnOrdinaryReply_IsNoBounce()
    {
        var message = new MimeMessage { Subject = "Re: Export is empty", Body = new TextPart("plain") { Text = "Any news?" }, MessageId = "reply-1@example.com" };
        message.From.Add(new MailboxAddress("A User", "user@example.com"));
        message.InReplyTo = AnswerId;

        var mail = InboundMailReader.Read(message);

        Assert.Null(mail.BouncedMessageId);
        Assert.Null(mail.BounceReason);
    }

    /// <summary>A person forwarding a mail as an attachment is not reporting a failed delivery.</summary>
    [Fact]
    public void APersonForwardingAMailAsAnAttachment_IsNoBounce()
    {
        var original = Parse(EximReport).BodyParts.OfType<MessagePart>().Single().Message;
        var message = new MimeMessage { Subject = "Fwd: Re: Export is empty", MessageId = "fwd-1@example.com" };
        message.From.Add(new MailboxAddress("A User", "user@example.com"));
        message.Body = new Multipart("mixed") { new TextPart("plain") { Text = "See below." }, new MessagePart { Message = original } };

        Assert.Null(InboundMailReader.Read(message).BouncedMessageId);
    }

    [Fact]
    public void ADaemonWithTheOriginalAttached_IsABounceWithoutAReport()
    {
        var original = Parse(EximReport).BodyParts.OfType<MessagePart>().Single().Message;
        var message = new MimeMessage { Subject = "Undeliverable: Re: Export is empty", MessageId = "ndr-1@kommun.se" };
        message.From.Add(new MailboxAddress("Postmaster", "postmaster@kommun.se"));
        message.Body = new Multipart("mixed") { new TextPart("plain") { Text = "Delivery has failed to these recipients: kund@kommun.se" }, new MessagePart { Message = original } };

        var mail = InboundMailReader.Read(message);

        Assert.Equal(AnswerId, mail.BouncedMessageId);
        Assert.Equal("Delivery has failed to these recipients: kund@kommun.se", mail.BounceReason);
    }
}
