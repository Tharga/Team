using System.Text.RegularExpressions;

namespace Tharga.Team.Support.Email;

/// <summary>
/// Trims the quoted thread and signature off a reply, leaving what the person actually wrote.
/// </summary>
/// <remarks>
/// <b>Not cosmetic.</b> A reply carries the entire prior conversation, so appending one raw makes the
/// transcript unreadable after two exchanges and pushes it toward <c>SupportCaseLimits</c> — 10,000
/// characters per entry, which a few quoted round-trips reach on their own.
/// <para>
/// <b>Deliberately conservative.</b> There is no reliable marker for where a quote begins: every client
/// invents its own, and locales translate them. So this cuts only on patterns that are unambiguous, and when
/// it finds nothing it keeps the whole body. Keeping too much is a transcript that reads badly; cutting too
/// much silently loses what somebody said, and only one of those is recoverable.
/// </para>
/// </remarks>
internal static partial class QuotedText
{
    public static string Trim(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;

        var lines = body.Replace("\r\n", "\n").Split('\n');
        var cut = lines.Length;

        for (var i = 0; i < lines.Length; i++)
        {
            if (IsCutPoint(lines[i]))
            {
                cut = i;
                break;
            }
        }

        var kept = string.Join('\n', lines.Take(cut)).TrimEnd();

        // Everything looked like a quote. The body is then more useful whole than empty, and an empty entry
        // in a transcript reads as the person having sent nothing.
        return kept.Length == 0 ? body.Trim() : kept;
    }

    private static bool IsCutPoint(string line)
    {
        var text = line.Trim();

        if (text.Length == 0) return false;

        return SignatureDelimiter().IsMatch(text)
               || AttributionLine().IsMatch(text)
               || ClientSeparator().IsMatch(text)
               || text.StartsWith('>');
    }

    /// <summary>
    /// The one standardised marker: a line of exactly <c>-- </c> begins a signature (RFC 3676).
    /// </summary>
    [GeneratedRegex(@"^--\s?$")]
    private static partial Regex SignatureDelimiter();

    /// <summary>
    /// <c>On &lt;date&gt;, &lt;someone&gt; wrote:</c> and its close relatives, which is what most clients
    /// put above a quote.
    /// </summary>
    /// <remarks>
    /// Matched on the bracketing words rather than on the date, which varies by locale and by client. The
    /// verb is not required to sit against the colon: English puts the name before it (<c>… Support
    /// wrote:</c>) and Swedish after (<c>… skrev Support:</c>), and a pattern fitted to one silently fails
    /// on the other.
    /// </remarks>
    [GeneratedRegex(@"^(on|den|el|le)\b.{0,120}\b(wrote|skrev|schrieb|écrit|escribió)\b.{0,80}:\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AttributionLine();

    /// <summary>
    /// The drawn separators Outlook and others insert — a rule, or a header block introduced by
    /// <c>From:</c> after one.
    /// </summary>
    [GeneratedRegex(@"^(_{5,}|-{5,}\s*(original message|forwarded message|ursprungligt meddelande)?\s*-{0,}|-{2,}\s*(original message|forwarded message)\s*-{2,})$", RegexOptions.IgnoreCase)]
    private static partial Regex ClientSeparator();
}
