namespace Tharga.Team.Support.Email;

/// <summary>
/// Decides whether a mail was addressed to this instance, for a mailbox shared by more than one site.
/// </summary>
/// <remarks>
/// <b>An empty filter accepts everything.</b> That is the single-site configuration and the default, so a
/// host that never shares a mailbox configures nothing.
/// <para>
/// <b>Plus-addressing is stripped before a local part is compared.</b> A case is corresponded with at
/// <c>support+{caseId}@…</c>, so an address filter matching local parts literally would reject every reply to
/// the toolkit's own mail — the failure would look like inbound being broken rather than like a filter doing
/// its job. Domain entries avoid the question entirely, which is a reason to prefer them.
/// </para>
/// </remarks>
public sealed class RecipientFilter
{
    private readonly string[] _domains;
    private readonly string[] _addresses;

    /// <summary>A filter that accepts every recipient.</summary>
    public static RecipientFilter AcceptAll { get; } = new(null);

    public RecipientFilter(IEnumerable<string> allowed)
    {
        var entries = (allowed ?? [])
            .Select(Normalize)
            .Where(x => x.Length > 0)
            .ToArray();

        _domains = entries.Where(x => !x.Contains('@')).Distinct().ToArray();
        _addresses = entries.Where(x => x.Contains('@')).Select(StripPlusTag).Distinct().ToArray();
    }

    /// <summary>Whether this filter accepts everything, because nothing was configured.</summary>
    public bool AcceptsEverything => _domains.Length == 0 && _addresses.Length == 0;

    /// <summary>Whether a single recipient address is one this instance handles.</summary>
    public bool Accepts(string recipient)
    {
        if (AcceptsEverything) return true;

        var address = StripPlusTag(Normalize(recipient));
        if (address.Length == 0) return false;

        var at = address.LastIndexOf('@');
        if (at <= 0 || at == address.Length - 1) return false;

        return _addresses.Contains(address) || _domains.Contains(address[(at + 1)..]);
    }

    /// <summary>
    /// Whether any of the addresses a mail was delivered to is one this instance handles.
    /// </summary>
    /// <remarks>
    /// Any rather than all: a mail addressed to both sites is legitimately both sites' business, and each
    /// instance stores it against its own case.
    /// </remarks>
    public bool AcceptsAny(IEnumerable<string> recipients)
    {
        if (AcceptsEverything) return true;

        return recipients != null && recipients.Any(Accepts);
    }

    /// <summary>
    /// Lower-cases, trims, and takes the address out of a <c>"Name" &lt;addr&gt;</c> header value.
    /// </summary>
    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var text = value.Trim();

        var open = text.LastIndexOf('<');
        var close = text.LastIndexOf('>');
        if (open >= 0 && close > open) text = text[(open + 1)..close];

        return text.Trim().TrimStart('@').ToLowerInvariant();
    }

    private static string StripPlusTag(string address)
    {
        var at = address.LastIndexOf('@');
        if (at <= 0) return address;

        var plus = address.IndexOf('+', StringComparison.Ordinal);

        return plus >= 0 && plus < at ? address[..plus] + address[at..] : address;
    }
}
