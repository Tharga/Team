using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A sender rule on a mailbox keeps automated mail from becoming a support case (Tharga/Team#449).
/// </summary>
/// <remarks>
/// <b>The event is the point.</b> A host announcing on <see cref="SupportCaseChange.Raised"/> must stay silent for a
/// matched sender in either action, with no code of its own. A rule that opened the case and closed it afterwards
/// would still raise it, which is why these assert on the notifier and not only on the store.
/// </remarks>
public class EmailSenderRuleTests
{
    private const string Dmarc = "noreply-dmarc-support@google.com";

    private static InboundMail Report(string from = Dmarc) => new(
        MessageId: "report-1@google.com",
        From: from,
        DeliveredTo: ["support@example.net"],
        Subject: "Report domain: example.net",
        Body: "Your DMARC aggregate report is attached.",
        SentAt: new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero));

    private static MailOptions RuleOptions(MailSenderAction action = MailSenderAction.Ignore, params string[] senders)
        => new()
        {
            FromAddress = "support@example.net",
            Recipients = ["example.net"],
            SenderRules = [new MailSenderRule { Senders = senders.Length == 0 ? [Dmarc] : senders, Action = action }]
        };

    private static (EmailEventHandler Handler, ISupportCaseStore Store, ISupportCaseNotifier Notifier, ISupportEventLedger Ledger, CapturingLogger Logger) Build(
        MailOptions primary, params SupportMailbox[] additional)
    {
        var store = Substitute.For<ISupportCaseStore>();
        var ledger = Substitute.For<ISupportEventLedger>();
        ledger.TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var notifier = Substitute.For<ISupportCaseNotifier>();
        var logger = new CapturingLogger();

        var handler = new EmailEventHandler(store, ledger, Microsoft.Extensions.Options.Options.Create(primary), TimeProvider.System, notifier, logger,
            mailboxes: new SupportMailboxes(primary, additional));

        return (handler, store, notifier, ledger, logger);
    }


    [Fact]
    public async Task Ignore_CreatesNoCase_AndRaisesNoEvent()
    {
        var (handler, store, notifier, _, _) = Build(RuleOptions(MailSenderAction.Ignore));

        var outcome = await handler.HandleAsync(Report());

        Assert.False(outcome.WasApplied);
        Assert.Equal("sender rule", outcome.Reason);
        await store.DidNotReceiveWithAnyArgs().AddCaseAsync(default, default, default);
        notifier.DidNotReceiveWithAnyArgs().Notify(default);
    }

    [Fact]
    public async Task Close_StoresAClosedCase_AndRaisesNoRaisedEvent()
    {
        var (handler, store, notifier, _, _) = Build(RuleOptions(MailSenderAction.Close));

        var outcome = await handler.HandleAsync(Report());

        Assert.True(outcome.WasApplied);
        await store.Received(1).AddCaseAsync(
            Arg.Is<SupportCase>(x => x.Status == SupportCaseStatus.Closed && x.ClosedAt != null && x.ClosedBy == SupportCaseActors.SenderRule && x.TeamKey == null),
            Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
        notifier.DidNotReceiveWithAnyArgs().Notify(default);
    }

    /// <summary>The mail id is recorded either way, so the mail is not reconsidered.</summary>
    [Theory]
    [InlineData(MailSenderAction.Ignore)]
    [InlineData(MailSenderAction.Close)]
    public async Task TheMessageIdIsRecorded(MailSenderAction action)
    {
        var (handler, _, _, ledger, _) = Build(RuleOptions(action));

        await handler.HandleAsync(Report());

        await ledger.Received(1).TryRecordAsync("email", "report-1@google.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMatchedMail_IsLoggedOnceAtInformation_NamingSenderAndRule()
    {
        var (handler, _, _, _, logger) = Build(RuleOptions(MailSenderAction.Ignore));

        await handler.HandleAsync(Report());

        var information = logger.Lines.Where(x => x.Level == LogLevel.Information).Select(x => x.Message).ToArray();

        var line = Assert.Single(information);
        Assert.Contains(Dmarc, line);
        Assert.Contains("Ignore", line);
    }

    [Fact]
    public async Task AnUnmatchedSender_OpensACaseAsToday()
    {
        var (handler, store, notifier, _, _) = Build(RuleOptions(MailSenderAction.Ignore));

        var outcome = await handler.HandleAsync(Report("someone@example.com"));

        Assert.True(outcome.OpenedCase);
        await store.Received(1).AddCaseAsync(Arg.Is<SupportCase>(x => x.Status == SupportCaseStatus.Open), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
        notifier.Received(1).Notify(Arg.Is<SupportCaseUpdatedEventArgs>(x => x.Change == SupportCaseChange.Raised));
    }

    [Theory]
    [InlineData("NoReply-DMARC-Support@Google.com")]
    [InlineData("  noreply-dmarc-support@google.com ")]
    [InlineData("DMARC Reports <noreply-dmarc-support@google.com>")]
    public async Task AddressMatching_IgnoresCase_AndTheDisplayName(string from)
    {
        var (handler, store, _, _, _) = Build(RuleOptions(MailSenderAction.Ignore));

        var outcome = await handler.HandleAsync(Report(from));

        Assert.Equal("sender rule", outcome.Reason);
        await store.DidNotReceiveWithAnyArgs().AddCaseAsync(default, default, default);
    }

    [Fact]
    public async Task AWildcardDomain_MatchesEveryAddressAtIt_AndNoOther()
    {
        var (handler, store, _, _, _) = Build(RuleOptions(MailSenderAction.Ignore, "*@Google.com"));

        Assert.Equal("sender rule", (await handler.HandleAsync(Report("anyone@google.com"))).Reason);
        Assert.NotEqual("sender rule", (await handler.HandleAsync(Report("anyone@mail.google.com") with { MessageId = "other-1" })).Reason);
        Assert.NotEqual("sender rule", (await handler.HandleAsync(Report("anyone@notgoogle.com") with { MessageId = "other-2" })).Reason);
    }

    [Fact]
    public async Task ARuleOnThePrimaryMailbox_SaysNothingAboutAnAddedOne()
    {
        var added = new SupportMailbox("sales", new MailOptions { FromAddress = "sales@example.net", Recipients = ["example.net"] });
        var (handler, store, _, _, _) = Build(RuleOptions(MailSenderAction.Ignore), added);

        var outcome = await handler.HandleAsync(Report(), added);

        Assert.True(outcome.OpenedCase);
        await store.Received(1).AddCaseAsync(Arg.Any<SupportCase>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARuleSetOnAnAddedMailbox_AppliesThere()
    {
        var added = new SupportMailbox("sales", RuleOptions(MailSenderAction.Ignore));
        var (handler, store, _, _, _) = Build(new MailOptions { FromAddress = "support@example.net", Recipients = ["example.net"] }, added);

        var outcome = await handler.HandleAsync(Report(), added);

        Assert.Equal("sender rule", outcome.Reason);
        await store.DidNotReceiveWithAnyArgs().AddCaseAsync(default, default, default);
    }

    /// <summary>A host binds the rule from its settings; the action is read by name.</summary>
    [Fact]
    public void TheRule_BindsFromConfiguration()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["SenderRules:0:Senders:0"] = Dmarc,
                ["SenderRules:0:Senders:1"] = "*@example.org",
                ["SenderRules:0:Action"] = "Close",
            })
            .Build();

        var options = new MailOptions();
        Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(configuration, options);

        var rule = Assert.Single(options.SenderRules);
        Assert.Equal(MailSenderAction.Close, rule.Action);
        Assert.Equal([Dmarc, "*@example.org"], rule.Senders);
    }

    /// <summary>Records what is logged; the handler's logger type is internal, so it cannot be substituted.</summary>
    internal sealed class CapturingLogger : ILogger<EmailEventHandler>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Lines.Add((logLevel, formatter(state, exception)));
    }
}
