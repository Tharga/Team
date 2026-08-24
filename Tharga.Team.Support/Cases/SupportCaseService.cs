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
internal sealed class SupportCaseService(ISupportCaseStore store, TeamAuthorizer authorizer, TimeProvider timeProvider) : ISupportCaseService
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
            Subject = subject,
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
    }

    public Task<SupportCase> GetCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
        => store.GetCaseAsync(teamKey, caseId, cancellationToken);

    public Task<SupportCasePage> GetCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
        => store.GetCasesAsync(teamKey, cursor, pageSize, cancellationToken);

    public async Task<SupportCasePage> GetMyCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
        => await store.GetCasesByAuthorAsync(teamKey, await authorizer.GetSubjectAsync(), cursor, pageSize, cancellationToken);

    public Task<SupportMessagePage> GetMessagesAsync(string teamKey, string caseId, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default)
        => store.GetMessagesAsync(teamKey, caseId, cursor, pageSize, cancellationToken);

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
