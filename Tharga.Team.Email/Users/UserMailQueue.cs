using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Tharga.Team.Email.Users;

/// <summary>Checks, sanitizes and stores mail to users for the sender to deliver.</summary>
/// <remarks>
/// <b>Everything that can be refused is refused here</b>, while the caller is still there to hear it: an unknown
/// category, a mail with nothing to say, files over the limits. What can only be known later — whether the user
/// still receives the category, what their address is — is decided when the mail is sent.
/// <para>
/// <b>A refusal is recorded as well as thrown</b>, so the mail log shows an attempt that would otherwise leave no trace.
/// The caller hears the same exception, whether or not the record could be kept.
/// </para>
/// </remarks>
internal sealed partial class UserMailQueue(
    UserMailCategoryRegistry categories,
    IUserMailOutbox outbox,
    IOptions<UserMailOptions> options,
    TimeProvider timeProvider,
    UserMailDispatcher dispatcher = null,
    ILogger<UserMailQueue> logger = null) : IUserMailQueue
{
    private const string OctetStream = "application/octet-stream";
    private const int MaxDeduplicationKeyLength = 200;
    private const int MaxPageSize = 100;
    private const int MaxRecordedTextLength = 200;
    private const int MaxRecordedReasonLength = 500;

    internal const string ContactRefusal = "Contact mail is queued by the contact message service, which checks the sender's scope and the addresses' opt-outs; this queue sends to users.";

    public async Task<UserMailQueued> QueueAsync(UserMail mail, CancellationToken cancellationToken = default)
    {
        var accepted = await AcceptRecordingAsync(mail, cancellationToken);

        return await outbox.AddAsync(accepted, Now(), cancellationToken);
    }

    public async Task<UserMailDelivery> SendNowAsync(UserMail mail, CancellationToken cancellationToken = default)
    {
        if (dispatcher == null) throw new InvalidOperationException("Sending at once needs the user mail dispatcher, which this queue was built without.");

        var accepted = await AcceptRecordingAsync(mail, cancellationToken);
        var claimant = $"{Environment.MachineName}:{Environment.ProcessId}:now:{Guid.NewGuid():N}";
        var item = await outbox.AddClaimedAsync(accepted, Now(), claimant, options.Value.Lease, cancellationToken);
        await dispatcher.SendNowAsync(item, claimant, cancellationToken);

        return ToDelivery(await outbox.GetAsync(item.Id, cancellationToken) ?? item);
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

    public async Task<UserMailDeliveryPage> ListAsync(UserMailFilter filter, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default)
        => ToPage(await outbox.ListAsync(Normalized(filter), Cursor(cursor), Math.Clamp(pageSize, 1, MaxPageSize), cancellationToken));

    /// <summary>The filter with its words trimmed and an address in the lower case the queue stores it in, so a filter typed by a person finds what was stored.</summary>
    internal static UserMailFilter Normalized(UserMailFilter filter)
    {
        filter ??= new UserMailFilter();

        return filter with
        {
            Category = string.IsNullOrWhiteSpace(filter.Category) ? null : filter.Category.Trim(),
            RecipientUserKey = string.IsNullOrWhiteSpace(filter.RecipientUserKey) ? null : filter.RecipientUserKey.Trim(),
            RecipientAddress = UserMailAddresses.Normalize(filter.RecipientAddress),
            TeamKey = Team(filter.TeamKey)
        };
    }

    internal static UserMailDelivery ToDelivery(UserMailItem item) => new()
    {
        Id = item.Id,
        RecipientUserKey = item.Mail.RecipientUserKey,
        RecipientAddress = item.RecipientAddress,
        RecipientName = item.Mail.RecipientName,
        Category = item.Mail.Category,
        Subject = item.Mail.Subject,
        Kind = item.Mail.Kind,
        DeduplicationKey = item.Mail.DeduplicationKey,
        TeamKey = item.Mail.TeamKey,
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
        UserMailBatch accepted;
        try
        {
            accepted = Accept(batch);
        }
        catch (ArgumentException e) when (batch != null && e is not ArgumentNullException)
        {
            await RecordRefusalAsync(new UserMail { Category = batch.Category, Subject = batch.Subject, Kind = batch.Kind, TeamKey = batch.TeamKey }, e, cancellationToken);
            throw;
        }

        var id = await outbox.AddBatchAsync(accepted, Now(), cancellationToken);

        return new UserMailBatchQueued(id, accepted.RecipientUserKeys.Length);
    }

    private async Task<UserMail> AcceptRecordingAsync(UserMail mail, CancellationToken cancellationToken)
    {
        try
        {
            return Accept(mail);
        }
        catch (ArgumentException e) when (mail != null && e is not ArgumentNullException)
        {
            await RecordRefusalAsync(mail, e, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Records the refusal as a mail given up, what the caller named of it and the reason, never its body. A cause that repeats
    /// is recorded once a day, so a host bug in a loop cannot fill the collection. Failing to record never changes what the
    /// caller hears.
    /// </summary>
    private async Task RecordRefusalAsync(UserMail mail, ArgumentException refusal, CancellationToken cancellationToken)
    {
        try
        {
            var refused = Refused(mail);
            var reason = Truncated(ParameterSuffix().Replace(refusal.Message, string.Empty).Trim(), MaxRecordedReasonLength);

            await outbox.AddRefusedAsync(refused, reason, RefusalKey(refused, reason), Now(), cancellationToken);
        }
        catch (NotSupportedException)
        {
            // An outbox that keeps no such record: the caller still hears the refusal.
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger?.LogWarning(e, "A refused mail could not be recorded in the mail log.");
        }
    }

    /// <summary>What the caller named of a refused mail, trimmed and cut short; the body and files are left out.</summary>
    private static UserMail Refused(UserMail mail)
    {
        var userKey = Truncated(mail.RecipientUserKey?.Trim(), MaxRecordedTextLength);

        return new UserMail
        {
            RecipientUserKey = userKey,
            RecipientAddress = userKey == null ? Truncated(UserMailAddresses.Normalize(mail.RecipientAddress), MaxRecordedTextLength) : null,
            RecipientName = userKey == null ? Truncated(OneLine(mail.RecipientName), MaxRecordedTextLength) : null,
            Category = Truncated(mail.Category?.Trim(), MaxRecordedTextLength) ?? string.Empty,
            Subject = Truncated(OneLine(mail.Subject), MaxRecordedTextLength) ?? string.Empty,
            Kind = mail.Kind,
            TeamKey = Truncated(Team(mail.TeamKey), MaxRecordedTextLength)
        };
    }

    /// <summary>The key that makes the same refusal of the same mail, on the same day, one record.</summary>
    private string RefusalKey(UserMail refused, string reason)
    {
        var cause = string.Join('\n', refused.Kind, refused.Category, refused.RecipientUserKey, refused.RecipientAddress, refused.Subject, refused.TeamKey, reason);

        return $"refused:{Now():yyyyMMdd}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cause)))[..32]}";
    }

    private static string Truncated(string value, int length)
        => string.IsNullOrEmpty(value) ? null : value.Length <= length ? value : value[..length];

    [GeneratedRegex(@"\s*\(Parameter '[^']*'\)\s*$")]
    private static partial Regex ParameterSuffix();

    public async Task<UserMailBatchDelivery> GetBatchAsync(string id, CancellationToken cancellationToken = default)
        => string.IsNullOrWhiteSpace(id) ? null : await outbox.GetBatchDeliveryAsync(id, cancellationToken);

    /// <summary>The mail as it is stored: trimmed, sanitized, its text derived when missing and its files described.</summary>
    /// <remarks>A mail naming no user goes to its address alone, which only a required category accepts: nobody could choose not to receive it, nor follow an unsubscribe link, so an optional one would reach a person with no way to stop it.</remarks>
    internal UserMail Accept(UserMail mail)
    {
        ArgumentNullException.ThrowIfNull(mail);
        if (mail.Kind == UserMailKind.Contact) throw new ArgumentException(ContactRefusal, nameof(mail));

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
            Kind = mail.Kind,
            TeamKey = Team(mail.TeamKey)
        };
    }

    private static string Team(string teamKey) => string.IsNullOrWhiteSpace(teamKey) ? null : teamKey.Trim();

    /// <summary>The batch as it is stored: each recipient once, and its content checked exactly as a single mail's is.</summary>
    internal UserMailBatch Accept(UserMailBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Kind == UserMailKind.Contact || (batch.RecipientAddresses ?? []).Length > 0) throw new ArgumentException(ContactRefusal, nameof(batch));

        string[] recipients = [.. (batch.RecipientUserKeys ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal)];
        if (recipients.Length == 0) throw new ArgumentException("A mail to many users names at least one user's key.", nameof(batch));

        var content = AcceptContent(batch.Category, batch.Subject, batch.BodyHtml, batch.Body, batch.Attachments, nameof(batch));

        return content with { RecipientUserKeys = recipients, Kind = batch.Kind, TeamKey = Team(batch.TeamKey) };
    }

    private UserMailBatch AcceptContent(string categoryKey, string subjectLine, string bodyHtml, string bodyText, UserMailAttachment[] attachments, string parameterName)
        => Content(options.Value, categories.Get(categoryKey, parameterName).Key, subjectLine, bodyHtml, bodyText, attachments, parameterName);

    /// <summary>The content as it is stored, in a category the caller already accepted: the subject on one line, the HTML sanitized, the text derived when missing, and the files within the limits.</summary>
    /// <exception cref="ArgumentException">There is no subject or body, or the body or files exceed the limits.</exception>
    internal static UserMailBatch Content(UserMailOptions o, string categoryKey, string subjectLine, string bodyHtml, string bodyText, UserMailAttachment[] attachments, string parameterName)
    {
        var (subject, body, html) = UserMailComposer.Draft(subjectLine, bodyText, bodyHtml);
        if (subject.Length == 0) throw new ArgumentException("A mail has a subject.", parameterName);

        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("A mail has a body, as text or as HTML with something readable in it.", parameterName);
        if (body.Length + (html?.Length ?? 0) > o.MaxBodyLength) throw new ArgumentException($"The mail's body is longer than the {o.MaxBodyLength} characters allowed.", parameterName);

        return new UserMailBatch
        {
            RecipientUserKeys = [],
            Category = categoryKey,
            Subject = subject,
            BodyHtml = html,
            Body = body,
            Attachments = Attachments(attachments, o)
        };
    }

    private static UserMailAttachment[] Attachments(UserMailAttachment[] attachments, UserMailOptions o)
    {
        var files = (attachments ?? []).Where(x => x != null).ToArray();

        if (files.Length > o.MaxAttachmentCount) throw new ArgumentException($"A mail carries at most {o.MaxAttachmentCount} files.", nameof(attachments));
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
