using Microsoft.Extensions.Options;
using MimeKit;
using Tharga.Team.Service.Html;

namespace Tharga.Team.Email.Users;

/// <summary>Checks, sanitizes and stores mail to one user for the sender to deliver.</summary>
/// <remarks>
/// <b>Everything that can be refused is refused here</b>, while the caller is still there to hear it: an unknown
/// category, a mail with nothing to say, files over the limits. What can only be known later — whether the user
/// still receives the category, what their address is — is decided when the mail is sent.
/// </remarks>
internal sealed class UserMailQueue(
    UserMailCategoryRegistry categories,
    IUserMailOutbox outbox,
    IOptions<UserMailOptions> options,
    TimeProvider timeProvider) : IUserMailQueue
{
    private const string OctetStream = "application/octet-stream";
    private const int MaxDeduplicationKeyLength = 200;

    public async Task<UserMailQueued> QueueAsync(UserMail mail, CancellationToken cancellationToken = default)
    {
        var accepted = Accept(mail);

        return await outbox.AddAsync(accepted, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }

    public async Task<UserMailDelivery> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        var item = await outbox.GetAsync(id, cancellationToken);

        return item == null ? null : new UserMailDelivery
        {
            Id = item.Id,
            RecipientUserKey = item.Mail.RecipientUserKey,
            Category = item.Mail.Category,
            Subject = item.Mail.Subject,
            DeduplicationKey = item.Mail.DeduplicationKey,
            Status = item.Status,
            Attempts = item.Attempts,
            QueuedAt = item.QueuedAt,
            NextAttemptAt = item.NextAttemptAt,
            CompletedAt = item.CompletedAt,
            Reason = item.Reason,
            MessageId = item.MessageId
        };
    }

    /// <summary>The mail as it is stored: trimmed, sanitized, its text derived when missing and its files described.</summary>
    internal UserMail Accept(UserMail mail)
    {
        ArgumentNullException.ThrowIfNull(mail);
        var o = options.Value;

        if (string.IsNullOrWhiteSpace(mail.RecipientUserKey)) throw new ArgumentException("A mail to a user names the user's key.", nameof(mail));

        var category = categories.Get(mail.Category, nameof(mail));

        var subject = OneLine(mail.Subject);
        if (subject.Length == 0) throw new ArgumentException("A mail to a user has a subject.", nameof(mail));

        var html = SafeHtml.Sanitize(mail.BodyHtml);
        var body = string.IsNullOrWhiteSpace(mail.Body) ? null : mail.Body.Replace("\r\n", "\n").Trim();
        body ??= html == null ? null : HtmlText.ToPlainText(html);

        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("A mail to a user has a body, as text or as HTML with something readable in it.", nameof(mail));
        if (body.Length + (html?.Length ?? 0) > o.MaxBodyLength) throw new ArgumentException($"The mail's body is longer than the {o.MaxBodyLength} characters allowed.", nameof(mail));

        var replyTo = string.IsNullOrWhiteSpace(mail.ReplyTo) ? null : mail.ReplyTo.Trim();
        if (replyTo != null && !MailboxAddress.TryParse(replyTo, out _)) throw new ArgumentException($"'{replyTo}' is not an address replies can be sent to.", nameof(mail));

        var deduplicationKey = string.IsNullOrWhiteSpace(mail.DeduplicationKey) ? null : mail.DeduplicationKey.Trim();
        if (deduplicationKey?.Length > MaxDeduplicationKeyLength) throw new ArgumentException($"A deduplication key is at most {MaxDeduplicationKeyLength} characters.", nameof(mail));

        return new UserMail
        {
            RecipientUserKey = mail.RecipientUserKey.Trim(),
            Category = category.Key,
            Subject = subject,
            BodyHtml = html,
            Body = body,
            Attachments = Attachments(mail.Attachments, o),
            ReplyTo = replyTo,
            DeduplicationKey = deduplicationKey
        };
    }

    private static UserMailAttachment[] Attachments(UserMailAttachment[] attachments, UserMailOptions o)
    {
        var files = (attachments ?? []).Where(x => x != null).ToArray();

        if (files.Length > o.MaxAttachmentCount) throw new ArgumentException($"A mail to a user carries at most {o.MaxAttachmentCount} files.", nameof(attachments));
        if (files.Any(x => x.Content == null || string.IsNullOrWhiteSpace(x.FileName))) throw new ArgumentException("Every file sent with a mail has a name and content.", nameof(attachments));
        if (files.Sum(x => (long)x.Content.Length) > o.MaxAttachmentBytes) throw new ArgumentException($"The files sent with one mail add up to at most {o.MaxAttachmentBytes} bytes.", nameof(attachments));

        return [.. files.Select(x => new UserMailAttachment
        {
            FileName = x.FileName.Trim(),
            ContentType = string.IsNullOrWhiteSpace(x.ContentType) ? OctetStream : x.ContentType.Trim(),
            Content = x.Content,
            Size = x.Content.Length,
            ContentId = string.IsNullOrWhiteSpace(x.ContentId) ? null : x.ContentId.Trim().Trim('<', '>')
        })];
    }

    private static string OneLine(string value)
        => string.Join(' ', (value ?? string.Empty).Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0));
}
