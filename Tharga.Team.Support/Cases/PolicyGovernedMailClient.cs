using Microsoft.Extensions.Logging;
using Tharga.Team.Service.Email;
using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Puts every support mail through <see cref="IOutboundMailPolicy"/> before it reaches the transport
/// (Tharga/Team#290): outside production it goes only to an allowed domain or the override address, or is withheld.
/// </summary>
/// <remarks>
/// <b>A decorator rather than a change to <see cref="SupportMailClient"/></b>, because the transport namespace is
/// kept ignorant of Team types so it can be lifted into its own package unchanged — which
/// <c>TransportNamespaceIsolationTests</c> guards. The policy is a Team rule, so it is applied here, around the
/// transport. It is the registered <see cref="ISupportMailClient"/>, so no caller reaches the bare transport.
/// <para>
/// A withheld mail reports a failed send, so a case records the reply as not delivered rather than as sent.
/// </para>
/// </remarks>
internal sealed class PolicyGovernedMailClient(
    ISupportMailClient inner,
    IOutboundMailPolicy policy,
    ILogger<PolicyGovernedMailClient> logger = null) : ISupportMailClient
{
    private const string WithheldMessage = "Withheld by the non-production mail policy: set Email:Override to receive it.";

    public bool CanSend => inner.CanSend;

    public bool CanRead => inner.CanRead;

    public Task<MailSendResult> SendAsync(OutboundMail mail, CancellationToken cancellationToken = default)
    {
        var decision = policy.Decide(mail.To, mail.Subject);
        if (!decision.ShouldSend)
        {
            logger?.LogWarning("Support mail to {Recipient} was not sent. {Reason}", mail.To, WithheldMessage);
            return Task.FromResult(MailSendResult.Failed(WithheldMessage));
        }

        return inner.SendAsync(mail with { To = decision.Recipient, Subject = decision.Subject }, cancellationToken);
    }

    public Task<MailFetchResult> FetchAsync(MailFetchPosition position, CancellationToken cancellationToken = default)
        => inner.FetchAsync(position, cancellationToken);
}
