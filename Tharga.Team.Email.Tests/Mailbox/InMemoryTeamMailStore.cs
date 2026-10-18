namespace Tharga.Team.Email.Tests.Mailbox;

internal sealed class InMemoryTeamMailStore : ITeamMailStore
{
    private readonly List<InboundMailItem> _items = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<InboundMailItem> All
    {
        get { lock (_lock) return [.. _items]; }
    }

    public Task<bool> AddAsync(InboundMailItem item, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_items.Any(x => x.Id == item.Id)) return Task.FromResult(false);
            _items.Add(item);
            return Task.FromResult(true);
        }
    }

    public Task<InboundMailItem> GetAsync(string teamKey, string mailId, CancellationToken cancellationToken = default)
    {
        lock (_lock) return Task.FromResult(_items.FirstOrDefault(x => x.TeamKey == teamKey && teamKey != null && x.Id == mailId));
    }

    public Task<InboundMailPage> ListAsync(string teamKey, InboundMailFilter filter, string cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var items = _items.Where(x => x.TeamKey == teamKey && teamKey != null && filter switch
            {
                InboundMailFilter.Unhandled => x.Outcome == null,
                InboundMailFilter.Handled => x.Outcome != null,
                _ => true
            }).OrderByDescending(x => x.ReceivedAt).ToArray();

            return Task.FromResult(new InboundMailPage { Items = items });
        }
    }

    public Task<int> CountUnhandledAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        lock (_lock) return Task.FromResult(_items.Count(x => x.TeamKey == teamKey && teamKey != null && x.Outcome == null));
    }

    public Task<InboundMailItem> SetOutcomeAsync(string teamKey, string mailId, string outcome, MailThreadOwner owner, string handledBy, DateTime handledAt, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(x => x.TeamKey == teamKey && teamKey != null && x.Id == mailId);
            if (index < 0) return Task.FromResult<InboundMailItem>(null);

            _items[index] = _items[index] with
            {
                Outcome = outcome,
                Owner = owner,
                HandledBy = outcome == null ? null : handledBy,
                HandledAt = outcome == null ? null : handledAt
            };

            return Task.FromResult(_items[index]);
        }
    }

    public Task<InboundMailItem> GetUnroutedAsync(string mailId, CancellationToken cancellationToken = default)
    {
        lock (_lock) return Task.FromResult(_items.FirstOrDefault(x => x.TeamKey == null && x.Id == mailId));
    }

    public Task<InboundMailPage> ListUnroutedAsync(string cursor, int pageSize, CancellationToken cancellationToken = default)
        => ListUnroutedAsync(PostmasterMailState.Held, cursor, pageSize, cancellationToken);

    public Task<InboundMailPage> ListUnroutedAsync(PostmasterMailState state, string cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        lock (_lock) return Task.FromResult(new InboundMailPage { Items = [.. _items.Where(x => InState(x, state)).OrderByDescending(x => x.ReceivedAt)] });
    }

    public Task<int> CountUnroutedAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock) return Task.FromResult(_items.Count(x => InState(x, PostmasterMailState.Held)));
    }

    public Task<InboundMailItem> RouteAsync(string mailId, string teamKey, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(x => x.TeamKey == null && x.Id == mailId);
            if (index < 0) return Task.FromResult<InboundMailItem>(null);

            _items[index] = _items[index] with { TeamKey = teamKey, Reason = MailRouteReason.Rerouted, KeyRetiresAt = null, Outcome = null, HandledAt = null, HandledBy = null };
            return Task.FromResult(_items[index]);
        }
    }

    public Task<InboundMailItem> ArchiveUnroutedAsync(string mailId, string archivedBy, DateTime archivedAt, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(x => x.Id == mailId && InState(x, PostmasterMailState.Held));
            if (index < 0) return Task.FromResult<InboundMailItem>(null);

            _items[index] = _items[index] with { Outcome = PostmasterMailOutcome.Archived, HandledAt = archivedAt, HandledBy = archivedBy };
            return Task.FromResult(_items[index]);
        }
    }

    public Task<InboundMailItem> RestoreUnroutedAsync(string mailId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(x => x.Id == mailId && InState(x, PostmasterMailState.Archived));
            if (index < 0) return Task.FromResult<InboundMailItem>(null);

            _items[index] = _items[index] with { Outcome = null, HandledAt = null, HandledBy = null };
            return Task.FromResult(_items[index]);
        }
    }

    private static bool InState(InboundMailItem item, PostmasterMailState state)
        => item.TeamKey == null && (state == PostmasterMailState.Archived ? item.Outcome == PostmasterMailOutcome.Archived : item.Outcome == null);

    public Task<string[]> DeleteUnroutedReceivedBeforeAsync(DateTime receivedBefore, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var removed = _items.Where(x => x.TeamKey == null && x.ReceivedAt < receivedBefore).Select(x => x.Id).ToArray();
            _items.RemoveAll(x => removed.Contains(x.Id));
            return Task.FromResult(removed);
        }
    }

    public Task<string[]> DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var removed = _items.Where(x => x.TeamKey == teamKey).Select(x => x.Id).ToArray();
            _items.RemoveAll(x => removed.Contains(x.Id));
            return Task.FromResult(removed);
        }
    }
}
