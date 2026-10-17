using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Users;

/// <summary>Writes staff messages to the users a selection reaches, through the user mail queue, and keeps the sent log.</summary>
/// <remarks>
/// Reached only through the scope proxy, which refuses a caller without <see cref="SystemMessageScopes.Send"/>.
/// The message's content and files are stored once as a batch, one mail is queued per user reached, and each is sent
/// with the host's From and Reply-To, after the recipient's choice for the category is checked again.
/// </remarks>
internal sealed class UserMessageService(
    UserMailCategoryRegistry categories,
    IOptions<UserMessageOptions> messageOptions,
    IOptions<UserMailOptions> mailOptions,
    IUserMessageMemberDirectory members,
    IUserMailPreferenceStore preferences,
    IUserMailQueue queue,
    IUserMessageLog log,
    IUserMailRecipientDirectory recipients,
    TeamAuthorizer authorizer,
    TimeProvider timeProvider,
    IUserService userService = null,
    CompositeAuditLogger auditLogger = null,
    IAuditEntryFactory auditEntryFactory = null,
    ILogger<UserMessageService> logger = null) : IUserMessageService
{
    private const int MaxPageSize = 100;

    public async Task<UserMessageChoices> GetChoicesAsync(CancellationToken cancellationToken = default)
    {
        var teams = await members.GetTeamsAsync(cancellationToken) ?? [];
        var memberships = await members.GetActiveMembersOfEveryTeamAsync(cancellationToken) ?? [];

        return new UserMessageChoices
        {
            Teams = [.. teams.OrderBy(x => x.Name ?? x.Key, StringComparer.CurrentCultureIgnoreCase)],
            Users = [.. memberships
                .GroupBy(x => x.UserKey, StringComparer.Ordinal)
                .Select(x => new UserMessageCandidate
                {
                    UserKey = x.Key,
                    Name = x.Select(y => y.Name).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)),
                    TeamKeys = [.. x.Select(y => y.TeamKey).Distinct(StringComparer.Ordinal)]
                })
                .OrderBy(x => x.Name ?? x.UserKey, StringComparer.CurrentCultureIgnoreCase)],
            Roles = [.. memberships.SelectMany(x => x.Roles ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            AccessLevels = [.. memberships.Select(x => x.AccessLevel).Distinct().Order()],
            Categories = [.. StaffCategories()],
            Limits = Limits()
        };
    }

    public async Task<UserMessagePreview> PreviewAsync(UserMessageRecipients recipients, string category, CancellationToken cancellationToken = default)
    {
        var allowed = StaffCategory(category);
        var reached = await ReachAsync(recipients, cancellationToken);

        return new UserMessagePreview { Recipients = reached.Length, LeftOut = await LeftOutAsync(allowed, reached, cancellationToken) };
    }

    public async Task<SentUserMessage> SendAsync(UserMessage message, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var sent = await QueueAndRecordAsync(message, cancellationToken);

            Audit(AuditEventType.DataChange, UserMessageAudit.SendAction, nameof(SendAsync), sw.ElapsedMilliseconds, true, null, Meta(
                (UserMessageAudit.MessageId, sent.Id),
                (UserMessageAudit.Category, sent.Category),
                (UserMessageAudit.Subject, sent.Subject),
                (UserMessageAudit.Recipients, Number(sent.RecipientCount)),
                (UserMessageAudit.Attachments, Number(sent.AttachmentCount)),
                (UserMessageAudit.Teams, TeamsOf(sent.Recipients))));

            return sent;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Audit(AuditEventType.DataChange, UserMessageAudit.SendAction, nameof(SendAsync), sw.ElapsedMilliseconds, false, e.Message, Meta(
                (UserMessageAudit.Category, message?.Category),
                (UserMessageAudit.Attachments, Number(message?.Attachments?.Count(x => x != null) ?? 0))));
            throw;
        }
    }

    public async Task<SentUserMessagePage> GetSentAsync(string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var page = await log.ListAsync(cursor, Math.Clamp(pageSize, 1, MaxPageSize), cancellationToken) ?? new UserMessageLogPage();

        var items = new List<SentUserMessage>();
        foreach (var entry in page.Items ?? [])
        {
            var delivery = await queue.GetBatchAsync(entry.Id, cancellationToken);
            items.Add(ToSent(entry, delivery?.Outcomes ?? new UserMailOutcomes()));
        }

        Audit(AuditEventType.ServiceCall, UserMessageAudit.LogAction, nameof(GetSentAsync), sw.ElapsedMilliseconds, true, null, Meta(
            (UserMessageAudit.Count, Number(items.Count))));

        return new SentUserMessagePage { Items = [.. items], NextCursor = page.NextCursor };
    }

    public async Task<SentUserMessage> GetSentMessageAsync(string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId)) return null;

        var sw = Stopwatch.StartNew();
        var entry = await log.GetAsync(messageId.Trim(), cancellationToken);
        if (entry == null) return null;

        var delivery = await queue.GetBatchAsync(entry.Id, cancellationToken);
        var sent = ToSent(entry, delivery?.Outcomes ?? new UserMailOutcomes()) with
        {
            Failures = [.. await NamedAsync(delivery?.Failures ?? [], cancellationToken)]
        };

        Audit(AuditEventType.ServiceCall, UserMessageAudit.OpenAction, nameof(GetSentMessageAsync), sw.ElapsedMilliseconds, true, null, Meta(
            (UserMessageAudit.MessageId, sent.Id),
            (UserMessageAudit.Recipients, Number(sent.RecipientCount)),
            (UserMessageAudit.Queued, Number(sent.Outcomes.Queued)),
            (UserMessageAudit.Sent, Number(sent.Outcomes.Sent)),
            (UserMessageAudit.Skipped, Number(sent.Outcomes.Skipped)),
            (UserMessageAudit.Failed, Number(sent.Outcomes.Failed))));

        return sent;
    }

    public async Task<UserMessageRecipientPage> GetRecipientsAsync(string messageId, UserMailStatus? status = null, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId)) return null;

        var sw = Stopwatch.StartNew();
        var entry = await log.GetAsync(messageId.Trim(), cancellationToken);
        if (entry == null) return null;

        var page = await queue.ListBatchAsync(entry.Id, status, cursor, Math.Clamp(pageSize, 1, MaxPageSize), cancellationToken) ?? new UserMailDeliveryPage();
        var names = await NamesAsync(page.Items.Select(x => x.RecipientUserKey), cancellationToken);

        UserMessageRecipient[] items = [.. page.Items.Select(x => new UserMessageRecipient
        {
            UserKey = x.RecipientUserKey,
            Name = x.RecipientUserKey != null && names.TryGetValue(x.RecipientUserKey, out var name) ? name : null,
            Address = x.RecipientAddress,
            Status = x.Status,
            Reason = x.Reason,
            ChangedAt = x.ChangedAt,
            MessageId = x.MessageId
        })];

        Audit(AuditEventType.ServiceCall, UserMessageAudit.RecipientsAction, nameof(GetRecipientsAsync), sw.ElapsedMilliseconds, true, null, Meta(
            (UserMessageAudit.MessageId, entry.Id),
            (UserMessageAudit.Status, status?.ToString()),
            (UserMessageAudit.Count, Number(items.Length))));

        return new UserMessageRecipientPage { Items = items, NextCursor = page.NextCursor };
    }

    private async Task<Dictionary<string, string>> NamesAsync(IEnumerable<string> userKeys, CancellationToken cancellationToken)
    {
        string[] keys = [.. userKeys.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal)];
        if (keys.Length == 0) return new Dictionary<string, string>(StringComparer.Ordinal);

        var found = await recipients.FindManyAsync(keys, cancellationToken) ?? [];

        return found.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().Name, StringComparer.Ordinal);
    }

    /// <remarks>Queued before it is recorded, so a message that cannot be queued leaves no record; one that cannot then be recorded is still sent, and that is logged with its id.</remarks>
    private async Task<SentUserMessage> QueueAndRecordAsync(UserMessage message, CancellationToken cancellationToken)
    {
        if (message == null) throw new ArgumentException("There is no message to send.", nameof(message));

        var category = StaffCategory(message.Category);
        var reached = await ReachAsync(message.Recipients, cancellationToken);
        if (reached.Length == 0) throw new ArgumentException("The selection reaches nobody: no active member of the chosen teams matches it.", nameof(message));

        var queued = await queue.QueueBatchAsync(new UserMailBatch
        {
            RecipientUserKeys = [.. reached.Select(x => x.UserKey)],
            Category = category.Key,
            Subject = message.Subject,
            BodyHtml = message.BodyHtml,
            Body = message.Body,
            Attachments = message.Attachments ?? []
        }, cancellationToken);

        var sender = await SenderAsync();
        var entry = new UserMessageLogEntry
        {
            Id = queued.Id,
            Recipients = UserMessageAudience.Normalize(message.Recipients),
            Category = category.Key,
            Subject = UserMailQueue.OneLine(message.Subject),
            AttachmentCount = (message.Attachments ?? []).Count(x => x != null),
            SentByUserKey = sender.UserKey,
            SentByName = sender.Name,
            SentAt = timeProvider.GetUtcNow().UtcDateTime,
            RecipientCount = queued.Recipients
        };

        try
        {
            await log.AddAsync(entry, cancellationToken);
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Message {MessageId} was queued to {Recipients} users but could not be recorded in the sent log.", queued.Id, queued.Recipients);
            throw;
        }

        return ToSent(entry, new UserMailOutcomes { Queued = queued.Recipients });
    }

    private async Task<UserMessageMember[]> ReachAsync(UserMessageRecipients selection, CancellationToken cancellationToken)
    {
        var teams = UserMessageAudience.TeamsOf(selection);

        var memberships = selection.AllTeams
            ? await members.GetActiveMembersOfEveryTeamAsync(cancellationToken)
            : await members.GetActiveMembersAsync(teams, cancellationToken);

        return UserMessageAudience.Select(memberships ?? [], selection);
    }

    private async Task<int> LeftOutAsync(UserMailCategory category, UserMessageMember[] reached, CancellationToken cancellationToken)
    {
        if (category.Required || reached.Length == 0) return 0;

        var stored = await preferences.GetChoicesForCategoryAsync(category.Key, [.. reached.Select(x => x.UserKey)], cancellationToken) ?? new Dictionary<string, bool>();

        return reached.Count(x => !category.Receives(stored.TryGetValue(x.UserKey, out var receives) ? receives : null));
    }

    private IEnumerable<UserMailCategory> StaffCategories()
        => (messageOptions.Value.StaffMessageCategories ?? []).Select(categories.Find).Where(x => x != null).Distinct();

    private UserMailCategory StaffCategory(string key)
        => StaffCategories().FirstOrDefault(x => x.Key == key?.Trim())
           ?? throw new ArgumentException($"'{key}' is not a category staff may write a message in.", nameof(key));

    private UserMessageLimits Limits()
    {
        var o = mailOptions.Value;

        return new UserMessageLimits { MaxAttachmentCount = o.MaxAttachmentCount, MaxAttachmentBytes = o.MaxAttachmentBytes, MaxBodyLength = o.MaxBodyLength };
    }

    private async Task<(string UserKey, string Name)> SenderAsync()
    {
        var user = userService == null ? null : await userService.GetCurrentUserAsync();
        var name = !string.IsNullOrWhiteSpace(user?.Name) ? user.Name : await authorizer.GetDisplayNameAsync();

        return (user?.Key, name);
    }

    private async Task<IEnumerable<UserMessageFailure>> NamedAsync(UserMailFailure[] failures, CancellationToken cancellationToken)
    {
        var names = await NamesAsync(failures.Select(x => x.UserKey), cancellationToken);

        return failures.Select(x => new UserMessageFailure { UserKey = x.UserKey, Name = x.UserKey != null ? names.GetValueOrDefault(x.UserKey) : null, Reason = x.Reason });
    }

    private static SentUserMessage ToSent(UserMessageLogEntry entry, UserMailOutcomes outcomes) => new()
    {
        Id = entry.Id,
        Recipients = entry.Recipients,
        Category = entry.Category,
        Subject = entry.Subject,
        AttachmentCount = entry.AttachmentCount,
        SentByUserKey = entry.SentByUserKey,
        SentByName = entry.SentByName,
        SentAt = entry.SentAt,
        RecipientCount = entry.RecipientCount,
        Outcomes = outcomes
    };

    private static string TeamsOf(UserMessageRecipients selection)
        => selection.AllTeams ? UserMessageAudit.AllTeams : Number(selection.TeamKeys?.Length ?? 0);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private void Audit(AuditEventType eventType, string action, string methodName, long durationMs, bool success, string error, IReadOnlyDictionary<string, string> metadata)
    {
        if (auditLogger == null || auditEntryFactory == null) return;

        auditLogger.Log(auditEntryFactory.Create(eventType, UserMessageAudit.Feature, action, methodName, durationMs, success, error, metadata: metadata));
    }

    private static Dictionary<string, string> Meta(params (string Key, string Value)[] pairs)
        => pairs.Where(x => x.Value != null).ToDictionary(x => x.Key, x => x.Value);
}
