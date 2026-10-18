namespace Tharga.Team.Email;

/// <summary>
/// The tags of a <c>DKIM-Signature</c> header this transport decides on before asking DNS for a key.
/// </summary>
/// <param name="Domain">The signing domain, <c>d=</c>, lower-cased.</param>
/// <param name="SignedHeaders">The header names the signature covers, <c>h=</c>.</param>
/// <param name="LimitsBodyLength">Whether it carries <c>l=</c>, covering only the start of the body.</param>
/// <remarks>
/// <b>A tag named twice makes the whole header unreadable</b>, as RFC 6376 requires. Picking one of two
/// <c>d=</c> values here while the verifier picks the other would align one domain and verify another.
/// </remarks>
internal sealed record DkimSignature(string Domain, string[] SignedHeaders, bool LimitsBodyLength)
{
    /// <summary>
    /// Whether a verified signature of this shape may count for the sender.
    /// </summary>
    /// <remarks>
    /// <b>It must cover <c>From</c></b>, or it vouches for a message whose sender was added afterwards. <b>It
    /// must cover the whole body</b>: with <c>l=</c> anybody holding one signed mail can append their own text
    /// below the signed part and it still verifies.
    /// </remarks>
    public bool CanVouchForSender => !LimitsBodyLength && SignedHeaders.Contains("from", StringComparer.OrdinalIgnoreCase);

    /// <summary>The tags of <paramref name="value"/>, or null when it is malformed or lacks <c>d=</c> or <c>h=</c>.</summary>
    public static DkimSignature Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var tags = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var spec in value.Split(';'))
        {
            if (string.IsNullOrWhiteSpace(spec)) continue;

            var separator = spec.IndexOf('=');
            if (separator <= 0) return null;

            if (!tags.TryAdd(spec[..separator].Trim(), WithoutWhitespace(spec[(separator + 1)..]))) return null;
        }

        if (!tags.TryGetValue("d", out var domain) || domain.Length == 0) return null;
        if (!tags.TryGetValue("h", out var headers) || headers.Length == 0) return null;

        return new DkimSignature(
            domain.ToLowerInvariant(),
            headers.Split(':', StringSplitOptions.RemoveEmptyEntries),
            tags.ContainsKey("l"));
    }

    private static string WithoutWhitespace(string text) => string.Concat(text.Where(x => !char.IsWhiteSpace(x)));
}
