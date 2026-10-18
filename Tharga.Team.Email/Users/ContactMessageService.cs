using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Users;

/// <summary>Writes staff messages to the contacts a host lists, through the user mail queue, and keeps the log of them.</summary>
/// <remarks>
/// Reached only through the scope proxy, which refuses a caller without <see cref="SystemContactScopes.Send"/>. The list
/// is checked by <see cref="ContactAudience"/> exactly as before sending; the content and files are stored once as a batch,
/// one mail is queued per address reached, and each is sent from the contact sender after its opt-outs are checked again.
/// </remarks>
internal sealed class ContactMessageService(
    ContactMailCategoryRegistry categories,
    IOptions<ContactMessageOptions> contactOptions,
    IOptions<UserMailOptions> mailOptions,
    IContactMailSuppressionStore suppressions,
    IUserMailOutbox outbox,
    IUserMailQueue queue,
    IContactMessageLog log,
    TeamAuthorizer authorizer,
    TimeProvider timeProvider,
    IUserService userService = null,
    CompositeAuditLogger auditLogger = null,
    IAuditEntryFactory auditEntryFactory = null,
    ILogger<ContactMessageService> logger = null,
    ContactMailFooter footer = null,
    MailTemplate template = null) : IContactMessageService
{
    private const int MaxPageSize = 100;
    private const string QueuedPhase = "q:";
    private const string LeftPhase = "l:";

    public Task<ContactMessageChoices> GetChoicesAsync(CancellationToken cancellationToken = default)
    {
        var o = mailOptions.Value;

        return Task.FromResult(new ContactMessageChoices
        {
            Categories = [.. categories.All],
            Limits = new UserMessageLimits { MaxAttachmentCount = o.MaxAttachmentCount, MaxAttachmentBytes = o.MaxAttachmentBytes, MaxBodyLength = o.MaxBodyLength },
            MaxAddressees = contactOptions.Value.MaxAddressees
        });
    }

    public async Task<ContactAddresseeList> GetAddresseesAsync(ContactAddressee[] addressees, string category, CancellationToken cancellationToken = default)
        => ContactAudience.List(await CheckAsync(addressees, categories.Get(category, nameof(category)), cancellationToken));

    /// <remarks>Composed by the code that composes each mail sent, for nobody in particular: the unsubscribe link it shows opts nobody out.</remarks>
    public async Task<MailPreview> PreviewMailAsync(ContactMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentException("There is no message to preview.", nameof(message));

        var category = categories.Get(message.Category, nameof(message));
        var (subject, body, html) = UserMailComposer.Draft(message.Subject, message.Body, message.BodyHtml);
        var parts = await (footer ?? throw new InvalidOperationException($"Previewing a message needs the contact mail footer, which {nameof(ContactMessageRegistration.AddThargaContactMessages)} registers.")).ForAsync(null, category);
        var composed = new UserMailComposer(null, template).Compose(subject, body, html, parts);

        return new MailPreview
        {
            From = ContactMailSender.Display(mailOptions.Value, contactOptions.Value),
            ReplyTo = ContactMailSender.ReplyTo(mailOptions.Value, contactOptions.Value),
            Subject = subject,
            Html = composed.BodyHtml,
            Text = composed.Body
        };
    }

    public async Task<SentContactMessage> SendAsync(ContactMessage message, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var sent = await QueueAndRecordAsync(message, cancellationToken);

            Audit(AuditEventType.DataChange, ContactMessageAudit.SendAction, nameof(SendAsync), sw.ElapsedMilliseconds, true, null, Meta(
                (ContactMessageAudit.MessageId, sent.Id),
                (ContactMessageAudit.Category, sent.Category),
                (ContactMessageAudit.Subject, sent.Subject),
                (ContactMessageAudit.Recipients, Number(sent.RecipientCount)),
                (ContactMessageAudit.OptedOut, Number(sent.OptedOutCount)),
                (ContactMessageAudit.LeftOut, Number(sent.LeftOutCount)),
                (ContactMessageAudit.Attachments, Number(sent.AttachmentCount))));

            return sent;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Audit(AuditEventType.DataChange, ContactMessageAudit.SendAction, nameof(SendAsync), sw.ElapsedMilliseconds, false, e.Message, Meta(
                (ContactMessageAudit.Category, message?.Category),
                (ContactMessageAudit.Attachments, Number(message?.Attachments?.Count(x => x != null) ?? 0))));
            throw;
        }
    }

    public async Task<SentContactMessagePage> GetSentAsync(string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var page = await log.ListAsync(cursor, Math.Clamp(pageSize, 1, MaxPageSize), cancellationToken) ?? new ContactMessageLogPage();

        var items = new List<SentContactMessage>();
        foreach (var entry in page.Items ?? [])
        {
            var delivery = await queue.GetBatchAsync(entry.Id, cancellationToken);
            items.Add(ToSent(entry, delivery?.Outcomes ?? new UserMailOutcomes()));
        }

        Audit(AuditEventType.ServiceCall, ContactMessageAudit.LogAction, nameof(GetSentAsync), sw.ElapsedMilliseconds, true, null, Meta(
            (ContactMessageAudit.Count, Number(items.Count))));

        return new SentContactMessagePage { Items = [.. items], NextCursor = page.NextCursor };
    }

    public async Task<SentContactMessage> GetSentMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId)) return null;

        var sw = Stopwatch.StartNew();
        var entry = await log.GetAsync(messageId.Trim(), cancellationToken);
        if (entry == null) return null;

        var delivery = await queue.GetBatchAsync(entry.Id, cancellationToken);
        var sent = ToSent(entry, delivery?.Outcomes ?? new UserMailOutcomes());

        Audit(AuditEventType.ServiceCall, ContactMessageAudit.OpenAction, nameof(GetSentMessageAsync), sw.ElapsedMilliseconds, true, null, Meta(
            (ContactMessageAudit.MessageId, sent.Id),
            (ContactMessageAudit.Recipients, Number(sent.RecipientCount)),
            (ContactMessageAudit.Queued, Number(sent.Outcomes.Queued)),
            (ContactMessageAudit.Sent, Number(sent.Outcomes.Sent)),
            (ContactMessageAudit.Skipped, Number(sent.Outcomes.Skipped)),
            (ContactMessageAudit.Failed, Number(sent.Outcomes.Failed)),
            (ContactMessageAudit.Bounced, Number(sent.Outcomes.Bounced))));

        return sent;
    }

    /// <remarks>
    /// Two lists one after the other, under one cursor: the mails queued, read from the queue in the order they were queued
    /// and filtered by their status there, then the addresses never queued, read from the log entry. A status only one of
    /// them can hold skips the other.
    /// </remarks>
    public async Task<ContactMessageRecipientPage> GetRecipientsAsync(string messageId, ContactRecipientStatus? status = null, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId)) return null;

        var sw = Stopwatch.StartNew();
        var entry = await log.GetAsync(messageId.Trim(), cancellationToken);
        if (entry == null) return null;

        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var (queuedPhase, position) = ReadCursor(cursor);
        var items = new List<ContactMessageRecipient>();
        string next = null;

        if (queuedPhase && QueuedStatusOf(status, out var mailStatus))
        {
            var byAddress = (entry.Addressees ?? []).Where(x => x.Status == ContactAddresseeStatus.Reached).GroupBy(x => x.Address, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            var page = await queue.ListBatchAsync(entry.Id, mailStatus, position, size, cancellationToken) ?? new UserMailDeliveryPage();

            items.AddRange(page.Items.Select(x => FromMail(x, x.RecipientAddress != null ? byAddress.GetValueOrDefault(x.RecipientAddress) : null)));
            if (page.NextCursor != null) next = QueuedPhase + page.NextCursor;
        }

        if (next == null)
        {
            var start = queuedPhase ? 0 : int.Parse(position, CultureInfo.InvariantCulture);
            ContactAddresseeCheck[] left = [.. (entry.Addressees ?? []).Where(x => x.Status != ContactAddresseeStatus.Reached && LeftOutMatches(x, status))];
            var take = size - items.Count;

            items.AddRange(left.Skip(start).Take(take).Select(x => FromCheck(x, entry.SentAt)));
            if (start + take < left.Length) next = LeftPhase + (start + take).ToString(CultureInfo.InvariantCulture);
        }

        Audit(AuditEventType.ServiceCall, ContactMessageAudit.RecipientsAction, nameof(GetRecipientsAsync), sw.ElapsedMilliseconds, true, null, Meta(
            (ContactMessageAudit.MessageId, entry.Id),
            (ContactMessageAudit.Status, status?.ToString()),
            (ContactMessageAudit.Count, Number(items.Count))));

        return new ContactMessageRecipientPage { Items = [.. items], NextCursor = next };
    }

    public async Task<bool> SuppressAsync(string address, string category = null, CancellationToken cancellationToken = default)
    {
        var normalized = UserMailAddresses.Normalize(address);
        if (!ContactAudience.IsAddress(normalized)) throw new ArgumentException($"'{address}' is not a mail address.", nameof(address));

        var categoryKey = string.IsNullOrWhiteSpace(category) ? null : categories.Get(category, nameof(category)).Key;
        var sw = Stopwatch.StartNew();

        var added = await suppressions.AddAsync(new ContactMailSuppression
        {
            Address = normalized,
            Category = categoryKey,
            SuppressedAt = Now(),
            Source = ContactMailSuppressionSource.Staff
        }, cancellationToken);

        if (added)
        {
            Audit(AuditEventType.DataChange, ContactMessageAudit.SuppressAction, nameof(SuppressAsync), sw.ElapsedMilliseconds, true, null, Meta(
                (ContactMessageAudit.Category, categoryKey ?? ContactMessageAudit.EveryCategory),
                (ContactMessageAudit.Source, nameof(ContactMailSuppressionSource.Staff))));
        }

        return added;
    }

    /// <remarks>Queued before it is recorded, so a message that cannot be queued leaves no record; one that cannot then be recorded is still sent, and that is logged with its id.</remarks>
    private async Task<SentContactMessage> QueueAndRecordAsync(ContactMessage message, CancellationToken cancellationToken)
    {
        if (message == null) throw new ArgumentException("There is no message to send.", nameof(message));

        var category = categories.Get(message.Category, nameof(message));
        var content = UserMailQueue.Content(mailOptions.Value, category.Key, message.Subject, message.BodyHtml, message.Body, message.Attachments, nameof(message));
        var checks = await CheckAsync(message.Addressees, category, cancellationToken);

        UserMailBatchAddress[] reached = [.. checks.Where(x => x.Status == ContactAddresseeStatus.Reached).Select(x => new UserMailBatchAddress(x.Address, x.Name))];
        if (reached.Length == 0) throw new ArgumentException("Nobody on the list can be mailed: every address is left out or has opted out.", nameof(message));

        var now = Now();
        var batchId = await outbox.AddBatchAsync(content with { RecipientUserKeys = [], RecipientAddresses = reached, Kind = UserMailKind.Contact }, now, cancellationToken);

        var sender = await SenderAsync();
        var entry = new ContactMessageLogEntry
        {
            Id = batchId,
            Category = category.Key,
            Subject = content.Subject,
            AttachmentCount = content.Attachments.Length,
            SentByUserKey = sender.UserKey,
            SentByName = sender.Name,
            SentAt = now,
            RecipientCount = reached.Length,
            OptedOutCount = checks.Count(x => x.Status == ContactAddresseeStatus.OptedOut),
            LeftOutCount = checks.Count(x => x.Status == ContactAddresseeStatus.LeftOut),
            Addressees = checks
        };

        try
        {
            await log.AddAsync(entry, cancellationToken);
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Contact message {MessageId} was queued to {Recipients} addresses but could not be recorded in the log.", batchId, reached.Length);
            throw;
        }

        return ToSent(entry, new UserMailOutcomes { Queued = reached.Length }) with { Recipients = [.. checks.Select(x => FromCheck(x, now))] };
    }

    /// <exception cref="ArgumentException">The list holds more rows than allowed.</exception>
    private async Task<ContactAddresseeCheck[]> CheckAsync(ContactAddressee[] addressees, ContactMailCategory category, CancellationToken cancellationToken)
    {
        var rows = addressees ?? [];
        var max = contactOptions.Value.MaxAddressees;
        if (rows.Length > max) throw new ArgumentException($"A message to contacts lists at most {max} addressees, and this one lists {rows.Length}.", nameof(addressees));

        var candidates = ContactAudience.Candidates(rows);
        var found = candidates.Length == 0 ? [] : await suppressions.GetAsync(candidates, cancellationToken) ?? [];

        return ContactAudience.Check(rows, category.Key, found);
    }

    private static bool QueuedStatusOf(ContactRecipientStatus? status, out UserMailStatus? mailStatus)
    {
        mailStatus = status switch
        {
            null => null,
            ContactRecipientStatus.Queued => UserMailStatus.Queued,
            ContactRecipientStatus.Sent => UserMailStatus.Sent,
            ContactRecipientStatus.OptedOut => UserMailStatus.Skipped,
            ContactRecipientStatus.Failed => UserMailStatus.Failed,
            ContactRecipientStatus.Bounced => UserMailStatus.Bounced,
            _ => null
        };

        return status != ContactRecipientStatus.LeftOut;
    }

    private static bool LeftOutMatches(ContactAddresseeCheck check, ContactRecipientStatus? status) => status switch
    {
        null => true,
        ContactRecipientStatus.OptedOut => check.Status == ContactAddresseeStatus.OptedOut,
        ContactRecipientStatus.LeftOut => check.Status == ContactAddresseeStatus.LeftOut,
        _ => false
    };

    /// <exception cref="ArgumentException">The cursor was not issued for this list.</exception>
    private static (bool QueuedPhase, string Position) ReadCursor(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (true, null);

        var value = cursor.Trim();
        if (value.StartsWith(QueuedPhase, StringComparison.Ordinal) && value.Length > QueuedPhase.Length) return (true, value[QueuedPhase.Length..]);
        if (value.StartsWith(LeftPhase, StringComparison.Ordinal) && int.TryParse(value[LeftPhase.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var index)) return (false, index.ToString(CultureInfo.InvariantCulture));

        throw new ArgumentException($"'{cursor}' is not a cursor this list issued.", nameof(cursor));
    }

    private static ContactMessageRecipient FromMail(UserMailDelivery mail, ContactAddresseeCheck check) => new()
    {
        Address = mail.RecipientAddress ?? check?.Address ?? string.Empty,
        Name = check?.Name,
        Detail = check?.Detail,
        References = check?.References ?? [],
        Status = mail.Status switch
        {
            UserMailStatus.Sent => ContactRecipientStatus.Sent,
            UserMailStatus.Skipped => ContactRecipientStatus.OptedOut,
            UserMailStatus.Failed => ContactRecipientStatus.Failed,
            UserMailStatus.Bounced => ContactRecipientStatus.Bounced,
            _ => ContactRecipientStatus.Queued
        },
        OptedOutOfAll = mail.Status == UserMailStatus.Skipped && mail.Reason == UserMailDispatcher.ContactOptedOutOfAllReason,
        Reason = mail.Status == UserMailStatus.Skipped ? null : mail.Reason,
        ChangedAt = mail.ChangedAt,
        MessageId = mail.MessageId
    };

    private static ContactMessageRecipient FromCheck(ContactAddresseeCheck check, DateTime at) => new()
    {
        Address = check.Address,
        Name = check.Name,
        Detail = check.Detail,
        References = check.References ?? [],
        Status = check.Status switch
        {
            ContactAddresseeStatus.OptedOut => ContactRecipientStatus.OptedOut,
            ContactAddresseeStatus.LeftOut => ContactRecipientStatus.LeftOut,
            _ => ContactRecipientStatus.Queued
        },
        OptedOutOfAll = check.OptedOutOfAll,
        InvalidAddress = check.InvalidAddress,
        Reason = check.ExcludedReason,
        ChangedAt = at
    };

    private static SentContactMessage ToSent(ContactMessageLogEntry entry, UserMailOutcomes outcomes) => new()
    {
        Id = entry.Id,
        Category = entry.Category,
        Subject = entry.Subject,
        AttachmentCount = entry.AttachmentCount,
        SentByUserKey = entry.SentByUserKey,
        SentByName = entry.SentByName,
        SentAt = entry.SentAt,
        RecipientCount = entry.RecipientCount,
        OptedOutCount = entry.OptedOutCount,
        LeftOutCount = entry.LeftOutCount,
        Outcomes = outcomes
    };

    private async Task<(string UserKey, string Name)> SenderAsync()
    {
        var user = userService == null ? null : await userService.GetCurrentUserAsync();
        var name = !string.IsNullOrWhiteSpace(user?.Name) ? user.Name : await authorizer.GetDisplayNameAsync();

        return (user?.Key, name);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private void Audit(AuditEventType eventType, string action, string methodName, long durationMs, bool success, string error, IReadOnlyDictionary<string, string> metadata)
    {
        if (auditLogger == null || auditEntryFactory == null) return;

        auditLogger.Log(auditEntryFactory.Create(eventType, ContactMessageAudit.Feature, action, methodName, durationMs, success, error, metadata: metadata));
    }

    private static Dictionary<string, string> Meta(params (string Key, string Value)[] pairs)
        => pairs.Where(x => x.Value != null).ToDictionary(x => x.Key, x => x.Value);
}
