using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Email.Invitations;

/// <summary>
/// Sends the invitation email through the same mail transport as everything else, after the outbound mail policy has decided where it may go.
/// </summary>
/// <remarks>
/// <b>Its own sending address</b>, read from <see cref="EmailOptions"/> and never from support's settings; set it to
/// an address a reply reaches. Once mail to users is registered, invitations go through its outbox instead and this
/// sender is not used.
/// <para>
/// <b>A failed send throws.</b> The transport reports failures as results; the invite dialog tells the inviter
/// the mail did not go out only if this throws, so the result is turned back into an exception here.
/// </para>
/// </remarks>
internal sealed class InvitationMailSender(IOptions<EmailOptions> options, IOutboundMailPolicy policy, ILogger<InvitationMailSender> logger = null, ILogger<MailClient> clientLogger = null, MailTemplate template = null) : ITeamEmailSender
{
    public async Task SendInviteAsync(TeamInviteMail invite)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.SmtpHost)) throw new InvalidOperationException("SMTP host is not configured.");

        var mail = CreateMail(invite.RecipientEmail, invite.RecipientName, invite.InviteLink, invite.TeamName);
        if (mail == null) return;

        var result = await new MailClient(Options.Create(ToMailOptions(settings)), clientLogger).SendAsync(mail.Value);
        if (!result.Success) throw new InvalidOperationException($"The invitation could not be sent: {result.Error}");
    }

    internal OutboundMail? CreateMail(string recipientEmail, string recipientName, string inviteLink, string teamName)
    {
        var subject = $"You've been invited to join {teamName}";
        var decision = policy.Decide(recipientEmail, subject);
        if (!decision.ShouldSend)
        {
            // The recipient is deliberately not logged: an address is personal data, and logs travel further than mail.
            logger?.LogWarning(
                "An invitation to team '{Team}' was not sent: outside production, mail reaches only an allowed domain or " +
                "the override address, and neither applies. Set {Section} to receive it. The invitation itself exists.",
                teamName, OutboundMailPolicyOptions.SectionName);
            return null;
        }

        var to = decision.Action == OutboundMailAction.Deliver && !string.IsNullOrWhiteSpace(recipientName)
            ? new MailboxAddress(recipientName, decision.Recipient).ToString()
            : decision.Recipient;

        var text = $"""
            Hi {recipientName},

            You have been invited to join the team "{teamName}".

            Click the link below to accept the invitation:
            {inviteLink}

            If you did not expect this invitation, you can safely ignore this email.
            """;

        return new OutboundMail(to, decision.Subject, text, BodyHtml: (template ?? MailTemplate.None).Render(subject, text, null));
    }

    internal static MailOptions ToMailOptions(EmailOptions settings)
    {
        var mail = new MailOptions { FromAddress = settings.FromAddress, FromName = settings.FromName };
        mail.Smtp.Host = settings.SmtpHost;
        mail.Smtp.Port = settings.SmtpPort;
        mail.Smtp.UseSsl = settings.UseSsl;
        mail.Smtp.UserName = settings.Username;
        mail.Smtp.Password = settings.Password;
        return mail;
    }
}
