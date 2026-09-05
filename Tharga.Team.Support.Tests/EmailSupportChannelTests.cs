using Microsoft.Extensions.Options;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Projecting a case onto an email thread: who it is addressed to, what is stored, and what is deliberately
/// not sent.
/// </summary>
/// <remarks>
/// <b>The two tests that matter most are the ones about not sending.</b> A mail that arrived from the channel
/// must not be posted back, or every message in the thread appears twice; and a case with nobody to write to
/// must still be raised, because a case is the record and email is a projection of it.
/// </remarks>
public class EmailSupportChannelTests
{
    private const string TeamKey = "acme";
    private const string Correspondent = "user@example.com";
    private const string OpeningId = "opening-1@fortdocs.se";





    /// <summary>
    /// The channel opens nothing from the site.
    /// </summary>
    /// <remarks>
    /// <b>It used to, and that was backwards.</b> Mailing the author when a case is raised on the site
    /// answers, by email, the one person who is already looking at the page. An email projection exists
    /// because a mail arrived, so <c>EmailEventHandler</c> creates it and this only ever replies into it.
    /// </remarks>
    [Fact]
    public async Task RaisingACaseOnTheSite_CreatesNoEmailProjection()
    {
        var (channel, mail, _) = Build();

        Assert.Null(await channel.OpenAsync(Case(), "The export is empty."));
        await mail.DidNotReceive().SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AReply_GoesToTheCorrespondentInTheSameThread()
    {
        var (channel, mail, store) = Build();
        store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId, Arg.Any<CancellationToken>()).Returns(Case());
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("reply-1@fortdocs.se"));

        var posted = await channel.PostAsync(Binding(), Message("Looking into it."));

        Assert.True(posted);
        await mail.Received(1).SendAsync(
            Arg.Is<OutboundMail>(x =>
                x.To == Correspondent &&
                x.Subject == "Re: Export is empty" &&
                x.InReplyTo == OpeningId &&
                x.References.Single() == OpeningId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AReply_NamesWhoWroteIt()
    {
        var (channel, mail, store) = Build();
        store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId, Arg.Any<CancellationToken>()).Returns(Case());
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("reply-1@fortdocs.se"));

        await channel.PostAsync(Binding(), Message("Looking into it."));

        await mail.Received(1).SendAsync(
            Arg.Is<OutboundMail>(x => x.Body == "Support Agent:\n\nLooking into it."),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A closure note is the toolkit speaking, so attributing it to a person would be a lie in front of a
    /// customer.
    /// </summary>
    [Fact]
    public async Task ASystemEntry_IsSentWithoutAnAuthor()
    {
        var (channel, mail, store) = Build();
        store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId, Arg.Any<CancellationToken>()).Returns(Case());
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("reply-1@fortdocs.se"));

        await channel.PostAsync(Binding(), Message("Case closed.") with { Kind = SupportMessageKind.System });

        await mail.Received(1).SendAsync(Arg.Is<OutboundMail>(x => x.Body == "Case closed."), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMessageThatArrivedByEmail_IsNotSentBack()
    {
        var (channel, mail, _) = Build();

        var posted = await channel.PostAsync(Binding(), Message("From their client.") with { Source = SupportChannelType.Email });

        Assert.False(posted);
        await mail.DidNotReceive().SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A Slack reply is not this channel's echo, so it is mailed on like any other entry.</summary>
    [Fact]
    public async Task AMessageThatArrivedFromAnotherChannel_IsStillMailed()
    {
        var (channel, mail, store) = Build();
        store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId, Arg.Any<CancellationToken>()).Returns(Case());
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("reply-1@fortdocs.se"));

        var posted = await channel.PostAsync(Binding(), Message("Answered in Slack.") with { Source = SupportChannelType.Slack });

        Assert.True(posted);
    }

    [Fact]
    public async Task ABindingWithNoAddress_CannotBeRepliedTo()
    {
        var (channel, mail, _) = Build();

        Assert.False(await channel.PostAsync(Binding() with { Address = null }, Message("Anything")));
        await mail.DidNotReceive().SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>());
    }



    /// <summary>
    /// The lookup can come back empty — a binding for a case that has since been purged. The mail still has
    /// somewhere to go, so it goes, with a subject that says something rather than nothing.
    /// </summary>
    [Fact]
    public async Task WhenTheCaseCannotBeFound_TheReplyIsStillSent()
    {
        var (channel, mail, store) = Build();
        store.GetCaseByBindingAsync(SupportChannelType.Email, OpeningId, Arg.Any<CancellationToken>()).Returns((SupportCase)null);
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("reply-1@fortdocs.se"));

        Assert.True(await channel.PostAsync(Binding(), Message("Looking into it.")));

        await mail.Received(1).SendAsync(
            Arg.Is<OutboundMail>(x => x.Subject == "Re: your support case"),
            Arg.Any<CancellationToken>());
    }

    private static SupportCase Case() => new()
    {
        Id = "case-1",
        TeamKey = TeamKey,
        AuthorIdentity = "sub-1",
        AuthorName = "A User",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        MessageCount = 1
    };

    private static SupportChannelBinding Binding() => new()
    {
        ChannelType = SupportChannelType.Email,
        ExternalId = OpeningId,
        Address = Correspondent
    };

    private static SupportMessage Message(string body) => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "sub-2",
        AuthorName = "Support Agent",
        Body = body,
        SentAt = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc)
    };

    private static (ISupportChannel Channel, ISupportMailClient Mail, ISupportCaseStore Store) Build(
        bool canSend = true,
        string userEmail = Correspondent,
        bool perCaseReplyTo = false)
    {
        var mail = Substitute.For<ISupportMailClient>();
        mail.CanSend.Returns(canSend);

        var store = Substitute.For<ISupportCaseStore>();

        var options = new MailOptions { FromAddress = "support@fortdocs.se", PerCaseReplyTo = perCaseReplyTo };

        return (new EmailSupportChannel(mail, store, Options.Create(options)), mail, store);
    }
}
