using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team;

namespace Tharga.Team.Service.Email;

/// <summary>
/// Sends invitations over SMTP, through <see cref="IOutboundMailPolicy"/>: outside production an invitation reaches
/// only an allowed domain or the override address, and is otherwise withheld.
/// </summary>
public class SmtpTeamEmailSender : ITeamEmailSender
{
    private readonly EmailOptions _options;
    private readonly IOutboundMailPolicy _policy;
    private readonly ILogger<SmtpTeamEmailSender> _logger;

    /// <summary>
    /// Constructs a sender with no host environment, which the policy treats as not production: invitations are
    /// withheld. Resolve the sender from the container to have the environment applied.
    /// </summary>
    public SmtpTeamEmailSender(IOptions<EmailOptions> options)
        : this(options, new OutboundMailPolicy(Options.Create(new OutboundMailPolicyOptions())))
    {
    }

    public SmtpTeamEmailSender(IOptions<EmailOptions> options, IOutboundMailPolicy policy, ILogger<SmtpTeamEmailSender> logger = null)
    {
        _options = options.Value;
        _policy = policy;
        _logger = logger;
    }

    public async Task SendInviteAsync(string recipientEmail, string recipientName, string inviteLink, string teamName)
    {
        if (string.IsNullOrWhiteSpace(_options.SmtpHost))
            throw new InvalidOperationException("SMTP host is not configured.");

        using var message = CreateMessage(recipientEmail, recipientName, inviteLink, teamName);
        if (message == null) return;

        // Properties are assigned after the using declaration rather than in an
        // object initializer, so the client is tracked for disposal from the
        // moment it is constructed.
        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort);
        client.EnableSsl = _options.UseSsl;
        client.Credentials = !string.IsNullOrEmpty(_options.Username)
            ? new NetworkCredential(_options.Username, _options.Password)
            : null;

        await client.SendMailAsync(message);
    }

    /// <summary>The invitation as the policy allows it to be sent, or null when it is withheld.</summary>
    internal MailMessage CreateMessage(string recipientEmail, string recipientName, string inviteLink, string teamName)
    {
        var decision = _policy.Decide(recipientEmail, $"You've been invited to join {teamName}");
        if (!decision.ShouldSend)
        {
            _logger?.LogWarning(
                "An invitation to {Recipient} was not sent: outside production, mail reaches only an allowed domain or " +
                "the override address, and neither applies. Set {Section} to receive it. The invitation itself exists.",
                recipientEmail, OutboundMailPolicyOptions.SectionName);
            return null;
        }

        var from = new MailAddress(_options.FromAddress, _options.FromName);
        var to = decision.Action == OutboundMailAction.Deliver
            ? new MailAddress(decision.Recipient, recipientName)
            : new MailAddress(decision.Recipient);

        var message = new MailMessage(from, to);
        message.Subject = decision.Subject;
        message.Body = $"""
            Hi {recipientName},

            You have been invited to join the team "{teamName}".

            Click the link below to accept the invitation:
            {inviteLink}

            If you did not expect this invitation, you can safely ignore this email.
            """;
        message.IsBodyHtml = false;

        return message;
    }
}
