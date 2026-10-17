namespace Tharga.Team.Email.Tests.Mailbox;

internal sealed class InMemoryTeamMailKeyStore : ITeamMailKeyStore
{
    private readonly List<TeamMailKeyRecord> _keys = [];
    private readonly Lock _lock = new();

    public bool RefuseNextIssue { get; set; }

    public IReadOnlyList<TeamMailKeyRecord> All
    {
        get { lock (_lock) return [.. _keys]; }
    }

    public void Add(TeamMailKeyRecord record)
    {
        lock (_lock) _keys.Add(record);
    }

    public Task<TeamMailKeyRecord[]> GetForTeamAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        lock (_lock) return Task.FromResult(_keys.Where(x => x.TeamKey == teamKey).ToArray());
    }

    public Task<TeamMailKeyRecord> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_lock) return Task.FromResult(_keys.FirstOrDefault(x => x.Key == key));
    }

    public Task<bool> IssueAsync(TeamMailKeyRecord primary, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (RefuseNextIssue)
            {
                RefuseNextIssue = false;
                return Task.FromResult(false);
            }

            if (_keys.Any(x => x.TeamKey == primary.TeamKey && x.Role == MailKeyRole.Primary)) return Task.FromResult(false);

            _keys.Add(primary);
            return Task.FromResult(true);
        }
    }

    public Task RotateAsync(string teamKey, TeamMailKeyRecord newPrimary, DateTime retiresAt, DateTime now, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            for (var i = 0; i < _keys.Count; i++)
            {
                var key = _keys[i];
                if (key.TeamKey != teamKey) continue;

                if (key.Role == MailKeyRole.Secondary && !key.HasRetired(now)) _keys[i] = key with { RetiresAt = now };
                else if (key.Role == MailKeyRole.Primary) _keys[i] = key with { Role = MailKeyRole.Secondary, RetiresAt = retiresAt };
            }

            _keys.Add(newPrimary);
        }

        return Task.CompletedTask;
    }

    public Task DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        lock (_lock) _keys.RemoveAll(x => x.TeamKey == teamKey);
        return Task.CompletedTask;
    }
}
