namespace Tharga.Team.Email;

/// <summary>
/// Whether a DKIM signing domain may vouch for a <c>From</c> address — relaxed alignment, as DMARC means it.
/// </summary>
/// <remarks>
/// <b>Equal, or the sender's domain lies under the signer's.</b> Whoever can publish a key for
/// <c>example.com</c> controls that zone and so its subdomains; a key published for <c>mail.example.com</c>
/// proves nothing about <c>example.com</c>, so that direction does not align.
/// <para>
/// <b>A signer with no dot never aligns.</b> Nobody but a registry can publish under a top-level domain, but
/// "the From domain ends with <c>.com</c>" is not a check worth being one typo away from.
/// </para>
/// <para>
/// <b>Not organisational-domain alignment.</b> DMARC proper compares registrable domains through the public
/// suffix list, which also lets a subdomain sign for its parent. Without the list that direction cannot be told
/// apart from a tenant of a shared suffix signing for its neighbour, so it is not offered.
/// </para>
/// </remarks>
internal static class DkimAlignment
{
    public static bool IsAligned(string fromDomain, string signingDomain)
    {
        var sender = Normalize(fromDomain);
        var signer = Normalize(signingDomain);

        if (sender.Length == 0 || signer.Length == 0 || !signer.Contains('.')) return false;

        return sender == signer || sender.EndsWith("." + signer, StringComparison.Ordinal);
    }

    private static string Normalize(string domain) => domain?.Trim().TrimEnd('.').ToLowerInvariant() ?? string.Empty;
}
