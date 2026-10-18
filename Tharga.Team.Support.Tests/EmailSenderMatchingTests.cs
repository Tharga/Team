using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;
using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A first mail from somebody the product knows: in their team when that is certain, otherwise unassigned
/// with their teams suggested.
/// </summary>
/// <remarks>
/// <b>The refusals are the tests to keep.</b> Placing a case in a team puts its words in front of that team's
/// support readers, so a <c>From</c> header that nothing verified must never do it, and neither may an address
/// that several accounts or several teams share. Only one combination places a case; every other one is
/// asserted to leave it where a person decides.
/// </remarks>
public class EmailSenderMatchingTests
{
    private const string Sender = "anna@example.com";
    private const string Identity = "sub-anna";

    private static readonly SupportCaseTeam Acme = new() { Key = "acme", Name = "Acme" };
    private static readonly SupportCaseTeam Beta = new() { Key = "beta", Name = "Beta" };

    private readonly ISupportCaseStore _store = Substitute.For<ISupportCaseStore>();
    private readonly ISupportCaseNotifier _notifier = Substitute.For<ISupportCaseNotifier>();
    private readonly ISupportSenderDirectory _directory = Substitute.For<ISupportSenderDirectory>();
    private readonly CapturingLogger _logger = new();

    public EmailSenderMatchingTests()
    {
        _directory.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<SupportSender>());
    }

    [Fact]
    public async Task AVerifiedSenderInOneTeam_OpensTheCaseInThatTeam()
    {
        Holds(Anna(Acme));

        var outcome = await Build().HandleAsync(NewMail(verified: true));

        Assert.True(outcome.OpenedCase);
        await _store.Received(1).AddCaseAsync(
            Arg.Is<SupportCase>(x =>
                x.TeamKey == "acme" &&
                x.TeamName == "Acme" &&
                x.AuthorIdentity == Identity &&
                x.AuthorName == "Anna Andersson" &&
                x.SuggestedTeams.Length == 0 &&
                x.SuggestionReason == null),
            Arg.Is<SupportMessage>(x => x.AuthorIdentity == Identity && x.AuthorName == "Anna Andersson" && x.Source == SupportChannelType.Email),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACaseOpenedInATeam_IsAnnouncedAsRaisedInThatTeam()
    {
        Holds(Anna(Acme));

        var outcome = await Build().HandleAsync(NewMail(verified: true));

        _notifier.Received(1).Notify(Arg.Is<SupportCaseUpdatedEventArgs>(x =>
            x.CaseId == outcome.CaseId && x.TeamKey == "acme" && x.Change == SupportCaseChange.Raised && x.FromChannel));
    }

    /// <summary>The author has read their own words, exactly as when raising a case on the site.</summary>
    [Fact]
    public async Task ACaseOpenedInATeam_IsReadByItsAuthor()
    {
        Holds(Anna(Acme));

        var outcome = await Build().HandleAsync(NewMail(verified: true));

        await _store.Received(1).MarkReadAsync("acme", outcome.CaseId, Identity, 1, Arg.Any<CancellationToken>());
    }

    /// <summary>It starts as private or shared exactly as a case raised on the site would.</summary>
    [Theory]
    [InlineData(SupportCaseVisibility.Private)]
    [InlineData(SupportCaseVisibility.Team)]
    public async Task UnderPerCaseVisibility_ACaseOpenedInATeamStartsAsASiteCaseDoes(SupportCaseVisibility defaultVisibility)
    {
        Holds(Anna(Acme));

        await Build(new SupportCaseOptions { Visibility = SupportCaseVisibilityMode.PerCase, DefaultVisibility = defaultVisibility })
            .HandleAsync(NewMail(verified: true));

        await _store.Received(1).AddCaseAsync(Arg.Is<SupportCase>(x => x.Visibility == defaultVisibility), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnderTeamWideVisibility_NoVisibilityIsRecorded()
    {
        Holds(Anna(Acme));

        await Build(new SupportCaseOptions { Visibility = SupportCaseVisibilityMode.TeamWide }).HandleAsync(NewMail(verified: true));

        await _store.Received(1).AddCaseAsync(Arg.Is<SupportCase>(x => x.Visibility == null), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// <b>The rule this whole feature is bounded by.</b> Anybody can write anybody's address in a <c>From</c>
    /// header; unverified, the match is only a hint for staff.
    /// </summary>
    [Fact]
    public async Task AForgedFrom_NeverPlacesTheCaseInATeam()
    {
        Holds(Anna(Acme));

        await Build().HandleAsync(NewMail(verified: false));

        await _store.Received(1).AddCaseAsync(
            Arg.Is<SupportCase>(x =>
                x.TeamKey == null &&
                x.AuthorIdentity == null &&
                x.AuthorName == Sender &&
                x.Visibility == null &&
                x.SuggestedTeams.Single().Key == "acme" &&
                x.SuggestionReason == SupportTeamSuggestionReason.SenderNotVerified),
            Arg.Is<SupportMessage>(x => x.AuthorIdentity == null),
            Arg.Any<CancellationToken>());
        await _store.DidNotReceive().MarkReadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        _notifier.Received(1).Notify(Arg.Is<SupportCaseUpdatedEventArgs>(x => x.TeamKey == null));
    }

    [Fact]
    public async Task AVerifiedSenderInTwoTeams_IsSuggestedBoth()
    {
        Holds(Anna(Acme, Beta));

        await Build().HandleAsync(NewMail(verified: true));

        await _store.Received(1).AddCaseAsync(
            Arg.Is<SupportCase>(x =>
                x.TeamKey == null &&
                x.AuthorIdentity == null &&
                x.SuggestedTeams.Select(t => t.Key).SequenceEqual(new[] { "acme", "beta" }) &&
                x.SuggestionReason == SupportTeamSuggestionReason.SeveralTeams),
            Arg.Any<SupportMessage>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// <b>An address two accounts hold belongs to neither.</b> One of them may have claimed an address that is
    /// not theirs, and verifying the mail cannot say which.
    /// </summary>
    [Fact]
    public async Task AnAddressTwoAccountsHold_NeverPlacesTheCase()
    {
        Holds(Anna(Acme), new SupportSender { Identity = "sub-other", Teams = [] });

        await Build().HandleAsync(NewMail(verified: true));

        await _store.Received(1).AddCaseAsync(
            Arg.Is<SupportCase>(x =>
                x.TeamKey == null &&
                x.AuthorIdentity == null &&
                x.SuggestedTeams.Single().Key == "acme" &&
                x.SuggestionReason == SupportTeamSuggestionReason.SharedAddress),
            Arg.Any<SupportMessage>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASenderNobodyKnows_OpensAPlainUnassignedCase()
    {
        await Build().HandleAsync(NewMail(verified: true));

        await _store.Received(1).AddCaseAsync(
            Arg.Is<SupportCase>(x => x.TeamKey == null && x.AuthorIdentity == null && x.SuggestedTeams.Length == 0 && x.SuggestionReason == null),
            Arg.Any<SupportMessage>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAccountInNoTeam_OpensAPlainUnassignedCase()
    {
        Holds(Anna());

        await Build().HandleAsync(NewMail(verified: true));

        await _store.Received(1).AddCaseAsync(
            Arg.Is<SupportCase>(x => x.TeamKey == null && x.AuthorIdentity == null && x.SuggestedTeams.Length == 0),
            Arg.Any<SupportMessage>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoDirectory_EveryNewMailOpensUnassignedAsBefore()
    {
        await Build(withDirectory: false).HandleAsync(NewMail(verified: true));

        await _store.Received(1).AddCaseAsync(
            Arg.Is<SupportCase>(x => x.TeamKey == null && x.SuggestedTeams.Length == 0),
            Arg.Any<SupportMessage>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// <b>The ledger has already recorded the mail</b>, so a directory that throws must not lose it: it opens
    /// unassigned and only the suggestion is missing.
    /// </summary>
    [Fact]
    public async Task ADirectoryThatFails_StillOpensTheCase()
    {
        _directory.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("database down"));

        var outcome = await Build().HandleAsync(NewMail(verified: true));

        Assert.True(outcome.OpenedCase);
        await _store.Received(1).AddCaseAsync(Arg.Is<SupportCase>(x => x.TeamKey == null), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The address is personal data, and logs travel further than mail.</summary>
    [Fact]
    public async Task OpeningACaseInATeam_LogsWithoutTheAddress()
    {
        Holds(Anna(Acme));

        await Build().HandleAsync(NewMail(verified: true));

        var entry = Assert.Single(_logger.Entries, x => x.Level == LogLevel.Information);
        Assert.Contains("acme", entry.Message);
        Assert.DoesNotContain(Sender, entry.Message);
    }

    /// <summary>A reply is matched by its thread and appended; who the sender is in the product is not asked again.</summary>
    [Fact]
    public async Task AReplyOnAKnownThread_DoesNotLookTheSenderUp()
    {
        _store.GetCaseByBindingAsync(SupportChannelType.Email, "thread-1@example.net", Arg.Any<CancellationToken>()).Returns(ExistingCase(authorIdentity: null));

        var outcome = await Build().HandleAsync(Reply(verified: true));

        Assert.True(outcome.WasApplied);
        Assert.False(outcome.OpenedCase);
        await _directory.DidNotReceive().FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// <b>The correspondent is the author</b>, so a reply on a case opened in a team carries the author's
    /// identity. Without it the customer's own reply would read as support having answered, and arm the clock
    /// that closes their case.
    /// </summary>
    [Fact]
    public async Task AReplyOnACaseOpenedInATeam_CountsAsTheAuthors()
    {
        _store.GetCaseByBindingAsync(SupportChannelType.Email, "thread-1@example.net", Arg.Any<CancellationToken>()).Returns(ExistingCase(authorIdentity: Identity));

        await Build().HandleAsync(Reply(verified: false));

        await _store.Received(1).AppendMessageAsync("acme", "case-1", Arg.Is<SupportMessage>(x => x.AuthorIdentity == Identity), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AReplyOnAnUnattributedCase_StaysUnattributed()
    {
        _store.GetCaseByBindingAsync(SupportChannelType.Email, "thread-1@example.net", Arg.Any<CancellationToken>()).Returns(ExistingCase(authorIdentity: null));

        await Build().HandleAsync(Reply(verified: true));

        await _store.Received(1).AppendMessageAsync("acme", "case-1", Arg.Is<SupportMessage>(x => x.AuthorIdentity == null), Arg.Any<CancellationToken>());
    }

    private void Holds(params SupportSender[] senders)
        => _directory.FindAsync(Sender, Arg.Any<CancellationToken>()).Returns(senders);

    private static SupportSender Anna(params SupportCaseTeam[] teams) => new() { Identity = Identity, Name = "Anna Andersson", Teams = teams };

    private static InboundMail NewMail(bool verified) => new(
        MessageId: "first-1@example.com",
        From: Sender,
        DeliveredTo: ["support@example.net"],
        Subject: "Export is empty",
        Body: "Nothing comes out.",
        SentAt: new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero),
        SenderVerified: verified,
        SenderDomain: verified ? "example.com" : null);

    private static InboundMail Reply(bool verified) => NewMail(verified) with
    {
        MessageId = "reply-1@example.com",
        InReplyTo = "thread-1@example.net",
        References = ["thread-1@example.net"],
        Body = "Any news?"
    };

    private static SupportCase ExistingCase(string authorIdentity) => new()
    {
        Id = "case-1",
        TeamKey = "acme",
        AuthorIdentity = authorIdentity,
        AuthorName = "Anna Andersson",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc),
        MessageCount = 1,
        Bindings = [new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = "thread-1@example.net", Address = Sender }]
    };

    private EmailEventHandler Build(SupportCaseOptions caseOptions = null, bool withDirectory = true)
    {
        var ledger = Substitute.For<ISupportEventLedger>();
        ledger.TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var options = new MailOptions { FromAddress = "support@example.net", Recipients = ["example.net"] };

        return new EmailEventHandler(_store, ledger, Options.Create(options), TimeProvider.System, _notifier, _logger,
            senderDirectory: withDirectory ? _directory : null,
            caseOptions: caseOptions == null ? null : Options.Create(caseOptions));
    }

    private sealed class CapturingLogger : ILogger<EmailEventHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
