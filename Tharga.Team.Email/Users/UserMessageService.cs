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
    ILogger<UserMessageService> logger = null,
    UserMailFooter footer = null,
    MailTemplate template = null) : IUserMessageService
{
    private const int MaxPageSize = 100;

    /// <remarks>Reads the teams and the roles and levels their members hold, never the users: a product with tens of thousands of them is searched instead.</remarks>
    public async Task<UserMessageChoices> GetChoicesAsync(CancellationToken cancellationToken = default)
    {
        var teams = await members.GetTeamsAsync(cancellationToken) ?? [];
        var held = await members.GetRolesAndLevelsAsync(cancellationToken) ?? new UserMessageRolesAndLevels();

        return new UserMessageChoices
        {
            Teams = [.. teams.OrderBy(x => x.Name ?? x.Key, StringComparer.CurrentCultureIgnoreCase)],
            Roles = [.. (held.Roles ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            AccessLevels = [.. (held.AccessLevels ?? []).Distinct().Order()],
            Categories = [.. StaffCategories()],
            Limits = Limits()
        };
    }

    public async Task<UserMessageCandidate[]> SearchUsersAsync(UserMessageUserSearch search, CancellationToken cancellationToken = default)
    {
        var text = search?.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return [];

        string[] teams = search.AllTeams ? null : [.. (search.TeamKeys ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal)];
        if (teams is { Length: 0 }) return [];

        var limit = Math.Clamp(search.Limit, 1, UserMessageUserSearch.MaxLimit);
        var found = await members.SearchActiveMembersAsync(text, teams, limit, cancellationToken) ?? [];

        return [.. found
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.UserKey) && (teams == null || teams.Contains(x.TeamKey, StringComparer.Ordinal)))
            .GroupBy(x => x.UserKey, StringComparer.Ordinal)
            .Take(limit)
            .Select(x => new UserMessageCandidate
            {
                UserKey = x.Key,
                Name = x.Select(y => y.Name).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)),
                EMail = x.Select(y => y.EMail).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)),
                TeamKeys = [.. x.Select(y => y.TeamKey).Distinct(StringComparer.Ordinal)]
            })
            .OrderBy(x => x.Name ?? x.EMail ?? x.UserKey, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<UserMessagePreview> PreviewAsync(UserMessageRecipients recipients, string category, CancellationToken cancellationToken = default)
    {
        var allowed = StaffCategory(category);
        var reached = await ReachAsync(recipients, cancellationToken);
        var optedOut = await OptedOutAsync(allowed, reached, cancellationToken);

        return new UserMessagePreview { Recipients = reached.Length, LeftOut = optedOut.Count };
    }

    /// <remarks>Ordered by name, then address, then key; the names and addresses on a page are read from the user records as sending reads them, falling back to the membership's.</remarks>
    public async Task<UserMessageAddresseePage> GetAddresseesAsync(UserMessageRecipients selection, string category, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var allowed = StaffCategory(category);
        var reached = await ReachAsync(selection, cancellationToken);
        var optedOut = await OptedOutAsync(allowed, reached, cancellationToken);

        var page = reached
            .OrderBy(x => x.Member.Name ?? x.Member.EMail ?? x.UserKey, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Member.EMail, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.UserKey, StringComparer.Ordinal)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, MaxPageSize))
            .ToArray();

        var records = (await recipients.FindManyAsync([.. page.Select(x => x.UserKey)], cancellationToken) ?? [])
            .Where(x => x?.Key != null)
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        return new UserMessageAddresseePage
        {
            Items = [.. page.Select(x => Addressee(x, records.GetValueOrDefault(x.UserKey), optedOut.Contains(x.UserKey)))],
            Total = reached.Length,
            LeftOut = optedOut.Count
        };
    }

    /// <remarks>Composed by the code that composes each mail sent, for a recipient nobody in particular: the unsubscribe link it shows unsubscribes nobody.</remarks>
    public async Task<MailPreview> PreviewMailAsync(UserMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentException("There is no message to preview.", nameof(message));

        var category = StaffCategory(message.Category);
        var (subject, body, html) = UserMailComposer.Draft(message.Subject, message.Body, message.BodyHtml);
        var composed = await Composer().ComposeAsync(subject, body, html, null, category);
        var o = mailOptions.Value;

        return new MailPreview
        {
            From = UserMailComposer.Sender(o),
            ReplyTo = string.IsNullOrWhiteSpace(o.ReplyTo) ? null : o.ReplyTo.Trim(),
            Subject = subject,
            Html = composed.BodyHtml,
            Text = composed.Body
        };
    }

    private UserMailComposer Composer()
        => new(footer ?? throw new InvalidOperationException($"Previewing a message needs the user mail footer, which {nameof(UserMailRegistration.AddThargaUserMail)} registers."), template);

    private static UserMessageAddressee Addressee(UserMessageReached reached, UserMailRecipient record, bool optedOut) => new()
    {
        UserKey = reached.UserKey,
        Name = !string.IsNullOrWhiteSpace(reached.Member.Name) ? reached.Member.Name : record?.Name,
        EMail = !string.IsNullOrWhiteSpace(record?.EMail) ? record.EMail.Trim() : reached.Member.EMail,
        TeamKeys = reached.TeamKeys,
        ByFilter = reached.ByFilter,
        Added = reached.Added,
        OptedOut = optedOut
    };

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
        var chosen = entry.Recipients?.UserKeys ?? [];
        var names = await NamesAsync(chosen, cancellationToken);
        var sent = ToSent(entry, delivery?.Outcomes ?? new UserMailOutcomes()) with
        {
            Failures = [.. await NamedAsync(delivery?.Failures ?? [], cancellationToken)],
            ChosenUsers = [.. chosen.Select(x => new UserMessageCandidate { UserKey = x, Name = names.GetValueOrDefault(x) })]
        };

        Audit(AuditEventType.ServiceCall, UserMessageAudit.OpenAction, nameof(GetSentMessageAsync), sw.ElapsedMilliseconds, true, null, Meta(
            (UserMessageAudit.MessageId, sent.Id),
            (UserMessageAudit.Recipients, Number(sent.RecipientCount)),
            (UserMessageAudit.Queued, Number(sent.Outcomes.Queued)),
            (UserMessageAudit.Sent, Number(sent.Outcomes.Sent)),
            (UserMessageAudit.Skipped, Number(sent.Outcomes.Skipped)),
            (UserMessageAudit.Failed, Number(sent.Outcomes.Failed)),
            (UserMessageAudit.Bounced, Number(sent.Outcomes.Bounced))));

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

    /// <summary>The team a message is about: the one its recipients are chosen within, or none when they are chosen within several teams or all of them.</summary>
    private static string TeamOf(UserMessageRecipients recipients)
    {
        var audience = UserMessageAudience.Normalize(recipients);

        return audience is { AllTeams: false, TeamKeys.Length: 1 } ? audience.TeamKeys[0] : null;
    }

    /// <remarks>Queued before it is recorded, so a message that cannot be queued leaves no record; one that cannot then be recorded is still sent, and that is logged with its id.</remarks>
    private async Task<SentUserMessage> QueueAndRecordAsync(UserMessage message, CancellationToken cancellationToken)
    {
        if (message == null) throw new ArgumentException("There is no message to send.", nameof(message));

        var category = StaffCategory(message.Category);
        var reached = await ReachAsync(message.Recipients, cancellationToken);
        if (reached.Length == 0) throw new ArgumentException("The selection reaches nobody: no active member of the chosen teams matches it, and no user added is an active member of any team.", nameof(message));

        var queued = await queue.QueueBatchAsync(new UserMailBatch
        {
            RecipientUserKeys = [.. reached.Select(x => x.UserKey)],
            Category = category.Key,
            Subject = message.Subject,
            BodyHtml = message.BodyHtml,
            Body = message.Body,
            Attachments = message.Attachments ?? [],
            TeamKey = TeamOf(message.Recipients)
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

    /// <summary>Who the selection reaches: the filter read only when it has teams and someone to reach in them, the added users only when there are some.</summary>
    private async Task<UserMessageReached[]> ReachAsync(UserMessageRecipients selection, CancellationToken cancellationToken)
    {
        var teams = UserMessageAudience.FilterTeamsOf(selection);
        var added = UserMessageAudience.AddedOf(selection);

        var filter = teams == null
            ? await members.GetActiveMembersOfEveryTeamAsync(cancellationToken)
            : teams.Length == 0 ? [] : await members.GetActiveMembersAsync(teams, cancellationToken);
        var byKey = added.Length == 0 ? [] : await members.GetActiveMembershipsOfUsersAsync(added, cancellationToken);

        return UserMessageAudience.Select(filter ?? [], byKey ?? [], selection);
    }

    /// <summary>The users reached who turned the category off; none for a required category.</summary>
    private async Task<HashSet<string>> OptedOutAsync(UserMailCategory category, UserMessageReached[] reached, CancellationToken cancellationToken)
    {
        if (category.Required || reached.Length == 0) return new HashSet<string>(StringComparer.Ordinal);

        var stored = await preferences.GetChoicesForCategoryAsync(category.Key, [.. reached.Select(x => x.UserKey)], cancellationToken) ?? new Dictionary<string, bool>();

        return reached
            .Where(x => !category.Receives(stored.TryGetValue(x.UserKey, out var receives) ? receives : null))
            .Select(x => x.UserKey)
            .ToHashSet(StringComparer.Ordinal);
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
