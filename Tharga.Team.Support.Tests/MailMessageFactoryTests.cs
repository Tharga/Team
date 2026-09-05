using MimeKit;
using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The headers that make a mail thread, asserted without a mail server.
/// </summary>
/// <remarks>
/// <b>This is the half of sending that fails invisibly.</b> A mail with the right body and the wrong headers
/// is delivered perfectly and arrives as an unrelated message, so the failure shows up as "threading does not
/// work" long after the send was declared successful.
/// </remarks>
public class MailMessageFactoryTests
{
    private static MailOptions Options()
    {
        var options = new MailOptions { FromAddress = "support@fortdocs.se", FromName = "FortDocs Support" };
        options.Smtp.Host = "smtp.example.com";

        return options;
    }

    [Fact]
    public void AnOpeningMail_CarriesSenderSubjectAndBody()
    {
        var message = MailMessageFactory.Create(
            new OutboundMail("user@example.com", "Export is empty", "It returns nothing."), Options());

        Assert.Equal("support@fortdocs.se", message.From.Mailboxes.Single().Address);
        Assert.Equal("FortDocs Support", message.From.Mailboxes.Single().Name);
        Assert.Equal("user@example.com", message.To.Mailboxes.Single().Address);
        Assert.Equal("Export is empty", message.Subject);
        Assert.Equal("It returns nothing.", message.TextBody);
    }

    /// <summary>
    /// SMTP reports no identifier, so the thread's identity is generated here and stored by the caller.
    /// </summary>
    [Fact]
    public void EveryMail_IsGivenItsOwnMessageId()
    {
        var first = MailMessageFactory.Create(new OutboundMail("user@example.com", "One", "Body"), Options());
        var second = MailMessageFactory.Create(new OutboundMail("user@example.com", "Two", "Body"), Options());

        Assert.False(string.IsNullOrWhiteSpace(first.MessageId));
        Assert.NotEqual(first.MessageId, second.MessageId);
        Assert.EndsWith("fortdocs.se", first.MessageId);
    }

    [Fact]
    public void AReply_NamesTheMessageItAnswers()
    {
        var message = MailMessageFactory.Create(
            new OutboundMail("user@example.com", "Re: Export", "Looking into it.", InReplyTo: "opening@fortdocs.se"),
            Options());

        Assert.Equal("opening@fortdocs.se", message.InReplyTo);
    }

    /// <summary>
    /// Clients thread on <c>References</c>, so a reply naming only its immediate parent starts a new
    /// conversation in some of them.
    /// </summary>
    [Fact]
    public void AReply_CarriesTheWholeThreadChain_WithTheParentLast()
    {
        var message = MailMessageFactory.Create(
            new OutboundMail("user@example.com", "Re: Export", "Looking into it.",
                InReplyTo: "second@fortdocs.se",
                References: ["first@fortdocs.se"]),
            Options());

        Assert.Equal(["first@fortdocs.se", "second@fortdocs.se"], message.References.ToArray());
    }

    [Fact]
    public void AParentAlreadyInTheChain_IsNotRepeated()
    {
        var message = MailMessageFactory.Create(
            new OutboundMail("user@example.com", "Re: Export", "Looking into it.",
                InReplyTo: "second@fortdocs.se",
                References: ["first@fortdocs.se", "second@fortdocs.se"]),
            Options());

        Assert.Equal(["first@fortdocs.se", "second@fortdocs.se"], message.References.ToArray());
    }

    [Fact]
    public void AnOpeningMail_HasNoThreadHeaders()
    {
        var message = MailMessageFactory.Create(new OutboundMail("user@example.com", "New", "Body"), Options());

        Assert.Null(message.InReplyTo);
        Assert.Empty(message.References);
    }

    /// <summary>
    /// The per-case reply address is what routes an answer back to the right case, and it differs from the
    /// address the mail is sent from.
    /// </summary>
    [Fact]
    public void APerCaseReplyAddress_IsSetWhenGiven()
    {
        var message = MailMessageFactory.Create(
            new OutboundMail("user@example.com", "New", "Body", ReplyTo: "support+64f1c2@fortdocs.se"),
            Options());

        Assert.Equal("support+64f1c2@fortdocs.se", message.ReplyTo.Mailboxes.Single().Address);
    }

    [Fact]
    public void NoReplyAddress_LeavesRepliesGoingToTheSender()
    {
        var message = MailMessageFactory.Create(new OutboundMail("user@example.com", "New", "Body"), Options());

        Assert.Empty(message.ReplyTo);
    }

    [Fact]
    public void AMessageWithNoBody_IsStillValid()
    {
        var message = MailMessageFactory.Create(new OutboundMail("user@example.com", null, null), Options());

        Assert.Equal(string.Empty, message.Subject);
        Assert.Equal(string.Empty, ((TextPart)message.Body).Text);
    }
}
