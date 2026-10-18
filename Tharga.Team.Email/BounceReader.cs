using System.Text.RegularExpressions;
using MimeKit;

namespace Tharga.Team.Email;

/// <summary>
/// Recognises a delivery status notification — a bounce — and reads which mail it reports and why.
/// </summary>
/// <remarks>
/// <b>The standard form first, the mail-server folklore second.</b> RFC 3464 says a bounce is a
/// <c>multipart/report</c> with a <c>message/delivery-status</c> part, and most servers send exactly that. Some
/// still send a plain mail from <c>MAILER-DAEMON</c> or <c>postmaster</c> with the original attached, or with
/// its headers pasted into the text, so those are recognised too — but only from those senders, because a
/// person forwarding a mail as an attachment is not reporting a failed delivery.
/// </remarks>
internal static class BounceReader
{
    internal const int MaxReasonLength = 500;

    private static readonly string[] DaemonSenders = ["mailer-daemon", "postmaster"];

    private static readonly Regex MessageIdLine = new(@"^\s*Message-ID:\s*<?(?<id>[^>\s]+)>?\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>What a bounce reports, or <c>null</c> when the mail is not one.</summary>
    public static (string MessageId, string Reason)? Read(MimeMessage message)
    {
        var parts = Parts(message.Body).ToList();
        var status = parts.OfType<MessageDeliveryStatus>().FirstOrDefault();
        var original = parts.OfType<MessagePart>().FirstOrDefault();
        var isReport = message.Body is MultipartReport report && string.Equals(report.ReportType, "delivery-status", StringComparison.OrdinalIgnoreCase);
        var fromDaemon = IsFromDaemon(message);
        var text = HumanText(message, parts);

        if (!isReport && status == null && !(fromDaemon && (original != null || MessageIdLine.IsMatch(text ?? string.Empty)))) return null;

        var messageId = original?.Message?.MessageId
                        ?? InlineMessageId(text)
                        ?? message.InReplyTo
                        ?? message.References.LastOrDefault();

        return (messageId, Trim(Diagnostic(status) ?? Explanation(text)));
    }

    private static bool IsFromDaemon(MimeMessage message)
    {
        var address = message.From.Mailboxes.FirstOrDefault()?.Address;
        var localPart = address?.Split('@')[0];

        return DaemonSenders.Contains(localPart, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every part, without descending into an attached message — the original's own parts are not the report's.</summary>
    private static IEnumerable<MimeEntity> Parts(MimeEntity entity)
    {
        if (entity == null) yield break;

        yield return entity;

        if (entity is not Multipart multipart) yield break;

        foreach (var child in multipart)
        {
            foreach (var part in Parts(child)) yield return part;
        }
    }

    private static string HumanText(MimeMessage message, IEnumerable<MimeEntity> parts)
    {
        var plain = parts.OfType<TextPart>().FirstOrDefault(x => x.IsPlain);

        return plain?.Text ?? message.TextBody ?? HtmlText.ToPlainText(message.HtmlBody);
    }

    /// <summary>The original's id from headers pasted into the text, which is where a plain bounce keeps them.</summary>
    private static string InlineMessageId(string text)
    {
        var match = MessageIdLine.Match(text ?? string.Empty);

        return match.Success ? match.Groups["id"].Value : null;
    }

    /// <summary>
    /// The receiving server's own words for the first recipient that failed, else the status code.
    /// </summary>
    private static string Diagnostic(MessageDeliveryStatus status)
    {
        if (status == null) return null;

        foreach (var group in status.StatusGroups.Skip(1))
        {
            var diagnostic = group["Diagnostic-Code"];
            if (!string.IsNullOrWhiteSpace(diagnostic)) return StripType(diagnostic);

            var code = group["Status"];
            if (!string.IsNullOrWhiteSpace(code)) return $"Status {code.Trim()}";
        }

        return null;
    }

    // "smtp; 554 5.7.23 ..." names the kind of code before the code; the reader wants the code.
    private static string StripType(string diagnostic)
    {
        var separator = diagnostic.IndexOf(';');

        return separator >= 0 ? diagnostic[(separator + 1)..] : diagnostic;
    }

    /// <summary>
    /// The human-readable explanation, without the copy of the original that follows it.
    /// </summary>
    /// <remarks>
    /// Starts at the line naming the SMTP error when there is one, because the lines before it are the same
    /// apology in every bounce and the reason would not survive the trim.
    /// </remarks>
    private static string Explanation(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var lines = text.Replace("\r", string.Empty).Split('\n')
            .TakeWhile(x => !x.TrimStart().StartsWith("------", StringComparison.Ordinal))
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();

        var error = lines.FindIndex(x => x.Contains("SMTP error", StringComparison.OrdinalIgnoreCase));

        return string.Join(" ", error >= 0 ? lines.Skip(error) : lines);
    }

    private static string Trim(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;

        var text = Regex.Replace(reason, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();

        return text.Length <= MaxReasonLength ? text : text[..(MaxReasonLength - 1)].TrimEnd() + "…";
    }
}
