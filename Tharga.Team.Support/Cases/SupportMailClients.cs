using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Support.Cases;

/// <summary>Hands out the mail client that reads and sends for one support mailbox.</summary>
internal interface ISupportMailClients
{
    IMailClient For(SupportMailbox mailbox);
}

/// <summary>The primary's registered client, and for every other mailbox its own transport behind the same outbound policy.</summary>
internal sealed class SupportMailClients(
    IMailClient primary,
    IOutboundMailPolicy policy,
    ILoggerFactory loggerFactory = null) : ISupportMailClients
{
    public IMailClient For(SupportMailbox mailbox)
    {
        if (mailbox == null || mailbox.IsPrimary) return primary;

        var transport = new MailClient(Options.Create(mailbox.Options), loggerFactory?.CreateLogger<MailClient>());

        return new PolicyGovernedMailClient(transport, policy, loggerFactory?.CreateLogger<PolicyGovernedMailClient>());
    }
}
