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

        var items = (found?.Messages ?? [])
            .Where(x => x.Sequence > after)
            .OrderBy(x => x.Sequence)
            .Take(pageSize)
            .ToArray();

        return Task.FromResult(new SupportMessagePage { Items = items });
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

    public Task<int> DeleteCasesForTeamAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var removed = _cases.RemoveAll(x => x.Case.TeamKey == teamKey);

        return Task.FromResult(removed);
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
