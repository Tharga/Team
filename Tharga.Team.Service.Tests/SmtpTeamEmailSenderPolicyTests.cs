using Microsoft.Extensions.Options;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Service.Tests;

/// <summary>The invitation sender applies <see cref="IOutboundMailPolicy"/> before anything reaches SMTP.</summary>
public class SmtpTeamEmailSenderPolicyTests
{
    private const string Customer = "kund@kommun.se";
    private const string Override = "test-inbox@eplicta.se";
    private const string RedirectedSubject = "[Staging -> kund@kommun.se] You've been invited to join Acme";

    private static SmtpTeamEmailSender Sender(OutboundMailDecision decision)
    {
        var policy = Substitute.For<IOutboundMailPolicy>();
        policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(decision);

        var options = Options.Create(new EmailOptions { SmtpHost = "smtp.example.com", FromAddress = "noreply@example.com" });
        return new SmtpTeamEmailSender(options, policy);
    }

    [Fact]
    public void Delivered_TheMessageGoesToTheIntendedRecipient()
    {
        var sender = Sender(new OutboundMailDecision(OutboundMailAction.Deliver, Customer, "You've been invited to join Acme"));

        using var message = sender.CreateMessage(Customer, "Kund", "https://example.com/i", "Acme");

        Assert.Equal(Customer, Assert.Single(message.To).Address);
        Assert.Equal("You've been invited to join Acme", message.Subject);
    }

    [Fact]
    public void Redirected_TheMessageGoesToTheOverrideAddress_WithTheDecidedSubject()
    {
        var sender = Sender(new OutboundMailDecision(OutboundMailAction.Redirect, Override, RedirectedSubject));

        using var message = sender.CreateMessage(Customer, "Kund", "https://example.com/i", "Acme");

        Assert.Equal(Override, Assert.Single(message.To).Address);
        Assert.Equal(RedirectedSubject, message.Subject);
    }

    [Fact]
    public void Withheld_NoMessageIsCreated()
    {
        var sender = Sender(new OutboundMailDecision(OutboundMailAction.Withhold, Customer, "You've been invited to join Acme"));

        Assert.Null(sender.CreateMessage(Customer, "Kund", "https://example.com/i", "Acme"));
    }

    /// <summary>A withheld invitation is not an error: the invitation exists and its link can be copied.</summary>
    [Fact]
    public async Task Withheld_SendingCompletesWithoutContactingSmtp()
    {
        var sender = Sender(new OutboundMailDecision(OutboundMailAction.Withhold, Customer, "You've been invited to join Acme"));

        await sender.SendInviteAsync(Customer, "Kund", "https://example.com/i", "Acme");
    }

    [Fact]
    public void ThePolicyIsAskedAboutTheIntendedRecipientAndSubject()
    {
        var policy = Substitute.For<IOutboundMailPolicy>();
        policy.Decide(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new OutboundMailDecision(OutboundMailAction.Withhold, Customer, "x"));
        var sender = new SmtpTeamEmailSender(
            Options.Create(new EmailOptions { SmtpHost = "smtp.example.com", FromAddress = "noreply@example.com" }), policy);

        sender.CreateMessage(Customer, "Kund", "https://example.com/i", "Acme");

        policy.Received(1).Decide(Customer, "You've been invited to join Acme");
    }
}
