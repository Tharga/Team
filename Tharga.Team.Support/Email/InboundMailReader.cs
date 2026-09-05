using MimeKit;

namespace Tharga.Team.Support.Email;

/// <summary>
/// Turns a received MIME message into an <see cref="InboundMail"/>.
/// </summary>
/// <remarks>
/// Separated from the IMAP client so every one of these decisions can be asserted against a constructed
/// message rather than a live mailbox.
/// </remarks>
internal static class InboundMailReader
{
    /// <summary>
    /// Headers naming the address a mail was actually delivered to, most trustworthy first.
    /// </summary>
    /// <remarks>
    /// <b>IMAP exposes no envelope, so this chain is the whole mechanism</b> — and which of these a mailbox
    /// carries is decided by the receiving mail server, not by anything here. <c>To</c> and <c>Cc</c> come
    /// last and are a guess: they can name a list, hold several addresses, or say nothing at all about a mail
    /// that was bcc'd or forwarded.
    /// </remarks>
    private static readonly string[] DeliveryHeaders = ["Delivered-To", "X-Original-To", "Envelope-To"];

    /// <summary>
    /// Headers by which a mail declares itself machine-generated. Any of them is enough.
    /// </summary>
    /// <remarks>
    /// Answering one of these is how a support case and a vacation responder fill each other's mailboxes
    /// overnight. <c>Precedence</c> is checked by value because <c>Precedence: bulk</c> marks automation
    /// while other values do not.
    /// </remarks>
    private static readonly string[] AutomationHeaders = ["Auto-Submitted", "X-Auto-Response-Suppress", "List-Id", "List-Unsubscribe"];

    private static readonly string[] AutomatedPrecedence = ["bulk", "junk", "list", "auto_reply"];

    public static InboundMail Read(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new InboundMail(
            MessageId: message.MessageId,
            From: Sender(message),
            DeliveredTo: DeliveredTo(message),
            Subject: message.Subject ?? string.Empty,
            Body: Body(message),
            SentAt: message.Date,
            InReplyTo: message.InReplyTo,
            References: [.. message.References],
            IsAutomated: IsAutomated(message),
            HadAttachments: message.Attachments.Any());
    }

    private static string Sender(MimeMessage message)
        => Normalize(message.From.Mailboxes.FirstOrDefault()?.Address);

    private static string[] DeliveredTo(MimeMessage message)
    {
        var addresses = DeliveryHeaders
            .SelectMany(header => message.Headers.Where(x => string.Equals(x.Field, header, StringComparison.OrdinalIgnoreCase)))
            .Select(x => Normalize(AddressOf(x.Value)))
            .ToList();

        addresses.AddRange(message.To.Mailboxes.Concat(message.Cc.Mailboxes).Select(x => Normalize(x.Address)));

        return [.. addresses.Where(x => x.Length > 0).Distinct()];
    }

    /// <summary>
    /// A delivery header may be a bare address or a full mailbox with a display name.
    /// </summary>
    private static string AddressOf(string headerValue)
        => MailboxAddress.TryParse(headerValue, out var mailbox) ? mailbox.Address : headerValue;

    private static string Normalize(string address)
        => address?.Trim().ToLowerInvariant() ?? string.Empty;

    private static bool IsEmptyReturnPath(string value)
    {
        var text = value?.Trim();

        return string.IsNullOrEmpty(text) || text == "<>";
    }

    /// <summary>
    /// The plain-text body, flattening HTML when that is all the sender provided.
    /// </summary>
    /// <remarks>
    /// <b>Quoted history and signatures are not removed here.</b> That is a decision about what belongs in a
    /// transcript, not about what the mail contained, and it lives with the case rather than the transport.
    /// </remarks>
    private static string Body(MimeMessage message)
    {
        var text = message.TextBody;

        return !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : HtmlText.ToPlainText(message.HtmlBody);
    }

    private static bool IsAutomated(MimeMessage message)
    {
        // An empty return path is how a bounce identifies itself, and answering one loops with the far mail
        // server rather than with a person. It is written literally as <>, which parses as no address rather
        // than as an empty string.
        if (message.Headers.Contains(HeaderId.ReturnPath) && IsEmptyReturnPath(message.Headers[HeaderId.ReturnPath]))
            return true;

        foreach (var header in message.Headers)
        {
            if (AutomationHeaders.Contains(header.Field, StringComparer.OrdinalIgnoreCase))
            {
                // Auto-Submitted: no is the explicit way of saying a human sent it.
                if (string.Equals(header.Field, "Auto-Submitted", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(header.Value?.Trim(), "no", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return true;
            }

            if (string.Equals(header.Field, "Precedence", StringComparison.OrdinalIgnoreCase) &&
                AutomatedPrecedence.Contains(header.Value?.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
