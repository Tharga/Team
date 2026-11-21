using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The team a sent mail was about settles which of a verified sender's teams their reply belongs to (Tharga/Team#539, following #529).
/// </summary>
/// <remarks>
/// <b>The refusals are the tests to keep.</b> The mail log's team is a hint and never a credential: it is only looked for among the teams the sender is a
/// member of, it never places an unverified sender, and it never overrides the sender checks. Placing a case puts its words in front of that team's
/// support readers, so every case where the hint is not enough is asserted to be left as it was.
/// </remarks>
public class EmailReplyTeamPlacementTests
{
    private const string Sender = "anna@example.com";
    private const string Identity = "sub-anna";
    private const string SentMessageId = "invite-1@example.net";

    private static readonly SupportCaseTeam Acme = new() { Key = "acme", Name = "Acme" };
    private static readonly SupportCaseTeam Beta = new() { Key = "beta", Name = "Beta" };

    private readonly ISupportCaseStore _store = Substitute.For<ISupportCaseStore>();
    private readonly ISupportSenderDirectory _directory = Substitute.For<ISupportSenderDirectory>();
    private readonly IUserMailOutbox _log = Substitute.For<IUserMailOutbox>();
    private readonly List<SupportCase> _raised = [];

    private EmailEventHandler Sut()
    {
        _store.AddCaseAsync(Arg.Do<SupportCase>(x => _raised.Add(x)), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var ledger = Substitute.For<ISupportEventLedger>();
        ledger.TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var options = new MailOptions { FromAddress = "support@example.net", Recipients = ["example.net"] };

        return new EmailEventHandler(_store, ledger, Options.Create(options), TimeProvider.System, senderDirectory: _directory, mailLog: _log);
    }

    private void TheSenderIs(params SupportSender[] senders) => _directory.FindAsync(Sender, Arg.Any<CancellationToken>()).Returns(senders);

    private static SupportSender Anna(params SupportCaseTeam[] teams) => new() { Identity = Identity, Name = "Anna Andersson", Teams = teams };

    private void TheSentMailWasAbout(string teamKey)
        => _log.FindSentByMessageIdAsync(SentMessageId, Arg.Any<CancellationToken>()).Returns(new UserMailItem
        {
            Id = "mail-row-1",
            Mail = new UserMail { Category = "account", Subject = "Your invitation", Body = "Welcome.", TeamKey = teamKey },
            Status = UserMailStatus.Sent,
            QueuedAt = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc),
            MessageId = SentMessageId
        });

    private static InboundMail Reply(bool verified) => new(
        MessageId: "reply-1@example.com",
        From: Sender,
        DeliveredTo: ["support@example.net"],
        Subject: "Re: Your invitation",
        Body: "I cannot sign in.",
        SentAt: new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero),
        InReplyTo: SentMessageId,
        References: [SentMessageId],
        SenderVerified: verified,
        SenderDomain: verified ? "example.com" : null);

    [Fact]
    public async Task AVerifiedSenderInSeveralTeams_IsPlacedInTheTeamTheMailWasAbout()
    {
        TheSenderIs(Anna(Acme, Beta));
        TheSentMailWasAbout("beta");

        await Sut().HandleAsync(Reply(verified: true));

        var raised = Assert.Single(_raised);
        Assert.Equal("beta", raised.TeamKey);
        Assert.Equal("Beta", raised.TeamName);
        Assert.Equal(Identity, raised.AuthorIdentity);
        Assert.Empty(raised.SuggestedTeams);
        Assert.Null(raised.SuggestionReason);
    }

    /// <summary>Without a hint nothing changes: a verified sender in two teams is suggested both, and a person decides.</summary>
    [Fact]
    public async Task AMailAboutNoTeam_LeavesSeveralTeamsToAPerson()
    {
        TheSenderIs(Anna(Acme, Beta));
        TheSentMailWasAbout(null);

        await Sut().HandleAsync(Reply(verified: true));

        var raised = Assert.Single(_raised);
        Assert.Null(raised.TeamKey);
        Assert.Equal(SupportTeamSuggestionReason.SeveralTeams, raised.SuggestionReason);
    }

    /// <summary>The hint is looked for among the sender's teams only, so a mail that names a team they are not in places nobody there.</summary>
    [Fact]
    public async Task AMailAboutATeamTheSenderIsNotIn_PlacesNothingThere()
    {
        TheSenderIs(Anna(Acme, Beta));
        TheSentMailWasAbout("gamma");

        await Sut().HandleAsync(Reply(verified: true));

        var raised = Assert.Single(_raised);
        Assert.Null(raised.TeamKey);
        Assert.Equal(SupportTeamSuggestionReason.SeveralTeams, raised.SuggestionReason);
    }

    /// <summary>An unverified <c>From</c> can name anybody, so the team of the mail never places it.</summary>
    [Fact]
    public async Task AnUnverifiedSender_IsNeverPlaced_WhateverTheMailWasAbout()
    {
        TheSenderIs(Anna(Acme, Beta));
        TheSentMailWasAbout("beta");

        await Sut().HandleAsync(Reply(verified: false));

        var raised = Assert.Single(_raised);
        Assert.Null(raised.TeamKey);
        Assert.Null(raised.AuthorIdentity);
        Assert.Equal(SupportTeamSuggestionReason.SenderNotVerified, raised.SuggestionReason);
    }

    /// <summary>An address two accounts hold belongs to neither, and the mail's team does not break the tie.</summary>
    [Fact]
    public async Task AnAddressSeveralAccountsHold_IsNeverPlaced_WhateverTheMailWasAbout()
    {
        TheSenderIs(Anna(Acme, Beta), new SupportSender { Identity = "sub-other", Name = "Other", Teams = [Beta] });
        TheSentMailWasAbout("beta");

        await Sut().HandleAsync(Reply(verified: true));

        var raised = Assert.Single(_raised);
        Assert.Null(raised.TeamKey);
        Assert.Equal(SupportTeamSuggestionReason.SharedAddress, raised.SuggestionReason);
    }

    /// <summary>A sender who is in one team is placed there as before, and a mail about another team does not move them.</summary>
    [Fact]
    public async Task ASenderInOneTeam_IsPlacedThere_WhateverTheMailWasAbout()
    {
        TheSenderIs(Anna(Acme));
        TheSentMailWasAbout("beta");

        await Sut().HandleAsync(Reply(verified: true));

        Assert.Equal("acme", Assert.Single(_raised).TeamKey);
    }

    /// <summary>A mail the log no longer holds gives no hint, so the case opens exactly as before.</summary>
    [Fact]
    public async Task AMailTheLogDoesNotHold_GivesNoHint()
    {
        TheSenderIs(Anna(Acme, Beta));

        await Sut().HandleAsync(Reply(verified: true));

        var raised = Assert.Single(_raised);
        Assert.Null(raised.TeamKey);
        Assert.Equal(SupportTeamSuggestionReason.SeveralTeams, raised.SuggestionReason);
    }
}
