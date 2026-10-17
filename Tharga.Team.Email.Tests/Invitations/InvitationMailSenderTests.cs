using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Invitations;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Email.Tests.Invitations;

/// <summary>Invitations go through the shared transport, after <see cref="IOutboundMailPolicy"/> has decided where they may go.</summary>
public class InvitationMailSenderTests
{
    private const string Customer = "kund@kommun.se";
    private const string Override = "test-inbox@eplicta.se";
    private const string RedirectedSubject = "[Staging -> kund@kommun.se] You've been invited to join Acme";

    private static readonly EmailOptions Settings = new() { SmtpHost = "smtp.example.com", FromAddress = "noreply@example.com", FromName = "Acme" };

    private static InvitationMailSender Sender(OutboundMailDecision decision, ILogger<InvitationMailSender> logger = null)
    {
        var policy = Substitute.For<IOutboundMailPolicy>();
        policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(decision);
        return new InvitationMailSender(Options.Create(Settings), policy, logger);
    }

    private sealed class FakeSender : ITeamEmailSender
    {
        public Task SendInviteAsync(TeamInviteMail mail) => Task.CompletedTask;
    }

    private sealed class CapturingLogger : ILogger<InvitationMailSender>
    {
        public List<string> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) => Entries.Add(formatter(state, exception));
    }

    [Fact]
    public void Delivered_TheMailGoesToTheIntendedRecipient_UnderTheirName()
    {
        var mail = Sender(new OutboundMailDecision(OutboundMailAction.Deliver, Customer, "You've been invited to join Acme")).CreateMail(Customer, "Kund Kundsson", "https://example.com/i", "Acme");

        Assert.Equal("\"Kund Kundsson\" <kund@kommun.se>", mail.Value.To);
        Assert.Equal("You've been invited to join Acme", mail.Value.Subject);
        Assert.Contains("https://example.com/i", mail.Value.Body);
    }

    [Fact]
    public void Redirected_TheMailGoesToTheOverrideAddress_WithTheDecidedSubject()
    {
        var mail = Sender(new OutboundMailDecision(OutboundMailAction.Redirect, Override, RedirectedSubject)).CreateMail(Customer, "Kund", "https://example.com/i", "Acme");

        Assert.Equal(Override, mail.Value.To);
        Assert.Equal(RedirectedSubject, mail.Value.Subject);
    }

    [Fact]
    public void Withheld_NoMailIsMade()
    {
        Assert.Null(Sender(new OutboundMailDecision(OutboundMailAction.Withhold, Customer, "x")).CreateMail(Customer, "Kund", "https://example.com/i", "Acme"));
    }

    /// <summary>A withheld invitation is not an error: the invitation exists and its link can be copied.</summary>
    [Fact]
    public async Task Withheld_SendingCompletes_WithoutContactingAnyServer()
    {
        await Sender(new OutboundMailDecision(OutboundMailAction.Withhold, Customer, "x")).SendInviteAsync(new TeamInviteMail { RecipientEmail = Customer, RecipientName = "Kund", InviteLink = "https://example.com/i", TeamKey = "acme", TeamName = "Acme" });
    }

    /// <summary>An address is personal data, and logs travel further than mail does.</summary>
    [Fact]
    public void Withheld_TheLogDoesNotContainTheRecipientAddress()
    {
        var logger = new CapturingLogger();

        Sender(new OutboundMailDecision(OutboundMailAction.Withhold, Customer, "x"), logger).CreateMail(Customer, "Kund", "https://example.com/i", "Acme");

        var entry = Assert.Single(logger.Entries);
        Assert.DoesNotContain(Customer, entry);
        Assert.DoesNotContain("kommun.se", entry);
        Assert.Contains("Acme", entry);
    }

    [Fact]
    public void ThePolicyIsAskedAboutTheIntendedRecipientAndSubject()
    {
        var policy = Substitute.For<IOutboundMailPolicy>();
        policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(new OutboundMailDecision(OutboundMailAction.Withhold, Customer, "x"));

        new InvitationMailSender(Options.Create(Settings), policy).CreateMail(Customer, "Kund", "https://example.com/i", "Acme");

        policy.Received(1).Decide(Customer, "You've been invited to join Acme");
    }

    [Fact]
    public async Task WithNoSmtpHost_SendingRefuses()
    {
        var sender = new InvitationMailSender(Options.Create(new EmailOptions()), Substitute.For<IOutboundMailPolicy>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendInviteAsync(new TeamInviteMail { RecipientEmail = Customer, RecipientName = "Kund", InviteLink = "https://example.com/i", TeamKey = "acme", TeamName = "Acme" }));
    }

    /// <summary>The transport reports a failure as a result; the invite dialog learns of it only if the sender throws.</summary>
    [Fact]
    public async Task ASendTheServerRefuses_Throws_SoTheInviterIsTold()
    {
        var settings = new EmailOptions { SmtpHost = "127.0.0.1", SmtpPort = 1, UseSsl = false, FromAddress = "noreply@example.com" };
        var policy = Substitute.For<IOutboundMailPolicy>();
        policy.Decide(Arg.Any<string>(), Arg.Any<string>()).Returns(new OutboundMailDecision(OutboundMailAction.Deliver, Customer, "x"));

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => new InvitationMailSender(Options.Create(settings), policy).SendInviteAsync(new TeamInviteMail { RecipientEmail = Customer, RecipientName = "Kund", InviteLink = "https://example.com/i", TeamKey = "acme", TeamName = "Acme" }));

        Assert.StartsWith("The invitation could not be sent", refusal.Message);
    }

    [Fact]
    public void TheSettingsReachTheTransport()
    {
        var mail = InvitationMailSender.ToMailOptions(new EmailOptions { SmtpHost = "smtp.example.com", SmtpPort = 465, UseSsl = true, Username = "u", Password = "p", FromAddress = "noreply@example.com", FromName = "Acme" });

        Assert.Equal(("smtp.example.com", 465, true, "u", "p"), (mail.Smtp.Host, mail.Smtp.Port, mail.Smtp.UseSsl, mail.Smtp.UserName, mail.Smtp.Password));
        Assert.Equal(("noreply@example.com", "Acme"), (mail.FromAddress, mail.FromName));
    }

    [Theory]
    [InlineData(587, true, SecureSocketOptions.StartTls)]
    [InlineData(25, true, SecureSocketOptions.StartTls)]
    [InlineData(465, true, SecureSocketOptions.SslOnConnect)]
    [InlineData(993, true, SecureSocketOptions.SslOnConnect)]
    [InlineData(587, false, SecureSocketOptions.StartTlsWhenAvailable)]
    public void UseSsl_OnASubmissionPort_RequiresStartTls_AndElsewhere_TlsFromTheFirstByte(int port, bool useSsl, SecureSocketOptions expected)
    {
        Assert.Equal(expected, MailClient.SecurityOf(new MailServerOptions { Host = "mail.example.com", Port = port, UseSsl = useSsl }));
    }

    [Fact]
    public void Registering_AddsTheSender_AndThePolicyItObeys()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddThargaEmailInvitations(o =>
        {
            o.SmtpHost = "smtp.example.com";
            o.FromAddress = "noreply@example.com";
        });

        using var provider = services.BuildServiceProvider();
        Assert.IsType<InvitationMailSender>(provider.GetRequiredService<ITeamEmailSender>());
        Assert.NotNull(provider.GetService<IOutboundMailPolicy>());
        Assert.Equal("smtp.example.com", provider.GetRequiredService<IOptions<EmailOptions>>().Value.SmtpHost);
    }

    [Fact]
    public void ASenderTheHostRegistered_Wins_WhicheverIsRegisteredFirst()
    {
        var before = new ServiceCollection();
        before.AddScoped<ITeamEmailSender, FakeSender>();
        before.AddThargaEmailInvitations();

        var after = new ServiceCollection();
        after.AddThargaEmailInvitations();
        after.AddScoped<ITeamEmailSender, FakeSender>();

        Assert.IsType<FakeSender>(before.BuildServiceProvider().GetRequiredService<ITeamEmailSender>());
        Assert.IsType<FakeSender>(after.BuildServiceProvider().GetRequiredService<ITeamEmailSender>());
    }
}
