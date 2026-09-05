using Tharga.Team.Service;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Support-case operations over the persistence port.
/// </summary>
/// <remarks>
/// <b>This class authorizes nothing.</b> Every check lives in
/// <see cref="AuthorizationSupportCaseServiceDecorator"/>, which wraps it — one enforcement point, so there
/// is no second place to keep in step and no path that reaches the store having skipped a check. What is
/// here is the domain: assigning identity, stamping the author, and composing the operations the store
/// applies atomically.
/// </remarks>
internal sealed class SupportCaseService(
    ISupportCaseStore store,
    TeamAuthorizer authorizer,
    TimeProvider timeProvider,
    IEnumerable<ISupportChannel> channels = null,
    ISupportCaseNotifier notifier = null) : ISupportCaseService
{
    /// <remarks>
    /// <b>Several channels at once, because they face different people.</b> Email faces the customer and
    /// Slack faces support, so a case can hold a binding for each — which is what
    /// <see cref="SupportCase.Bindings"/> being an array has always meant. This took a single channel until
    /// 3.18, so configuring both silently used whichever registered first.
    /// </remarks>
    private readonly ISupportChannel[] _channels = channels?.ToArray() ?? [];

    public async Task<SupportCase> RaiseCaseAsync(string teamKey, string subject, string body, CancellationToken cancellationToken = default)
    {
        RequireWithinLength(body);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var subjectIdentity = await authorizer.GetSubjectAsync();
        var authorName = await authorizer.GetDisplayNameAsync();

        var supportCase = new SupportCase
        {
            Id = Guid.NewGuid().ToString(),
            TeamKey = teamKey,
            AuthorIdentity = subjectIdentity,
            AuthorName = authorName,
            Subject = string.IsNullOrWhiteSpace(subject) ? SubjectFromMessage.Derive(body) : subject.Trim(),
            Status = SupportCaseStatus.Open,
            CreatedAt = now,
            MessageCount = 1
        };

        var firstMessage = new SupportMessage
        {
            Sequence = 1,
            Kind = SupportMessageKind.User,
            AuthorIdentity = subjectIdentity,
            AuthorName = authorName,
            Body = body,
            SentAt = now
        };

        await store.AddCaseAsync(supportCase, firstMessage, cancellationToken);

        // The case is written first and is authoritative. Projecting it onto a channel comes after, so a
        // channel that is slow, misconfigured or down cannot stop somebody reporting a problem -- they get a
        // case either way, and an undelivered entry stays visible as Pending rather than being lost.
        await ProjectAsync(supportCase, body, firstMessage.Sequence, cancellationToken);

        Notify(teamKey, supportCase.Id, SupportCaseChange.Raised);

        return supportCase;
    }

    public async Task ReplyToCaseAsync(string teamKey, string caseId, string body, CancellationToken cancellationToken = default)
    {
        RequireWithinLength(body);

        // Costs a read before the write, and is worth it: the caller learns the case is full before typing
        // is discarded, and the limit is checked in the domain so a second adapter inherits it rather than
        // reimplementing it.
        var existing = await store.GetCaseAsync(teamKey, caseId, cancellationToken);
        if (existing != null && existing.MessageCount >= SupportCaseLimits.MaxMessagesPerCase)
            throw new InvalidOperationException(
                $"Support case '{caseId}' already holds {existing.MessageCount} messages, which is the limit of " +
                $"{SupportCaseLimits.MaxMessagesPerCase}. Raise a new case rather than extending this one.");

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var message = new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.User,
            AuthorIdentity = await authorizer.GetSubjectAsync(),
            AuthorName = await authorizer.GetDisplayNameAsync(),
            Body = body,
            SentAt = now
        };

        await store.AppendMessageAsync(teamKey, caseId, message, cancellationToken);

        await DeliverAsync(teamKey, caseId, existing, message with { Sequence = (existing?.MessageCount ?? 0) + 1 }, cancellationToken);

        Notify(teamKey, caseId, SupportCaseChange.Replied);
    }

    /// <remarks>
    /// Raised after the write, never before: a notification for something that then failed to persist would
    /// send a listener to read a case that does not say what it was told.
    /// </remarks>
    private void Notify(string teamKey, string caseId, SupportCaseChange change) =>
        notifier?.Notify(new SupportCaseUpdatedEventArgs
        {
            TeamKey = teamKey,
            CaseId = caseId,
            Change = change,
            FromChannel = false
        });

    /// <summary>
    /// Opens a projection on every configured channel and records whether the opening message got anywhere.
    /// </summary>
    /// <remarks>
    /// <b>Delivered means it reached at least one channel, not all of them.</b> Delivery is one field on one
    /// entry, and channels are asymmetric on purpose: a case raised on the site opens a Slack thread for
    /// support and opens nothing by mail, because the person who typed it is already looking at the site.
    /// Requiring every channel to accept would mark a perfectly delivered entry as Pending forever.
    /// <para>
    /// <b>A refusal is not a failure.</b> Every channel returning null — none configured, or all of them
    /// declining — leaves the entry Pending, which is something a retry or a reminder can act on. The case
    /// itself already stands on its own.
    /// </para>
    /// </remarks>
    private async Task ProjectAsync(SupportCase supportCase, string body, int sequence, CancellationToken cancellationToken)
    {
        if (_channels.Length == 0) return;

        var opened = false;

        foreach (var channel in _channels)
        {
            var binding = await channel.OpenAsync(supportCase, body, cancellationToken);

            if (binding == null) continue;

            await store.AddBindingAsync(supportCase.TeamKey, supportCase.Id, binding, cancellationToken);
            opened = true;
        }

        await store.SetMessageDeliveryAsync(supportCase.TeamKey, supportCase.Id, sequence,
            opened ? SupportMessageDelivery.Sent : SupportMessageDelivery.Pending, cancellationToken);
    }

    /// <summary>Posts a reply into every projection the case has.</summary>
    /// <remarks>
    /// <b>Into all of them, because they face different people.</b> Support answering a case that arrived by
    /// mail has to reach the customer's inbox *and* the Slack thread support is reading, and each channel is
    /// given only a binding of its own type.
    /// <para>
    /// <b>Pending and Failed mean different things, and the difference is worth keeping.</b> No binding at
    /// all is Pending — nothing has been tried, and a projection may yet open. A binding that refused the
    /// post is Failed, which is a thing that went wrong. A reply that reached one channel and was refused by
    /// another counts as Sent: the entry is one field, and it did reach somebody.
    /// </para>
    /// </remarks>
    private async Task DeliverAsync(string teamKey, string caseId, SupportCase existing, SupportMessage message, CancellationToken cancellationToken)
    {
        if (_channels.Length == 0) return;

        var attempted = false;
        var delivered = false;

        foreach (var channel in _channels)
        {
            var binding = existing?.Bindings?.FirstOrDefault(x => x.ChannelType == channel.ChannelType);

            if (binding == null) continue;

            attempted = true;
            delivered |= await channel.PostAsync(binding, message, cancellationToken);
        }

        var status = !attempted
            ? SupportMessageDelivery.Pending
            : delivered
                ? SupportMessageDelivery.Sent
                : SupportMessageDelivery.Failed;

        await store.SetMessageDeliveryAsync(teamKey, caseId, message.Sequence, status, cancellationToken);
    }

    public async Task CloseCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var subjectIdentity = await authorizer.GetSubjectAsync();
        var authorName = await authorizer.GetDisplayNameAsync();

        // The closure goes into the transcript rather than only onto the header, so reading the conversation
        // shows how it ended. A closure with no trace of who closed it reads as a gap.
        var closure = new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.System,
            AuthorIdentity = subjectIdentity,
            AuthorName = authorName,
            Body = $"Case closed by {authorName}.",
            SentAt = now
        };

        await store.CloseCaseAsync(teamKey, caseId, now, subjectIdentity, closure, cancellationToken);

        Notify(teamKey, caseId, SupportCaseChange.Closed);
    }

    public async Task ReopenCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        var supportCase = await store.GetCaseAsync(teamKey, caseId, cancellationToken);

        // Already open is not a failure. Two people looking at the same closed case both press reopen, and
        // the second one should see an open case rather than an error about it already being open.
        if (supportCase == null || supportCase.Status != SupportCaseStatus.Closed) return;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var authorName = await authorizer.GetDisplayNameAsync();

        var reopen = new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.System,
            AuthorIdentity = await authorizer.GetSubjectAsync(),
            AuthorName = authorName,
            Body = $"Case reopened by {authorName}.",
            SentAt = now
        };

        await store.ReopenCaseAsync(teamKey, caseId, reopen, cancellationToken);

        Notify(teamKey, caseId, SupportCaseChange.Reopened);
    }

    public async Task<bool> AssignCaseAsync(string caseId, string teamKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamKey)) throw new ArgumentException("A case must be assigned to a team.", nameof(teamKey));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var actor = await authorizer.GetDisplayNameAsync();

        // In the transcript, because which tenant a case belongs to is part of its history: a case that
        // changed hands and says nothing about it reads as having always been there.
        var note = new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.System,
            AuthorIdentity = await authorizer.GetSubjectAsync(),
            AuthorName = actor,
            Body = $"Assigned to team {teamKey} by {actor}.",
            SentAt = now
        };

        // Conditional in the store, so two agents triaging the same queue do not both assign it. The loser
        // is told rather than left looking at a button that did nothing -- the case is now somebody else's,
        // and a queue that silently swallows the second click is how an operator stops trusting it.
        if (!await store.TryAssignCaseAsync(caseId, teamKey, note, cancellationToken)) return false;

        Notify(teamKey, caseId, SupportCaseChange.Assigned);

        return true;
    }

    public Task<SupportCasePage> GetUnassignedCasesAsync(string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
        => store.GetUnassignedCasesAsync(cursor, pageSize, cancellationToken);

    public Task<SupportCase> GetCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
        => store.GetCaseAsync(teamKey, caseId, cancellationToken);

    public Task<SupportCasePage> GetCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
        => store.GetCasesAsync(teamKey, cursor, pageSize, cancellationToken);

    /// <remarks>
    /// <b>A caller with no subject is shown nothing, rather than everything unattributed.</b> A case raised
    /// by inbound mail has no author identity, so matching on an empty subject would list every one of them
    /// as the caller's own -- and a principal without a name identifier is a configuration a host can
    /// produce.
    /// </remarks>
    public async Task<SupportCasePage> GetMyCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var subject = await authorizer.GetSubjectAsync();

        if (string.IsNullOrEmpty(subject)) return new SupportCasePage { Items = [] };

        return await store.GetCasesByAuthorAsync(teamKey, subject, cursor, pageSize, cancellationToken);
    }

    public Task<SupportMessagePage> GetMessagesAsync(string teamKey, string caseId, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default)
        => store.GetMessagesAsync(teamKey, caseId, cursor, pageSize, cancellationToken);

    public async Task MarkReadAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        var existing = await store.GetCaseAsync(teamKey, caseId, cancellationToken);
        if (existing == null) return;

        await store.MarkReadAsync(teamKey, caseId, await authorizer.GetSubjectAsync(), existing.MessageCount, cancellationToken);
    }

    public async Task<int> GetMyUnreadCountAsync(string teamKey, CancellationToken cancellationToken = default)
        => await store.GetUnreadCountAsync(teamKey, await authorizer.GetSubjectAsync(), cancellationToken);

    public Task<int> GetAwaitingSupportCountAsync(string teamKey, CancellationToken cancellationToken = default)
        => store.GetAwaitingSupportCountAsync(teamKey, cancellationToken);

    /// <remarks>
    /// <b>Enforced here rather than in the adapter, because the limit is a contract fact.</b>
    /// <see cref="SupportCaseLimits"/> lives beside the contracts and is documented for callers, so a second
    /// storage adapter must inherit the rule rather than be trusted to reimplement it. The MongoDB adapter
    /// keeps its own check as a last-resort backstop for a caller that reaches the port directly.
    /// </remarks>
    private static void RequireWithinLength(string body)
    {
        if (body != null && body.Length > SupportCaseLimits.MaxMessageLength)
            throw new InvalidOperationException(
                $"A support message is {body.Length} characters, which exceeds the limit of " +
                $"{SupportCaseLimits.MaxMessageLength}. Attach or link long content instead of pasting it.");
    }
}
