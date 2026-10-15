using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Mailbox;

internal sealed class TeamMailboxHealth(IOptions<TeamMailboxOptions> options, ILogger<MailClient> logger = null) : ITeamMailboxHealth
{
    public async Task<MailboxHealth> CheckAsync(CancellationToken cancellationToken = default)
    {
        var mail = options.Value.Mail;
        if (mail?.Imap?.IsConfigured != true) return MailboxHealth.NotConfigured;

        var error = await new MailClient(Options.Create(mail), logger).CheckReadAsync(cancellationToken);
        return new MailboxHealth(true, error == null, error);
    }
}
