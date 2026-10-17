using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Mailbox;

/// <summary>Reads the catch-all mailbox over IMAP with the mailbox's own server settings, never support's.</summary>
internal sealed class MailClientMailboxSource : IMailboxSource
{
    private readonly MailClient _client;

    public MailClientMailboxSource(IOptions<TeamMailboxOptions> options, ILogger<MailClient> logger = null)
    {
        _client = new MailClient(Options.Create(options.Value.Mail ?? new MailOptions()), logger);
    }

    public bool CanRead => _client.CanRead;

    public Task<MailFetchResult> FetchAsync(MailFetchPosition position, CancellationToken cancellationToken) => _client.FetchAsync(position, true, cancellationToken);
}
