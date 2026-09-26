using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tharga.Team.Service.Email;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Support mail goes through the same <see cref="IOutboundMailPolicy"/> as invitations (Tharga/Team#290), applied
/// around the transport so the transport itself stays ignorant of Team types.
/// </summary>
public class SupportMailClientPolicyTests
{
    private const string Customer = "kund@kommun.se";
    private const string Override = "test-inbox@eplicta.se";
    private const string Subject = "Re: printer";

    private readonly ISupportMailClient _inner = Substitute.For<ISupportMailClient>();

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

        Assert.IsType<PolicyGovernedMailClient>(scope.ServiceProvider.GetRequiredService<ISupportMailClient>());
    }
}
