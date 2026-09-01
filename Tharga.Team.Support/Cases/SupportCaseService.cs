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
internal sealed class SupportCaseService(ISupportCaseStore store, TeamAuthorizer authorizer, TimeProvider timeProvider, ISupportChannel channel = null, ISupportCaseNotifier notifier = null) : ISupportCaseService
{
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
    /// Opens the channel projection for a new case and records whether the opening message got there.
    /// </summary>
    private async Task ProjectAsync(SupportCase supportCase, string body, int sequence, CancellationToken cancellationToken)
    {
        if (channel == null) return;

        var binding = await channel.OpenAsync(supportCase, body, cancellationToken);

        if (binding == null)
        {
            // No channel configured, or it refused. Neither is an error: the case stands on its own, and
            // Pending marks the entry as something a retry or a reminder can act on.
            await store.SetMessageDeliveryAsync(supportCase.TeamKey, supportCase.Id, sequence, SupportMessageDelivery.Pending, cancellationToken);
            return;
        }

        await store.AddBindingAsync(supportCase.TeamKey, supportCase.Id, binding, cancellationToken);
        await store.SetMessageDeliveryAsync(supportCase.TeamKey, supportCase.Id, sequence, SupportMessageDelivery.Sent, cancellationToken);
    }

    /// <summary>Posts a reply into the case's existing projection, if it has one.</summary>
    private async Task DeliverAsync(string teamKey, string caseId, SupportCase existing, SupportMessage message, CancellationToken cancellationToken)
    {
        if (channel == null) return;

        var binding = existing?.Bindings?.FirstOrDefault(x => x.ChannelType == channel.ChannelType);

        if (binding == null)
        {
            await store.SetMessageDeliveryAsync(teamKey, caseId, message.Sequence, SupportMessageDelivery.Pending, cancellationToken);
            return;
        }

        var delivered = await channel.PostAsync(binding, message, cancellationToken);

        await store.SetMessageDeliveryAsync(teamKey, caseId, message.Sequence,
            delivered ? SupportMessageDelivery.Sent : SupportMessageDelivery.Failed, cancellationToken);
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

    public Task<SupportCase> GetCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
        => store.GetCaseAsync(teamKey, caseId, cancellationToken);

    public Task<SupportCasePage> GetCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
        => store.GetCasesAsync(teamKey, cursor, pageSize, cancellationToken);

    public async Task<SupportCasePage> GetMyCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
        => await store.GetCasesByAuthorAsync(teamKey, await authorizer.GetSubjectAsync(), cursor, pageSize, cancellationToken);

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
