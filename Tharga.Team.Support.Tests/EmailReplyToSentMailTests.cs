using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A reply to a mail the application sent on its own, such as an invitation or a notice, raises a case that records which mail it answers, so staff can
/// see what the user is answering (Tharga/Team#529).
/// </summary>
/// <remarks>
/// <b>Why the mail is looked up, and only when it matches no case.</b> The quoted thread is cut off the body, which is right for the transcript, so
/// the mail answered would otherwise appear nowhere. The mail log holds it with the <c>Message-ID</c> it was sent with, which the reply names.
/// A reply that continues a case is handled as before and never asks the log. Addresses are made up.
/// </remarks>
public class EmailReplyToSentMailTests
{
    private const string SentMessageId = "invite-1@example.net";
    private const string SentMailId = "mail-row-1";

    private readonly ISupportCaseStore _store = Substitute.For<ISupportCaseStore>();
    private readonly IUserMailOutbox _log = Substitute.For<IUserMailOutbox>();
    private readonly List<SupportCase> _raised = [];

    private EmailEventHandler Sut(bool withLog = true)
    {
        _store.AddCaseAsync(Arg.Do<SupportCase>(x => _raised.Add(x)), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var ledger = Substitute.For<ISupportEventLedger>();
        ledger.TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var options = new MailOptions { FromAddress = "support@example.net", Recipients = ["example.net"] };

        return new EmailEventHandler(_store, ledger, Options.Create(options), TimeProvider.System, mailLog: withLog ? _log : null);
    }

    private void TheLogHoldsTheSentMail(string subject = "Your invitation")
        => _log.FindSentByMessageIdAsync(SentMessageId, Arg.Any<CancellationToken>()).Returns(new UserMailItem
        {
            Id = SentMailId,
            Mail = new UserMail { Category = "account", Subject = subject, Body = "Welcome." },
            Status = UserMailStatus.Sent,
            QueuedAt = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc),
            MessageId = SentMessageId
        });

    private static InboundMail Reply(string inReplyTo, string[] references = null, string subject = "Re: Your invitation") => new(
        MessageId: "reply-1@customer.example",
        From: "user@customer.example",
        DeliveredTo: ["support@example.net"],
        Subject: subject,
        Body: "I cannot sign in.",
        SentAt: new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero),
        InReplyTo: inReplyTo,
        References: references ?? [inReplyTo]);

    [Fact]
    public async Task AReplyToASentMail_RaisesACase_NamingTheMailItAnswers()
    {
        TheLogHoldsTheSentMail();

        var outcome = await Sut().HandleAsync(Reply(SentMessageId));

        Assert.True(outcome.OpenedCase);
        Assert.Equal(SentMailId, Assert.Single(_raised).InReplyToMailId);
    }

    [Fact]
    public async Task TheMailIsFound_ByTheReferencesToo_WhenInReplyToNamesNothingWeSent()
    {
        TheLogHoldsTheSentMail();

        await Sut().HandleAsync(Reply("somebody-elses@example.org", [SentMessageId, "somebody-elses@example.org"]));

        Assert.Equal(SentMailId, Assert.Single(_raised).InReplyToMailId);
    }

    /// <summary>A mail that expired from the log, or one this application never sent, raises a case exactly as it did before.</summary>
    [Fact]
    public async Task AMailTheLogDoesNotHold_RaisesACase_ExactlyAsBefore()
    {
        var outcome = await Sut().HandleAsync(Reply("unknown@example.org"));

        Assert.True(outcome.OpenedCase);
        Assert.Null(Assert.Single(_raised).InReplyToMailId);
    }

    [Fact]
    public async Task AReplyThatContinuesACase_NeverAsksTheLog()
    {
        _store.GetCaseByBindingAsync(SupportChannelType.Email, "opening-1@example.net", Arg.Any<CancellationToken>()).Returns(new SupportCase
        {
            Id = "case-1",
            TeamKey = "acme",
            AuthorIdentity = "sub-1",
            AuthorName = "A User",
            Subject = "Export is empty",
            Status = SupportCaseStatus.Open,
            CreatedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            MessageCount = 1,
            Bindings = [new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = "opening-1@example.net", Address = "user@customer.example" }]
        });

        await Sut().HandleAsync(Reply("opening-1@example.net"));

        await _log.DidNotReceive().FindSentByMessageIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(_raised);
    }

    [Fact]
    public async Task AReplyWithNoSubject_TakesTheSubjectOfTheMailItAnswers()
    {
        TheLogHoldsTheSentMail("Your invitation to Acme");

        await Sut().HandleAsync(Reply(SentMessageId, subject: null));

        Assert.Equal("Your invitation to Acme", Assert.Single(_raised).Subject);
    }

    [Fact]
    public async Task AReplyWithASubject_KeepsIt()
    {
        TheLogHoldsTheSentMail("Your invitation to Acme");

        await Sut().HandleAsync(Reply(SentMessageId, subject: "Cannot sign in"));

        Assert.Equal("Cannot sign in", Assert.Single(_raised).Subject);
    }

    /// <summary>The ledger has recorded the mail already, so a log that fails must never cost it its case.</summary>
    [Fact]
    public async Task AMailLogThatFails_NeverCostsTheCase()
    {
        _log.FindSentByMessageIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<Task<UserMailItem>>(_ => throw new InvalidOperationException("log down"));

        var outcome = await Sut().HandleAsync(Reply(SentMessageId));

        Assert.True(outcome.OpenedCase);
        Assert.Null(Assert.Single(_raised).InReplyToMailId);
    }

    [Fact]
    public async Task WithNoMailLog_TheCaseOpensAsBefore()
    {
        var outcome = await Sut(withLog: false).HandleAsync(Reply(SentMessageId));

        Assert.True(outcome.OpenedCase);
        Assert.Null(Assert.Single(_raised).InReplyToMailId);
    }

    /// <summary>A log that cannot say (a host's own outbox that does not implement the lookup) is the same as one that does not hold it.</summary>
    [Fact]
    public async Task AnOutboxWithoutTheLookup_OpensTheCaseAsBefore()
    {
        _log.FindSentByMessageIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<Task<UserMailItem>>(_ => throw new NotSupportedException());

        var outcome = await Sut().HandleAsync(Reply(SentMessageId));

        Assert.True(outcome.OpenedCase);
    }
}
