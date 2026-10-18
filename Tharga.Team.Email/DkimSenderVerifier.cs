using MimeKit;
using MimeKit.Cryptography;

namespace Tharga.Team.Email;

/// <summary>
/// Decides whether a received mail demonstrably comes from its <c>From</c> address's domain, by DKIM.
/// </summary>
/// <remarks>
/// <b>DKIM because nothing else reaches this far.</b> The receiving server adds no <c>Authentication-Results</c>
/// to trust, and SPF judges the envelope sender of the last hop, which IMAP never shows. A DKIM signature
/// travels with the message and can be checked by anyone holding it.
/// <para>
/// <b>A signature counts only when it verifies, aligns and covers what matters</b>: its domain is the sender's
/// or a parent of it (<see cref="DkimAlignment"/>), it signs <c>From</c> and the whole body
/// (<see cref="DkimSignature.CanVouchForSender"/>), and the key came from that same domain
/// (<see cref="SingleDomainKeyLocator"/>). A valid signature from some other domain says only that the other
/// domain relayed it.
/// </para>
/// <para>
/// <b>Exactly one <c>From</c>.</b> A signature covers the last instance of a header it names, while the
/// sender read from the mail is the first; a second <c>From</c> added above a genuinely signed one is how a
/// replayed mail would be passed off as somebody else's.
/// </para>
/// <para>
/// <b>Never throws.</b> Anything that goes wrong — a malformed header, a missing key, a resolver timing out —
/// means not verified. Only the caller cancelling is passed on. At most <see cref="MaxVerifications"/>
/// signatures are checked, because each can cost a DNS lookup and the mail decides how many it carries.
/// </para>
/// </remarks>
internal sealed class DkimSenderVerifier(IDkimPublicKeyLocator keys)
{
    internal const int MaxVerifications = 3;

    private static readonly Lazy<DkimSenderVerifier> SharedVerifier = new(() => new DkimSenderVerifier(DnsDkimPublicKeyLocator.Create()));

    /// <summary>The verifier over the system's DNS, shared so its key cache serves every read in the process.</summary>
    public static DkimSenderVerifier Shared => SharedVerifier.Value;

    /// <summary>The signing domain that vouches for the sender, or null when none does.</summary>
    public async Task<string> VerifiedDomainAsync(MimeMessage message, CancellationToken cancellationToken = default)
    {
        var fromDomain = SoleSenderDomain(message);
        if (fromDomain == null) return null;

        var attempts = 0;

        foreach (var header in message.Headers.Where(x => x.Id == HeaderId.DkimSignature))
        {
            var signature = DkimSignature.Parse(header.Value);
            if (signature is not { CanVouchForSender: true } || !DkimAlignment.IsAligned(fromDomain, signature.Domain)) continue;

            if (++attempts > MaxVerifications) break;

            if (await VerifiesAsync(message, header, signature.Domain, cancellationToken)) return signature.Domain;
        }

        return null;
    }

    private async Task<bool> VerifiesAsync(MimeMessage message, Header header, string domain, CancellationToken cancellationToken)
    {
        try
        {
            return await new DkimVerifier(new SingleDomainKeyLocator(keys, domain)).VerifyAsync(message, header, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static string SoleSenderDomain(MimeMessage message)
    {
        if (message?.Headers.Count(x => x.Id == HeaderId.From) != 1 || message.From.Count != 1) return null;

        var domain = message.From.Mailboxes.SingleOrDefault()?.Domain;

        return string.IsNullOrWhiteSpace(domain) ? null : domain;
    }
}
