using MimeKit;
using Tharga.Team.Service.Html;

namespace Tharga.Team.Email.Users;

/// <summary>The bodies a recipient of mail to users receives, with the footer and in the host's template; one code path for sending and for previewing.</summary>
internal sealed class UserMailComposer(UserMailFooter footer, MailTemplate template)
{
    private readonly MailTemplate _template = template ?? MailTemplate.None;

    /// <summary>The text with the footer under a signature line, the HTML in the template with the footer, and the unsubscribe address; with no user key, as a preview shows it.</summary>
    public async Task<UserMailComposed> ComposeAsync(string subject, string body, string bodyHtml, string userKey, UserMailCategory category)
        => Compose(subject, body, bodyHtml, await footer.ForAsync(userKey, category));

    /// <summary>The text with <paramref name="parts"/> under a signature line, the HTML in the template with them, and their unsubscribe address; with no footer, the bodies alone.</summary>
    public UserMailComposed Compose(string subject, string body, string bodyHtml, UserMailFooterContent parts)
    {
        var text = parts == null ? body : $"{body?.TrimEnd()}\n\n-- \n{parts.Text}";
        var html = _template.Render(subject, body, bodyHtml, parts?.Html);

        return new UserMailComposed(text, html, parts?.UnsubscribeUrl);
    }

    /// <summary>A draft as the queue would store it, without refusing anything: the subject on one line, the HTML sanitized, the text derived from it when none is given.</summary>
    public static (string Subject, string Body, string BodyHtml) Draft(string subject, string body, string bodyHtml)
    {
        var html = SafeHtml.Sanitize(bodyHtml);
        var text = string.IsNullOrWhiteSpace(body) ? null : body.Replace("\r\n", "\n").Trim();
        text ??= html == null ? string.Empty : HtmlText.ToPlainText(html);

        return (UserMailQueue.OneLine(subject), text, html);
    }

    /// <summary>The sender as a recipient sees it: the From name and address of mail to users.</summary>
    public static string Sender(UserMailOptions options)
    {
        var address = options?.Mail?.FromAddress?.Trim();
        if (string.IsNullOrEmpty(address)) return null;

        return string.IsNullOrWhiteSpace(options.Mail.FromName) ? address : new MailboxAddress(options.Mail.FromName, address).ToString();
    }
}

/// <summary>The text, the HTML (null when the mail has none) and the unsubscribe address (null for a required category) of one mail to a user.</summary>
internal sealed record UserMailComposed(string Body, string BodyHtml, string UnsubscribeUrl);
