using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Email.Mailbox;

namespace Tharga.Team.Support.Cases;

/// <summary>Checks every configured support mailbox at once, each over its own connections, so a slow server delays the answer by its own timeout and no more.</summary>
internal sealed class SupportMailboxHealthCheck(SupportMailboxes mailboxes, ILogger<MailClient> logger = null) : ISupportMailboxHealth
{
    public async Task<IReadOnlyList<SupportMailboxHealth>> CheckAsync(CancellationToken cancellationToken = default)
        => await Task.WhenAll(mailboxes.All.Where(Configured).Select(x => CheckAsync(x, cancellationToken)));

    private static bool Configured(SupportMailbox mailbox) => mailbox.Options.Imap.IsConfigured || mailbox.Options.Smtp.IsConfigured;

    private async Task<SupportMailboxHealth> CheckAsync(SupportMailbox mailbox, CancellationToken cancellationToken)
    {
        var client = new MailClient(Options.Create(mailbox.Options), logger);

        var reading = HealthAsync(mailbox.Options.Imap.IsConfigured, () => client.CheckReadAsync(cancellationToken));
        var sending = HealthAsync(mailbox.Options.Smtp.IsConfigured, () => client.CheckSendAsync(cancellationToken));

        return new SupportMailboxHealth(mailbox.Name, await reading, await sending);
    }

    private static async Task<MailboxHealth> HealthAsync(bool configured, Func<Task<string>> check)
    {
        if (!configured) return MailboxHealth.NotConfigured;

        var error = await check();

        return new MailboxHealth(true, error == null, error);
    }
}
