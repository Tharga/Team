using Microsoft.Extensions.Options;
using MimeKit;
using Tharga.Team.Service.Html;

namespace Tharga.Team.Email.Users;

/// <summary>Checks, sanitizes and stores mail to users for the sender to deliver.</summary>
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
    private const int MaxPageSize = 100;

    public async Task<UserMailQueued> QueueAsync(UserMail mail, CancellationToken cancellationToken = default)
    {
        var accepted = Accept(mail);

        return await outbox.AddAsync(accepted, Now(), cancellationToken);
    }

    public async Task<UserMailDelivery> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        var item = await outbox.GetAsync(id, cancellationToken);

        return item == null ? null : ToDelivery(item);
    }

    public async Task<UserMailDeliveryPage> ListBatchAsync(string batchId, UserMailStatus? status = null, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(batchId)) return new UserMailDeliveryPage();

        return ToPage(await outbox.ListBatchAsync(batchId.Trim(), status, Cursor(cursor), Math.Clamp(pageSize, 1, MaxPageSize), cancellationToken));
    }

    public async Task<UserMailDeliveryPage> ListForRecipientAsync(string userKey, string address, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var key = string.IsNullOrWhiteSpace(userKey) ? null : userKey.Trim();
        var normalized = UserMailAddresses.Normalize(address);
        if (key == null && normalized == null) return new UserMailDeliveryPage();

        return ToPage(await outbox.ListForRecipientAsync(key, normalized, Cursor(cursor), Math.Clamp(pageSize, 1, MaxPageSize), cancellationToken));
    }

    internal static UserMailDelivery ToDelivery(UserMailItem item) => new()
    {
        Id = item.Id,
        RecipientUserKey = item.Mail.RecipientUserKey,
        RecipientAddress = item.RecipientAddress,
        Category = item.Mail.Category,
        Subject = item.Mail.Subject,
        Kind = item.Mail.Kind,
        DeduplicationKey = item.Mail.DeduplicationKey,
        BatchId = item.BatchId,
        Status = item.Status,
        Attempts = item.Attempts,
        QueuedAt = item.QueuedAt,
        NextAttemptAt = item.NextAttemptAt,
        CompletedAt = item.CompletedAt,
        Reason = item.Reason,
        MessageId = item.MessageId,
        BouncedAt = item.BouncedAt
    };

    private static UserMailDeliveryPage ToPage(UserMailItemPage page) => new()
    {
        Items = [.. (page?.Items ?? []).Where(x => x != null).Select(ToDelivery)],
        NextCursor = page?.NextCursor
    };

    private static string Cursor(string cursor) => string.IsNullOrWhiteSpace(cursor) ? null : cursor.Trim();

    public async Task<UserMailBatchQueued> QueueBatchAsync(UserMailBatch batch, CancellationToken cancellationToken = default)
    {
        var accepted = Accept(batch);

        var id = await outbox.AddBatchAsync(accepted, Now(), cancellationToken);

        return new UserMailBatchQueued(id, accepted.RecipientUserKeys.Length);
    }

    public async Task<UserMailBatchDelivery> GetBatchAsync(string id, CancellationToken cancellationToken = default)
        => string.IsNullOrWhiteSpace(id) ? null : await outbox.GetBatchDeliveryAsync(id, cancellationToken);

    /// <summary>The mail as it is stored: trimmed, sanitized, its text derived when missing and its files described.</summary>
    /// <remarks>A mail naming no user goes to its address alone, which only a required category accepts: nobody could choose not to receive it, nor follow an unsubscribe link, so an optional one would reach a person with no way to stop it.</remarks>
    internal UserMail Accept(UserMail mail)
    {
        ArgumentNullException.ThrowIfNull(mail);

        var userKey = string.IsNullOrWhiteSpace(mail.RecipientUserKey) ? null : mail.RecipientUserKey.Trim();
        var address = userKey == null ? RecipientAddress(mail.RecipientAddress) : null;

        var content = AcceptContent(mail.Category, mail.Subject, mail.BodyHtml, mail.Body, mail.Attachments, nameof(mail));

        if (userKey == null && !categories.Get(content.Category, nameof(mail)).Required)
            throw new ArgumentException($"A mail to an address alone is accepted only in a required category, and '{content.Category}' can be turned off.", nameof(mail));

        var replyTo = string.IsNullOrWhiteSpace(mail.ReplyTo) ? null : mail.ReplyTo.Trim();
        if (replyTo != null && !MailboxAddress.TryParse(replyTo, out _)) throw new ArgumentException($"'{replyTo}' is not an address replies can be sent to.", nameof(mail));

        var deduplicationKey = string.IsNullOrWhiteSpace(mail.DeduplicationKey) ? null : mail.DeduplicationKey.Trim();
        if (deduplicationKey?.Length > MaxDeduplicationKeyLength) throw new ArgumentException($"A deduplication key is at most {MaxDeduplicationKeyLength} characters.", nameof(mail));

        return new UserMail
        {
            RecipientUserKey = userKey,
            RecipientAddress = address,
            RecipientName = userKey == null && !string.IsNullOrWhiteSpace(mail.RecipientName) ? OneLine(mail.RecipientName) : null,
            Category = content.Category,
            Subject = content.Subject,
            BodyHtml = content.BodyHtml,
            Body = content.Body,
            Attachments = content.Attachments,
            ReplyTo = replyTo,
            DeduplicationKey = deduplicationKey,
            Kind = mail.Kind
        };
    }

    /// <summary>The batch as it is stored: each recipient once, and its content checked exactly as a single mail's is.</summary>
    internal UserMailBatch Accept(UserMailBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        string[] recipients = [.. (batch.RecipientUserKeys ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal)];
        if (recipients.Length == 0) throw new ArgumentException("A mail to many users names at least one user's key.", nameof(batch));

        var content = AcceptContent(batch.Category, batch.Subject, batch.BodyHtml, batch.Body, batch.Attachments, nameof(batch));

        return content with { RecipientUserKeys = recipients, Kind = batch.Kind };
    }

    private UserMailBatch AcceptContent(string categoryKey, string subjectLine, string bodyHtml, string bodyText, UserMailAttachment[] attachments, string parameterName)
    {
        var o = options.Value;
        var category = categories.Get(categoryKey, parameterName);

        var subject = OneLine(subjectLine);
        if (subject.Length == 0) throw new ArgumentException("A mail to a user has a subject.", parameterName);

        var html = SafeHtml.Sanitize(bodyHtml);
        var body = string.IsNullOrWhiteSpace(bodyText) ? null : bodyText.Replace("\r\n", "\n").Trim();
        body ??= html == null ? null : HtmlText.ToPlainText(html);

        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("A mail to a user has a body, as text or as HTML with something readable in it.", parameterName);
        if (body.Length + (html?.Length ?? 0) > o.MaxBodyLength) throw new ArgumentException($"The mail's body is longer than the {o.MaxBodyLength} characters allowed.", parameterName);

        return new UserMailBatch
        {
            RecipientUserKeys = [],
            Category = category.Key,
            Subject = subject,
            BodyHtml = html,
            Body = body,
            Attachments = Attachments(attachments, o)
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

    /// <exception cref="ArgumentException">There is no address, or it is not one a mail can be sent to.</exception>
    private static string RecipientAddress(string value)
    {
        var address = UserMailAddresses.Normalize(value)
                      ?? throw new ArgumentException("A mail names the user's key, or for a required category the address it goes to.", nameof(value));

        if (!MailboxAddress.TryParse(address, out var mailbox) || mailbox.Address != address || !address.Contains('@'))
            throw new ArgumentException($"'{address}' is not an address a mail can be sent to.", nameof(value));

        return address;
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    internal static string OneLine(string value)
        => string.Join(' ', (value ?? string.Empty).Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0));
}
