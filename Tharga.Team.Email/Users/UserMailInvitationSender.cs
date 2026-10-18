using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Users;

/// <summary>Sends an invitation as mail to users: recorded in the outbox in the required account category, addressed to the invited person's address alone, and attempted once, at once, so the inviter learns whether it went.</summary>
/// <remarks>
/// <b>One attempt, reported, never retried.</b> The inviter is waiting and is told whether the mail went, with the
/// link to pass on by hand when it did not, exactly as before; a mail they were told did not go must not arrive
/// later. A bounce reported afterwards marks it bounced like any other user mail, and it shows in the history of
/// whoever holds the address. It goes out from the address mail to users is sent from.
/// </remarks>
internal sealed class UserMailInvitationSender(
    UserMailQueue queue,
    UserMailText text,
    IOptions<UserMailOptions> options) : ITeamEmailSender
{
    /// <exception cref="InvalidOperationException">Mail to users cannot be sent, or this invitation was not; the message says why.</exception>
    /// <exception cref="ArgumentException">The address is not one a mail can be sent to.</exception>
    public async Task SendInviteAsync(TeamInviteMail invite)
    {
        ArgumentNullException.ThrowIfNull(invite);

        var missing = UserMailSender.Missing(options.Value.Mail);
        if (missing != null) throw new InvalidOperationException($"The invitation could not be sent: {missing} is not configured for mail to users.");

        var name = string.IsNullOrWhiteSpace(invite.RecipientName) ? invite.RecipientEmail?.Trim() : invite.RecipientName.Trim();
        var sent = await queue.SendNowAsync(new UserMail
        {
            RecipientAddress = invite.RecipientEmail,
            RecipientName = invite.RecipientName,
            Category = UserMailCategories.AccountKey,
            Subject = await text.FormatAsync(UserMailTexts.InvitationSubjectKey, UserMailTexts.InvitationSubject, invite.TeamName),
            Body = await text.FormatAsync(UserMailTexts.InvitationBodyKey, UserMailTexts.InvitationBody, name, invite.TeamName, invite.InviteLink),
            Kind = UserMailKind.Invitation
        });

        if (sent.Status == UserMailStatus.Sent) return;

        throw new InvalidOperationException($"The invitation could not be sent: {sent.Reason}");
    }
}
