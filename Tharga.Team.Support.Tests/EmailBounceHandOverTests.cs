using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>A bounce that names no support answer is handed to the registered bounce handlers in turn, while a bounce for a support answer stays support's.</summary>
public class EmailBounceHandOverTests
{
    private const string TeamKey = "team-1";
    private const string CaseId = "case-1";
    private const string AnswerId = "answer-2@example.com";
    private const string UserMailId = "user-mail-7@example.com";
    private const string Reason = "550 5.1.1 <anna@example.org>: Recipient address rejected";

    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static InboundMail Bounce(string bouncedId, string reason = Reason) => new(
        MessageId: "report-1@mx.example.net",
        From: "mailer-daemon@mx.example.net",
        DeliveredTo: ["hello@example.com"],
        Subject: "Undelivered Mail Returned to Sender",
        Body: "This is the mail system.",
        SentAt: Now,
        IsAutomated: true,
        BouncedMessageId: bouncedId,
        BounceReason: reason);

    private static SupportCase Case() => new()
    {
        Id = CaseId,
        TeamKey = TeamKey,
        AuthorIdentity = null,
        AuthorName = "anna@example.org",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = Now.UtcDateTime,
        MessageCount = 2,
        Bindings = [new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = AnswerId, Address = "anna@example.org", MessageSequence = 2 }]
    };

    private static (EmailEventHandler Handler, ISupportCaseStore Store) Build(IEnumerable<IMailBounceHandler> handlers, ILogger<EmailEventHandler> logger = null)
    {
        var store = Substitute.For<ISupportCaseStore>();
        store.GetCaseByBindingAsync(SupportChannelType.Email, AnswerId, Arg.Any<CancellationToken>()).Returns(Case());

        var ledger = Substitute.For<ISupportEventLedger>();
        ledger.TryRecordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(Now);

        var options = new MailOptions { FromAddress = "hello@example.com", Recipients = ["example.com"] };

        return (new EmailEventHandler(store, ledger, Options.Create(options), time, logger: logger, bounceHandlers: handlers), store);
    }

    private static IMailBounceHandler Handler(bool recognises)
    {
        var handler = Substitute.For<IMailBounceHandler>();
        handler.HandleAsync(Arg.Any<MailBounce>(), Arg.Any<CancellationToken>()).Returns(recognises);
        return handler;
    }

    [Fact]
    public async Task ABounceForNoSupportAnswer_IsHandedToTheHandlers_WithTheMailItNamesWhyAndWhen()
    {
        var handler = Handler(true);
        var (events, store) = Build([handler]);

        var outcome = await events.HandleAsync(Bounce(UserMailId));

        Assert.True(outcome.HandedOver);
        Assert.False(outcome.WasApplied);
        await handler.Received(1).HandleAsync(Arg.Is<MailBounce>(x => x.OriginalMessageId == UserMailId && x.Reason == Reason && x.ReceivedAt == Now.UtcDateTime), Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().AddCaseAsync(default, default, default);
        await store.DidNotReceiveWithAnyArgs().SetMessageDeliveryAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task ABounceForASupportAnswer_StaysSupports_AndNoHandlerIsAsked()
    {
        var handler = Handler(true);
        var (events, store) = Build([handler]);

        var outcome = await events.HandleAsync(Bounce(AnswerId));

        Assert.True(outcome.Bounced);
        await store.Received(1).SetMessageDeliveryAsync(TeamKey, CaseId, 2, SupportMessageDelivery.Failed, Reason, Arg.Any<CancellationToken>());
        await handler.DidNotReceiveWithAnyArgs().HandleAsync(default, default);
    }

    [Fact]
    public async Task HandlersAreAskedInTurn_UntilOneRecognisesTheMail()
    {
        var first = Handler(false);
        var second = Handler(true);
        var third = Handler(true);
        var (events, _) = Build([first, second, third]);

        await events.HandleAsync(Bounce(UserMailId));

        await first.ReceivedWithAnyArgs(1).HandleAsync(default, default);
        await second.ReceivedWithAnyArgs(1).HandleAsync(default, default);
        await third.DidNotReceiveWithAnyArgs().HandleAsync(default, default);
    }

    [Fact]
    public async Task AHandlerThatFails_DoesNotKeepTheBounceFromTheNext()
    {
        var broken = Substitute.For<IMailBounceHandler>();
        broken.HandleAsync(Arg.Any<MailBounce>(), Arg.Any<CancellationToken>()).Returns<bool>(_ => throw new InvalidOperationException("The store is down."));
        var working = Handler(true);
        var (events, _) = Build([broken, working]);

        var outcome = await events.HandleAsync(Bounce(UserMailId));

        Assert.True(outcome.HandedOver);
        await working.ReceivedWithAnyArgs(1).HandleAsync(default, default);
    }

    [Fact]
    public async Task ABounceNobodyRecognises_IsIgnored_AndLoggedAtDebug_WithoutAnAddress()
    {
        var logger = new RecordingLogger();
        var (events, store) = Build([Handler(false)], logger);

        var outcome = await events.HandleAsync(Bounce("nobody-knows@example.net"));

        Assert.False(outcome.WasApplied);
        Assert.False(outcome.HandedOver);
        await store.DidNotReceiveWithAnyArgs().AddCaseAsync(default, default, default);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.DoesNotContain("@", entry.Message);
    }

    [Fact]
    public async Task WithNoHandlerRegistered_ABounceForNoSupportAnswer_IsIgnoredAsBefore()
    {
        var (events, store) = Build(null);

        var outcome = await events.HandleAsync(Bounce(UserMailId));

        Assert.False(outcome.WasApplied);
        Assert.False(outcome.HandedOver);
        await store.DidNotReceiveWithAnyArgs().AddCaseAsync(default, default, default);
    }

    [Fact]
    public async Task TheHandlerResolvedFromTheContainer_HandsBouncesToTheRegisteredHandlers()
    {
        var registered = Handler(true);
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ISupportCaseStore>());
        services.AddSingleton(Substitute.For<ISupportEventLedger>());
        services.AddSingleton(Options.Create(new MailOptions { FromAddress = "hello@example.com" }));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<EmailEventHandler>();
        services.AddScoped(_ => registered);

        using var scope = services.BuildServiceProvider().CreateScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<EmailEventHandler>().HandleAsync(Bounce(UserMailId));

        Assert.True(outcome.HandedOver);
        await registered.ReceivedWithAnyArgs(1).HandleAsync(default, default);
    }

    private sealed class RecordingLogger : ILogger<EmailEventHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
