namespace Tharga.Team.Support.Tests;

/// <summary>
/// An in-memory <see cref="ISupportCaseStore"/> for exercising the operations and their authorization
/// without a database.
/// </summary>
/// <remarks>
/// <b>It keys on the team exactly as the real adapter does.</b> That is the point of the fake rather than an
/// incidental detail: if it looked cases up by id alone, the cross-tenant test would pass here and fail
/// against MongoDB, which is worse than having no test at all.
/// </remarks>
internal sealed class InMemorySupportCaseStore : ISupportCaseStore
{
    private readonly List<(SupportCase Case, List<SupportMessage> Messages)> _cases = [];
    private readonly Dictionary<(string CaseId, string Identity), int> _reads = [];

    public Task<SupportCase> GetCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
        => Task.FromResult(Find(teamKey, caseId)?.Case);

    public Task<SupportCasePage> GetCasesAsync(string teamKey, string cursor, int pageSize, CancellationToken cancellationToken = default)
        => Task.FromResult(Page(_cases.Where(x => x.Case.TeamKey == teamKey).Select(x => x.Case)));

    public Task<SupportCasePage> GetCasesByAuthorAsync(string teamKey, string authorIdentity, string cursor, int pageSize, CancellationToken cancellationToken = default)
        => Task.FromResult(Page(_cases
            .Where(x => x.Case.TeamKey == teamKey && x.Case.AuthorIdentity == authorIdentity)
            .Select(x => x.Case)));

    public Task<SupportMessagePage> GetMessagesAsync(string teamKey, string caseId, string cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var found = Find(teamKey, caseId);

        var after = int.TryParse(cursor, out var value) ? value : 0;

        var all = found?.Messages ?? [];

        var items = all
            .Where(x => x.Sequence > after)
            .OrderBy(x => x.Sequence)
            .Take(pageSize)
            .ToArray();

        // The cursor has to be produced the same way the real adapter produces it. A fake that returns
        // items but no cursor makes every paged read restart from the beginning, which reads as a paging
        // defect in the code under test rather than as a gap in the double.
        var last = items.Length == 0 ? 0 : items[^1].Sequence;
        var more = items.Length == pageSize && all.Any(x => x.Sequence > last);

        return Task.FromResult(new SupportMessagePage
        {
            Items = items,
            NextCursor = more ? last.ToString() : null
        });
    }

    public Task AddCaseAsync(SupportCase supportCase, SupportMessage firstMessage, CancellationToken cancellationToken = default)
    {
        _cases.Add((supportCase, [firstMessage]));

        return Task.CompletedTask;
    }

    public Task AppendMessageAsync(string teamKey, string caseId, SupportMessage message, CancellationToken cancellationToken = default)
    {
        var found = Require(teamKey, caseId);

        found.Messages.Add(message with { Sequence = found.Messages.Count + 1 });

        Replace(found.Case with { MessageCount = found.Messages.Count }, found.Messages);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Mirrors the Mongo store's predicate, including the part a filter cannot express: the newest entry must
    /// be a person's and not the author's, so a system entry never makes a case eligible.
    /// </summary>
    public Task<SupportCase[]> GetCasesForInactivityCloseAsync(DateTime lastActivityBefore, int limit, CancellationToken cancellationToken = default)
    {
        var due = new List<SupportCase>();

        foreach (var (supportCase, messages) in _cases.ToArray())
        {
            if (supportCase.Status != SupportCaseStatus.Open) continue;

            var last = messages.OrderBy(x => x.Sequence).LastOrDefault();

            if (last == null) continue;
            if (last.Kind != SupportMessageKind.User) continue;
            if (last.AuthorIdentity == supportCase.AuthorIdentity) continue;
            if (last.SentAt >= lastActivityBefore) continue;

            due.Add(supportCase);

            if (due.Count >= limit) break;
        }

        return Task.FromResult<SupportCase[]>([.. due]);
    }

    public Task<bool> TryCloseForInactivityAsync(string teamKey, string caseId, DateTime closedAt, SupportMessage closureMessage, CancellationToken cancellationToken = default)
    {
        var found = Require(teamKey, caseId);

        // Conditional, as the real store is: the second sweep to arrive closes nothing and says so.
        if (found.Case.Status != SupportCaseStatus.Open) return Task.FromResult(false);

        found.Messages.Add(closureMessage with { Sequence = found.Messages.Count + 1 });

        Replace(
            found.Case with
            {
                Status = SupportCaseStatus.Closed,
                ClosedAt = closedAt,
                ClosedBy = SupportCaseActors.AutoClose,
                MessageCount = found.Messages.Count
            },
            found.Messages);

        return Task.FromResult(true);
    }

    public Task ReopenCaseAsync(string teamKey, string caseId, SupportMessage reopenMessage, CancellationToken cancellationToken = default)
    {
        var found = Require(teamKey, caseId);

        found.Messages.Add(reopenMessage with { Sequence = found.Messages.Count + 1 });

        Replace(
            found.Case with
            {
                Status = SupportCaseStatus.Open,
                ClosedAt = null,
                ClosedBy = null,
                MessageCount = found.Messages.Count
            },
            found.Messages);

        return Task.CompletedTask;
    }

    public Task CloseCaseAsync(string teamKey, string caseId, DateTime closedAt, string closedBy, SupportMessage closureMessage, CancellationToken cancellationToken = default)
    {
        var found = Require(teamKey, caseId);

        found.Messages.Add(closureMessage with { Sequence = found.Messages.Count + 1 });

        Replace(
            found.Case with
            {
                Status = SupportCaseStatus.Closed,
                ClosedAt = closedAt,
                ClosedBy = closedBy,
                MessageCount = found.Messages.Count
            },
            found.Messages);

        return Task.CompletedTask;
    }

    /// <remarks>
    /// Mirrors the real adapter, including refusing to move the marker backwards and replacing the entry
    /// rather than appending. A fake that grew the list on every mark would hide exactly the defect the
    /// idempotency test exists to catch.
    /// </remarks>
    public Task MarkReadAsync(string teamKey, string caseId, string identity, int sequence, CancellationToken cancellationToken = default)
    {
        var found = Require(teamKey, caseId);

        if (_reads.TryGetValue((caseId, identity), out var current) && current >= sequence) return Task.CompletedTask;

        _reads[(caseId, identity)] = sequence;

        return Task.CompletedTask;
    }

    public Task<int> GetUnreadCountAsync(string teamKey, string identity, CancellationToken cancellationToken = default)
        => Task.FromResult(_cases
            .Where(x => x.Case.TeamKey == teamKey && x.Case.AuthorIdentity == identity)
            .Count(x => (_reads.TryGetValue((x.Case.Id, identity), out var read) ? read : 0) < x.Messages.Count));

    public Task<int> GetAwaitingSupportCountAsync(string teamKey, CancellationToken cancellationToken = default)
        => Task.FromResult(_cases
            .Where(x => x.Case.TeamKey == teamKey && x.Case.Status == SupportCaseStatus.Open)
            .Count(x => x.Messages.Count > 0
                        && x.Messages[^1].Kind == SupportMessageKind.User
                        && x.Messages[^1].AuthorIdentity == x.Case.AuthorIdentity));

    public Task<SupportCase> GetCaseByBindingAsync(SupportChannelType channelType, string externalId, CancellationToken cancellationToken = default)
        => Task.FromResult(_cases
            .Select(x => x.Case)
            .FirstOrDefault(c => (c.Bindings ?? []).Any(b => b.ChannelType == channelType && b.ExternalId == externalId)));

    public Task AddBindingAsync(string teamKey, string caseId, SupportChannelBinding binding, CancellationToken cancellationToken = default)
    {
        var found = Require(teamKey, caseId);

        var bindings = (found.Case.Bindings ?? []).Append(binding).ToArray();

        Replace(found.Case with { Bindings = bindings }, found.Messages);

        return Task.CompletedTask;
    }

    public Task SetMessageDeliveryAsync(string teamKey, string caseId, int sequence, SupportMessageDelivery delivery, CancellationToken cancellationToken = default)
    {
        var found = Require(teamKey, caseId);

        var index = found.Messages.FindIndex(x => x.Sequence == sequence);
        if (index >= 0) found.Messages[index] = found.Messages[index] with { Delivery = delivery };

        Replace(found.Case, found.Messages);

        return Task.CompletedTask;
    }

    public Task<int> DeleteCasesForTeamAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var removed = _cases.RemoveAll(x => x.Case.TeamKey == teamKey);

        return Task.FromResult(removed);
    }

    /// <summary>
    /// Pads a case out to <paramref name="messageCount"/> entries, so the full-case limit can be exercised
    /// without writing five hundred messages through the service.
    /// </summary>
    public void Stuff(string teamKey, string caseId, int messageCount)
    {
        var found = Require(teamKey, caseId);

        while (found.Messages.Count < messageCount)
        {
            found.Messages.Add(new SupportMessage
            {
                Sequence = found.Messages.Count + 1,
                Kind = SupportMessageKind.User,
                Body = "filler",
                SentAt = DateTime.UtcNow
            });
        }

        Replace(found.Case with { MessageCount = found.Messages.Count }, found.Messages);
    }

    private (SupportCase Case, List<SupportMessage> Messages)? Find(string teamKey, string caseId)
    {
        var index = _cases.FindIndex(x => x.Case.TeamKey == teamKey && x.Case.Id == caseId);

        return index < 0 ? null : _cases[index];
    }

    private (SupportCase Case, List<SupportMessage> Messages) Require(string teamKey, string caseId)
        => Find(teamKey, caseId)
           ?? throw new InvalidOperationException($"Support case {caseId} was not found in team {teamKey}.");

    private void Replace(SupportCase updated, List<SupportMessage> messages)
    {
        var index = _cases.FindIndex(x => x.Case.TeamKey == updated.TeamKey && x.Case.Id == updated.Id);

        _cases[index] = (updated, messages);
    }

    private static SupportCasePage Page(IEnumerable<SupportCase> cases)
        => new() { Items = [.. cases.OrderByDescending(x => x.CreatedAt)] };
}
