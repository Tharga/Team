using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Users;

/// <summary>Turns raised occurrences into users' notices and mails what is due, each step claimed so several instances share the work without sending anything twice.</summary>
/// <remarks>
/// <b>Distributing</b> finds an occurrence's recipients among the active members of its team — whoever the catalogue's
/// rule admits and whoever the raiser named — and stores a notice for each who receives it, by their choice, else the
/// team's default, else the catalogue's. <b>Flushing</b> claims one user's stream and queues one mail for everything
/// waiting in it: notices delivered at once wait the burst window from when they were raised, so a burst goes as one
/// mail, and the daily summary waits for the first summary time after it. Whether the user still receives the mail category is checked when the mail is sent.
/// </remarks>
internal sealed class UserNoticeDistributor(
    IUserNoticeOutbox outbox,
    UserNoticeRegistry notices,
    IUserMessageMemberDirectory members,
    IUserNoticePreferenceStore preferences,
    UserNoticeEligibility eligibility,
    IUserMailQueue queue,
    UserNoticeMail mail,
    UserNoticeSchedule schedule,
    IServiceProvider services,
    IOptions<UserNoticeOptions> options,
    IOptions<UserMailOptions> mailOptions,
    TimeProvider timeProvider,
    ILogger<UserNoticeDistributor> logger = null)
{
    internal const string NotInCatalogueReason = "The notice is no longer in the catalogue.";
    internal const string TurnedOffReason = "The user turned the notice off before it was sent.";

    /// <summary>Distributes due occurrences, then mails due streams, each up to a batch, and returns how many of either were handled.</summary>
    public async Task<int> RunAsync(string claimant, CancellationToken cancellationToken)
    {
        var batch = Math.Max(1, options.Value.BatchSize);
        var handled = 0;

        for (var i = 0; i < batch && !cancellationToken.IsCancellationRequested && await DistributeNextAsync(claimant, cancellationToken); i++) handled++;
        for (var i = 0; i < batch && !cancellationToken.IsCancellationRequested && await FlushNextAsync(claimant, cancellationToken); i++) handled++;

        return handled;
    }

    /// <summary>Claims and distributes the next due occurrence; false when none was due.</summary>
    public async Task<bool> DistributeNextAsync(string claimant, CancellationToken cancellationToken)
    {
        var record = await outbox.ClaimNextAsync(claimant, Now(), options.Value.Lease, cancellationToken);
        if (record == null) return false;

        try
        {
            var notice = notices.Find(record.Occurrence.NoticeKey);
            if (notice == null)
            {
                await Record(outbox.MarkFailedAsync(record.Id, claimant, NotInCatalogueReason, Now(), cancellationToken), record.Id);
                return true;
            }

            if (record.Attempts > options.Value.MaxAttempts)
            {
                await Record(outbox.MarkFailedAsync(record.Id, claimant, $"Gave up after {record.Attempts - 1} attempts. {record.Reason}".Trim(), Now(), cancellationToken), record.Id);
                return true;
            }

            var reached = await DistributeAsync(record, notice, cancellationToken);
            await Record(outbox.MarkDistributedAsync(record.Id, claimant, reached, Now(), cancellationToken), record.Id);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(e, "Notice {Id} could not be distributed.", record.Id);
            await RetryAsync(record, claimant, e.Message, cancellationToken);
        }

        return true;
    }

    /// <summary>Claims the next due stream and queues one mail for what waits in it; false when none was due.</summary>
    public async Task<bool> FlushNextAsync(string claimant, CancellationToken cancellationToken)
    {
        var claim = await outbox.ClaimDueStreamAsync(claimant, Now(), options.Value.Lease, cancellationToken);
        if (claim == null) return false;

        try
        {
            await FlushAsync(claim, claimant, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(e, "The notices waiting in stream {Stream} of user {UserKey} could not be mailed; they are tried again once the claim runs out.", claim.Stream, claim.UserKey);
        }

        return true;
    }

    private async Task<int> DistributeAsync(UserNoticeRecord record, UserNoticeType notice, CancellationToken cancellationToken)
    {
        var occurrence = record.Occurrence;
        var recipients = await RecipientsAsync(notice, occurrence, cancellationToken);
        if (recipients.Count == 0) return 0;

        var choices = await preferences.GetChoicesForNoticeAsync(notice.Key, recipients, cancellationToken) ?? new Dictionary<string, UserNoticeSetting>();
        var teamDefault = (await preferences.GetTeamDefaultsAsync(occurrence.TeamKey, cancellationToken))?.GetValueOrDefault(notice.Key);
        var occurrenceKey = UserNoticeRaiser.OccurrenceKey(occurrence);
        var reached = 0;

        foreach (var userKey in recipients)
        {
            var (enabled, delivery) = UserNoticeSettings.Effective(notice, choices.GetValueOrDefault(userKey), teamDefault);
            if (!enabled) continue;

            var stream = delivery == UserNoticeDelivery.Digest ? UserNoticeMail.DigestStream(notice.Category) : UserNoticeMail.AtOnceStream(notice.Key);
            var dueBy = delivery == UserNoticeDelivery.Digest ? schedule.NextDigest(record.RaisedAt) : record.RaisedAt + Max(options.Value.BurstWindow, TimeSpan.Zero);

            var item = new UserNoticeItem
            {
                NoticeId = record.Id,
                OccurrenceKey = occurrenceKey,
                UserKey = userKey,
                TeamKey = occurrence.TeamKey,
                NoticeKey = notice.Key,
                Category = notice.Category,
                Stream = stream,
                Delivery = delivery,
                SubjectId = occurrence.SubjectId,
                SubjectText = occurrence.SubjectText,
                Values = occurrence.Values,
                LinkPath = occurrence.LinkPath,
                RaisedAt = record.RaisedAt,
                Status = UserNoticeItemStatus.Waiting
            };

            if (await outbox.AddItemAsync(item, dueBy, cancellationToken)) reached++;
        }

        return reached;
    }

    /// <summary>Whoever the rule admits and whoever the raiser named, among the active members of the occurrence's team, each once.</summary>
    internal async Task<IReadOnlyCollection<string>> RecipientsAsync(UserNoticeType notice, UserNoticeOccurrence occurrence, CancellationToken cancellationToken)
    {
        var memberships = (await members.GetActiveMembersAsync([occurrence.TeamKey], cancellationToken) ?? [])
            .Where(x => x != null && x.TeamKey == occurrence.TeamKey && !string.IsNullOrWhiteSpace(x.UserKey))
            .GroupBy(x => x.UserKey, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        var recipients = new List<string>();

        void Add(string userKey)
        {
            var key = userKey?.Trim();
            if (key != null && memberships.ContainsKey(key) && !recipients.Contains(key, StringComparer.Ordinal)) recipients.Add(key);
        }

        if (notice.Audience.SelectsByMembership)
        {
            foreach (var membership in memberships.Values)
            {
                if (await eligibility.MayReceiveAsync(notice, membership)) Add(membership.UserKey);
            }
        }
        else if (notice.Audience.Kind == UserNoticeAudienceKind.Resolved)
        {
            foreach (var userKey in await notice.Audience.Resolver(occurrence, services, cancellationToken) ?? []) Add(userKey);
        }

        foreach (var userKey in occurrence.Recipients ?? []) Add(userKey);

        return recipients;
    }

    private async Task FlushAsync(UserNoticeStreamClaim claim, string claimant, CancellationToken cancellationToken)
    {
        var now = Now();
        var inherited = claim.MailKey != null;
        var items = inherited
            ? await outbox.GetItemsAsync(claim.MailItemIds ?? [], cancellationToken)
            : await outbox.GetWaitingAsync(claim.UserKey, claim.Stream, Math.Max(1, options.Value.MaxItemsPerMail), cancellationToken);

        items = [.. (items ?? []).Where(x => x.Status == UserNoticeItemStatus.Waiting)];

        var gone = items.Where(x => notices.Find(x.NoticeKey) == null).ToArray();
        if (gone.Length > 0) await outbox.CompleteItemsAsync([.. gone.Select(x => x.Id)], UserNoticeItemStatus.Failed, null, NotInCatalogueReason, now, cancellationToken);

        items = [.. items.Except(gone)];

        var off = await TurnedOffAsync(claim.UserKey, items, cancellationToken);
        if (off.Length > 0) await outbox.CompleteItemsAsync([.. off.Select(x => x.Id)], UserNoticeItemStatus.Skipped, null, TurnedOffReason, now, cancellationToken);

        items = [.. items.Except(off).OrderBy(x => x.RaisedAt)];
        if (items.Length > 0) await MailAsync(claim, claimant, items, inherited, now, cancellationToken);

        await outbox.ReleaseStreamAsync(claim.UserKey, claim.Stream, claimant, NextDue(claim.Stream, now), cancellationToken);
    }

    /// <summary>The notices the user turned off since they were distributed, by their choice, else their team's default, else the catalogue's.</summary>
    private async Task<UserNoticeItem[]> TurnedOffAsync(string userKey, UserNoticeItem[] items, CancellationToken cancellationToken)
    {
        if (items.Length == 0) return [];

        var choices = await preferences.GetChoicesAsync(userKey, cancellationToken) ?? new Dictionary<string, UserNoticeSetting>();
        var teams = new Dictionary<string, IReadOnlyDictionary<string, UserNoticeSetting>>(StringComparer.Ordinal);
        var off = new List<UserNoticeItem>();

        foreach (var item in items)
        {
            if (!teams.TryGetValue(item.TeamKey, out var team))
            {
                team = await preferences.GetTeamDefaultsAsync(item.TeamKey, cancellationToken) ?? new Dictionary<string, UserNoticeSetting>();
                teams[item.TeamKey] = team;
            }

            var notice = notices.Find(item.NoticeKey);
            if (!UserNoticeSettings.Effective(notice, choices.GetValueOrDefault(item.NoticeKey), team.GetValueOrDefault(item.NoticeKey)).Enabled) off.Add(item);
        }

        return [.. off];
    }

    private async Task MailAsync(UserNoticeStreamClaim claim, string claimant, UserNoticeItem[] items, bool inherited, DateTime now, CancellationToken cancellationToken)
    {
        string[] ids = [.. items.Select(x => x.Id)];
        var mailKey = inherited ? claim.MailKey : UserNoticeMail.MailKey(claim.UserKey, claim.Stream, schedule.LocalDate(now));

        if (!inherited && !await outbox.BeginMailAsync(claim.UserKey, claim.Stream, claimant, mailKey, ids, cancellationToken))
        {
            logger?.LogWarning("Stream {Stream} of user {UserKey} was taken by another instance before its mail was begun here.", claim.Stream, claim.UserKey);
            return;
        }

        UserMailQueued queued;
        try
        {
            queued = await queue.QueueAsync(await mail.ComposeAsync(claim.UserKey, claim.Stream, items) with { DeduplicationKey = mailKey }, cancellationToken);
        }
        catch (ArgumentException e)
        {
            await outbox.CompleteItemsAsync(ids, UserNoticeItemStatus.Failed, null, e.Message, now, cancellationToken);
            return;
        }

        if (queued.Duplicate && !inherited)
        {
            logger?.LogInformation("A summary for stream {Stream} of user {UserKey} was already sent today; what waits goes in the next one.", claim.Stream, claim.UserKey);
            return;
        }

        await outbox.CompleteItemsAsync(ids, UserNoticeItemStatus.Mailed, queued.Id, null, now, cancellationToken);
    }

    private DateTime NextDue(string stream, DateTime now)
        => UserNoticeMail.IsDigest(stream) ? schedule.NextDigest(now) : now + Max(options.Value.BurstWindow, TimeSpan.Zero);

    private async Task RetryAsync(UserNoticeRecord record, string claimant, string reason, CancellationToken cancellationToken)
    {
        if (record.Attempts >= options.Value.MaxAttempts)
        {
            await Record(outbox.MarkFailedAsync(record.Id, claimant, $"Gave up after {record.Attempts} attempts. {reason}".Trim(), Now(), cancellationToken), record.Id);
            return;
        }

        await Record(outbox.PostponeAsync(record.Id, claimant, reason, Now() + UserMailDispatcher.Backoff(record.Attempts, mailOptions.Value), cancellationToken), record.Id);
    }

    private async Task Record(Task<bool> write, string id)
    {
        if (!await write) logger?.LogWarning("Notice {Id} was taken by another instance before its outcome was recorded here; the other instance's outcome stands.", id);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
