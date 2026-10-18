using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tharga.Team.Service.Email;
using Tharga.Team.Support.Cases;
using Tharga.Team.Email;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Support mail goes through the same <see cref="IOutboundMailPolicy"/> as invitations (Tharga/Team#290), applied
/// around the transport so the transport itself stays ignorant of Team types.
/// </summary>
public class SupportMailClientPolicyTests
{
    private const string Customer = "kund@customer.example";
    private const string Override = "test-inbox@example.org";
    private const string Subject = "Re: printer";

    private readonly IMailClient _inner = Substitute.For<IMailClient>();

    public SupportMailClientPolicyTests()
    {
        _inner.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("<id@x>"));
    }

    private PolicyGovernedMailClient Client(OutboundMailDecision decision)
    {
        var policy = Substitute.For<IOutboundMailPolicy>();
        policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(decision);

        return new PolicyGovernedMailClient(_inner, policy);
    }

    private static OutboundMail Mail() => new(Customer, Subject, "body", InReplyTo: "<a@b>", ReplyTo: "support+1@example.com");

    [Fact]
    public async Task Delivered_TheMailReachesTheTransportUnchanged()
    {
        var result = await Client(new OutboundMailDecision(OutboundMailAction.Deliver, Customer, Subject)).SendAsync(Mail());

        Assert.True(result.Success);
        await _inner.Received(1).SendAsync(Mail(), Arg.Any<CancellationToken>());
    }

    /// <summary>Only recipient and subject change; threading and reply-to survive, or a redirected reply cannot be tested.</summary>
    [Fact]
    public async Task Redirected_OnlyRecipientAndSubjectChange()
    {
        var redirectedSubject = $"[Staging -> {Customer}] {Subject}";

        await Client(new OutboundMailDecision(OutboundMailAction.Redirect, Override, redirectedSubject)).SendAsync(Mail());

        await _inner.Received(1).SendAsync(Mail() with { To = Override, Subject = redirectedSubject }, Arg.Any<CancellationToken>());
    }

    /// <summary>A withheld reply is recorded as not delivered rather than reported as sent.</summary>
    [Fact]
    public async Task Withheld_TheTransportIsNotCalled_AndSendReportsAFailure()
    {
        var result = await Client(new OutboundMailDecision(OutboundMailAction.Withhold, Customer, Subject)).SendAsync(Mail());

        Assert.False(result.Success);
        await _inner.DidNotReceive().SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An address is personal data, and logs travel further than mail does (CodeQL, PR #305).</summary>
    [Fact]
    public async Task Withheld_TheLogDoesNotContainTheRecipientAddress()
    {
        var policy = Substitute.For<IOutboundMailPolicy>();
        policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(new OutboundMailDecision(OutboundMailAction.Withhold, Customer, Subject));
        var logger = new CapturingLogger();

        await new PolicyGovernedMailClient(_inner, policy, logger).SendAsync(Mail());

        var entry = Assert.Single(logger.Entries);
        Assert.DoesNotContain(Customer, entry);
        Assert.DoesNotContain("customer.example", entry);
    }

    /// <summary>
    /// A redirect is said out loud, so somebody wondering where a test answer went can find out. It names the
    /// override, which is configuration, and not the customer, which is personal data.
    /// </summary>
    [Fact]
    public async Task Redirected_IsLoggedAsInformation_NamingWhereItWentButNotTheCustomer()
    {
        var policy = Substitute.For<IOutboundMailPolicy>();
        policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(new OutboundMailDecision(OutboundMailAction.Redirect, Override, $"[Staging -> {Customer}] {Subject}"));
        var logger = new CapturingLogger();

        await new PolicyGovernedMailClient(_inner, policy, logger).SendAsync(Mail());

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, Assert.Single(logger.Levels));
        Assert.Contains(Override, entry);
        Assert.DoesNotContain(Customer, entry);
        Assert.DoesNotContain("body", entry);
    }

    [Fact]
    public async Task Delivered_LogsNothing()
    {
        var policy = Substitute.For<IOutboundMailPolicy>();
        policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(new OutboundMailDecision(OutboundMailAction.Deliver, Customer, Subject));
        var logger = new CapturingLogger();

        await new PolicyGovernedMailClient(_inner, policy, logger).SendAsync(Mail());

        Assert.Empty(logger.Entries);
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<PolicyGovernedMailClient>
    {
        public List<string> Entries { get; } = [];

        public List<Microsoft.Extensions.Logging.LogLevel> Levels { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Levels.Add(logLevel);
            Entries.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task ReadingIsNotAffected()
    {
        _inner.CanRead.Returns(true);
        var client = Client(new OutboundMailDecision(OutboundMailAction.Withhold, Customer, Subject));

        Assert.True(client.CanRead);
        await client.FetchAsync(default);
        await _inner.Received(1).FetchAsync(Arg.Any<MailFetchPosition>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The registered client is the governed one: nothing resolves the bare transport by accident.</summary>
    [Fact]
    public void TheRegisteredMailClientIsGovernedByThePolicy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddThargaSupportCases(o =>
        {
            o.Email.Smtp.Host = "smtp.example.com";
            o.Email.FromAddress = "support@example.com";
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<PolicyGovernedMailClient>(scope.ServiceProvider.GetRequiredService<IMailClient>());
    }
}
