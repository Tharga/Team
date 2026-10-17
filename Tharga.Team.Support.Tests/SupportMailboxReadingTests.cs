using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Service.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Reading several support mailboxes: each from its own position, each failing on its own, and a mail that reached
/// two of them opening one case that remembers the mailbox it came in through.
/// </summary>
public class SupportMailboxReadingTests
{
    private const string Hosted = "hosted";
    private const string OwnAddress = "support@example.com";
    private const string HostedAddress = "help@example.net";

    private readonly InMemorySupportCaseStore _store = new();
    private readonly ISupportMailPositionStore _positions = Substitute.For<ISupportMailPositionStore>();
    private readonly CapturingLogger<SupportMailPoller> _log = new();

    public SupportMailboxReadingTests()
    {
        _positions.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SupportMailPosition.Start);
    }

    [Fact]
    public void ThePrimarysPositionKey_IsTheOneItAlwaysHad()
    {
        var options = new MailOptions { Recipients = ["example.com"], Folder = "Support" };

        Assert.Equal(SupportMailPoller.PositionKey(options), SupportMailPoller.PositionKey(SupportMailbox.Primary(options)));
    }

    [Fact]
    public void AnAddedMailbox_HasAPositionOfItsOwn_EvenReadingTheSameFolderForTheSameRecipients()
    {
        var options = new MailOptions();

        Assert.NotEqual(SupportMailPoller.PositionKey(SupportMailbox.Primary(options)), SupportMailPoller.PositionKey(new SupportMailbox(Hosted, options)));
        Assert.NotEqual(SupportMailPoller.PositionKey(new SupportMailbox("other", options)), SupportMailPoller.PositionKey(new SupportMailbox(Hosted, options)));
        Assert.Equal(SupportMailPoller.PositionKey(new SupportMailbox("HOSTED", options)), SupportMailPoller.PositionKey(new SupportMailbox(Hosted, options)));
    }

    [Fact]
    public async Task EachMailbox_IsReadFromItsOwnPosition_AndRecordsItsOwn()
    {
        var own = Client(uidValidity: 7, mails: Mail("own-1@example.org", OwnAddress));
        var hosted = Client(uidValidity: 9, mails: Mail("hosted-1@example.org", HostedAddress));
        var poller = Build(own, hosted);
        var ownKey = SupportMailPoller.PositionKey(Own());
        var hostedKey = SupportMailPoller.PositionKey(new SupportMailbox(Hosted, HostedOptions()));
        _positions.GetAsync(ownKey, Arg.Any<CancellationToken>()).Returns(new SupportMailPosition(7, 10));
        _positions.GetAsync(hostedKey, Arg.Any<CancellationToken>()).Returns(new SupportMailPosition(9, 500));

        Assert.Equal(2, await poller.PollAsync(CancellationToken.None));

        await own.Received(1).FetchAsync(new MailFetchPosition(7, 10), Arg.Any<CancellationToken>());
        await hosted.Received(1).FetchAsync(new MailFetchPosition(9, 500), Arg.Any<CancellationToken>());
        await _positions.Received(1).SetAsync(ownKey, new SupportMailPosition(7, 11), Arg.Any<CancellationToken>());
        await _positions.Received(1).SetAsync(hostedKey, new SupportMailPosition(9, 501), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMailboxWhoseReadThrows_CostsTheOthersNothing_AndIsLoggedByName()
    {
        var own = Client(uidValidity: 7, mails: Mail("own-1@example.org", OwnAddress));
        var hosted = Substitute.For<IMailClient>();
        hosted.CanRead.Returns(true);
        hosted.FetchAsync(Arg.Any<MailFetchPosition>(), Arg.Any<CancellationToken>()).Returns<MailFetchResult>(_ => throw new TimeoutException("the server went away"));
        var poller = Build(own, hosted);

        Assert.Equal(1, await poller.PollAsync(CancellationToken.None));

        Assert.Single(await CasesAsync());
        Assert.Contains(_log.Entries, x => x.Level == LogLevel.Error && x.Message.Contains(Hosted));
    }

    /// <summary>A wrong password or a server that is down: the transport says why, the poller names the mailbox, and the password is never written.</summary>
    [Fact]
    public async Task AMailboxThatCannotBeReached_IsLoggedByName_WithoutItsPassword_WhileTheOtherIsRead()
    {
        const string password = "app-password-123";
        var hostedOptions = HostedOptions();
        hostedOptions.Imap.Host = "127.0.0.1";
        hostedOptions.Imap.Port = 1;
        hostedOptions.Imap.UseSsl = false;
        hostedOptions.Imap.UserName = HostedAddress;
        hostedOptions.Imap.Password = password;
        hostedOptions.Timeout = TimeSpan.FromSeconds(5);
        var own = Client(uidValidity: 7, mails: Mail("own-1@example.org", OwnAddress));
        var mailboxes = new SupportMailboxes(Own().Options, [new SupportMailbox(Hosted, hostedOptions)]);
        var poller = Build(mailboxes, new SupportMailClients(own, Substitute.For<IOutboundMailPolicy>()));

        Assert.Equal(1, await poller.PollAsync(CancellationToken.None));

        var warning = Assert.Single(_log.Entries, x => x.Level == LogLevel.Warning);
        Assert.Contains(Hosted, warning.Message);
        Assert.All(_log.Entries, x => Assert.DoesNotContain(password, x.Message));
        await _positions.DidNotReceive().SetAsync(SupportMailPoller.PositionKey(new SupportMailbox(Hosted, hostedOptions)), Arg.Any<SupportMailPosition>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A customer who wrote to both addresses sent one mail with one Message-ID, and it opens one case.</summary>
    [Fact]
    public async Task TheSameMessageInBothMailboxes_OpensOneCase()
    {
        var both = Mail("both-1@example.org", OwnAddress) with { DeliveredTo = [OwnAddress, HostedAddress] };
        var poller = Build(Client(7, both), Client(9, both));

        Assert.Equal(1, await poller.PollAsync(CancellationToken.None));

        var opened = Assert.Single(await CasesAsync());
        Assert.Equal(SupportCaseOptions.PrimaryMailboxName, Assert.Single(opened.Bindings).Mailbox);
    }

    [Fact]
    public async Task ACaseOpenedFromAnAddedMailbox_RemembersItsNameAndAddress()
    {
        var poller = Build(Client(7), Client(9, Mail("hosted-1@example.org", HostedAddress)));

        await poller.PollAsync(CancellationToken.None);

        var binding = Assert.Single(Assert.Single(await CasesAsync()).Bindings);
        Assert.Equal((Hosted, HostedAddress, "customer@example.org"), (binding.Mailbox, binding.MailboxAddress, binding.Address));
    }

    [Fact]
    public async Task ACaseOpenedFromThePrimary_RemembersItToo()
    {
        var poller = Build(Client(7, Mail("own-1@example.org", OwnAddress)), Client(9));

        await poller.PollAsync(CancellationToken.None);

        var binding = Assert.Single(Assert.Single(await CasesAsync()).Bindings);
        Assert.Equal((SupportCaseOptions.PrimaryMailboxName, OwnAddress), (binding.Mailbox, binding.MailboxAddress));
    }

    /// <summary>Each mailbox answers for its own recipients: the primary's filter does not reach the hosted one.</summary>
    [Fact]
    public async Task EachMailboxFiltersByItsOwnRecipients()
    {
        var handler = Handler();
        var toHosted = Mail("hosted-1@example.org", HostedAddress);

        var throughPrimary = await handler.HandleAsync(toHosted, Own(), CancellationToken.None);
        var throughHosted = await handler.HandleAsync(toHosted, HostedMailbox(), CancellationToken.None);

        Assert.Equal("addressed elsewhere", throughPrimary.Reason);
        Assert.True(throughHosted.OpenedCase);
    }

    [Fact]
    public async Task AReplyKeepsTheMailboxItsCaseCameInThrough()
    {
        var handler = Handler();
        var opened = await handler.HandleAsync(Mail("hosted-1@example.org", HostedAddress), HostedMailbox(), CancellationToken.None);

        var reply = await handler.HandleAsync(Mail("hosted-2@example.org", HostedAddress) with { InReplyTo = "hosted-1@example.org" }, HostedMailbox(), CancellationToken.None);

        Assert.Equal(opened.CaseId, reply.CaseId);
        Assert.All((await _store.GetCaseByIdAsync(opened.CaseId)).Bindings, x => Assert.Equal((Hosted, HostedAddress), (x.Mailbox, x.MailboxAddress)));
    }

    /// <summary>An answer one mailbox sent that reaches another is our own mail, never a new case.</summary>
    [Fact]
    public async Task MailFromAnAddedMailboxsAddress_ArrivingInThePrimary_IsOurOwn()
    {
        var outcome = await Handler().HandleAsync(Mail("answer-1@example.net", OwnAddress) with { From = HostedAddress }, Own(), CancellationToken.None);

        Assert.Equal("sent by this instance", outcome.Reason);
        Assert.Empty(await CasesAsync());
    }

    [Fact]
    public async Task APerCaseAddressOfAnAddedMailbox_FindsTheCase()
    {
        var handler = Handler();
        var opened = await handler.HandleAsync(Mail("hosted-1@example.org", HostedAddress), HostedMailbox(), CancellationToken.None);

        var reply = await handler.HandleAsync(Mail("hosted-2@example.org", $"help+{opened.CaseId}@example.net"), HostedMailbox(), CancellationToken.None);

        Assert.Equal(opened.CaseId, reply.CaseId);
        Assert.False(reply.OpenedCase);
    }

    [Fact]
    public async Task AMailboxsOwnFileLimits_ApplyToTheMailItReads()
    {
        var files = new InMemorySupportAttachmentStore();
        var hosted = new SupportMailbox(Hosted, HostedOptions());
        hosted.Options.MaxAttachmentCount = 1;
        var handler = Handler(files);
        var mail = Mail("hosted-1@example.org", HostedAddress) with
        {
            Attachments = [new InboundAttachment("a.txt", "text/plain", [1]), new InboundAttachment("b.txt", "text/plain", [2])]
        };

        var outcome = await handler.HandleAsync(mail, hosted, CancellationToken.None);

        Assert.Single(files.Files);
        Assert.Contains("b.txt was not stored", Assert.Single((await _store.GetMessagesAsync(null, outcome.CaseId, null, 10)).Items).Body);
    }

    private async Task<SupportCase[]> CasesAsync() => (await _store.GetUnassignedCasesAsync(null, 100)).Items;

    private static InboundMail Mail(string messageId, string deliveredTo) => new(
        MessageId: messageId,
        From: "customer@example.org",
        DeliveredTo: [deliveredTo],
        Subject: "Export is empty",
        Body: "Nothing comes out.",
        SentAt: new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));

    private static SupportMailbox Own() => SupportMailbox.Primary(new MailOptions { FromAddress = OwnAddress, Recipients = ["example.com"] });

    private static MailOptions HostedOptions() => new() { FromAddress = HostedAddress, FromName = "Help" };

    private static SupportMailbox HostedMailbox() => new(Hosted, HostedOptions());

    private static IMailClient Client(uint uidValidity, params InboundMail[] mails)
    {
        var client = Substitute.For<IMailClient>();
        client.CanRead.Returns(true);
        client.FetchAsync(Arg.Any<MailFetchPosition>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var from = call.Arg<MailFetchPosition>();
                return new MailFetchResult(new MailFetchPosition(uidValidity, from.LastUid + (uint)mails.Length), mails);
            });

        return client;
    }

    private EmailEventHandler Handler(ISupportAttachmentStore files = null)
    {
        var mailboxes = new SupportMailboxes(Own().Options, [HostedMailbox()]);

        return new EmailEventHandler(_store, new InMemoryLedger(), Options.Create(mailboxes.Primary.Options), TimeProvider.System, attachmentStore: files, mailboxes: mailboxes);
    }

    private SupportMailPoller Build(IMailClient own, IMailClient hosted)
    {
        var mailboxes = new SupportMailboxes(Own().Options, [HostedMailbox()]);

        return Build(mailboxes, new FakeClients(new Dictionary<string, IMailClient> { [SupportCaseOptions.PrimaryMailboxName] = own, [Hosted] = hosted }));
    }

    private SupportMailPoller Build(SupportMailboxes mailboxes, ISupportMailClients clients)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISupportCaseStore>(_store);
        services.AddSingleton<ISupportEventLedger>(new InMemoryLedger());
        services.AddSingleton(_positions);
        services.AddSingleton(clients);
        services.AddSingleton(mailboxes);
        services.AddSingleton(Options.Create(mailboxes.Primary.Options));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<EmailEventHandler>();

        return new SupportMailPoller(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Options.Create(mailboxes.Primary.Options), _log, mailboxes);
    }

    private sealed class FakeClients(Dictionary<string, IMailClient> byName) : ISupportMailClients
    {
        public IMailClient For(SupportMailbox mailbox) => byName.GetValueOrDefault(mailbox.Name);
    }
}

/// <summary>A ledger in memory that refuses an id it has seen, as the shared one does.</summary>
internal sealed class InMemoryLedger : ISupportEventLedger
{
    private readonly HashSet<(string, string)> _seen = [];

    public Task<bool> TryRecordAsync(string source, string eventId, CancellationToken cancellationToken = default)
    {
        lock (_seen) return Task.FromResult(_seen.Add((source, eventId)));
    }
}

/// <summary>A logger that keeps what it was given, so a test can read what was said and at what level.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        lock (Entries) Entries.Add((logLevel, formatter(state, exception)));
    }
}
