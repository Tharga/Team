namespace Tharga.Team.Email.Users;

/// <summary>How often each category was sent over the past year, or over the shorter period the outbox keeps, from one count on the outbox kept for a few minutes.</summary>
/// <remarks>
/// <b>Read-only statistics, cached per instance.</b> Two instances may show figures a few minutes apart, which nobody
/// can tell from how often a category is sent. An outbox that cannot count gives no figures rather than an error.
/// </remarks>
internal sealed class UserMailFrequencies(IUserMailOutbox outbox, UserMailCategoryRegistry categories, UserMailFrequencyCache cache, TimeProvider timeProvider)
{
    /// <summary>The longest period counted.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromDays(365);

    /// <summary>How long a count is kept before it is made again.</summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    /// <summary>Every registered category's frequency, in the registry's order; empty when the outbox cannot count.</summary>
    public async Task<IReadOnlyList<UserMailCategoryFrequency>> GetAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (cache.TryGet(now, out var cached)) return cached;

        var since = now - (outbox.Retention is { } retention && retention > TimeSpan.Zero && retention < Period ? retention : Period);

        IReadOnlyList<UserMailCategorySent> counted;
        try
        {
            counted = await outbox.CountSentByCategoryAsync(since, cancellationToken) ?? [];
        }
        catch (NotSupportedException)
        {
            return [];
        }

        var byCategory = counted.Where(x => x?.Category != null).GroupBy(x => x.Category, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        IReadOnlyList<UserMailCategoryFrequency> frequencies =
        [
            .. categories.All.Select(x => new UserMailCategoryFrequency
            {
                Category = x.Key,
                Sent = byCategory.TryGetValue(x.Key, out var sent) ? sent.Sent : 0,
                Recipients = byCategory.TryGetValue(x.Key, out var reached) ? reached.Recipients : 0,
                Since = since,
                Until = now
            })
        ];

        cache.Set(frequencies, now + CacheDuration);
        return frequencies;
    }

    /// <summary>One category's frequency, or null when the outbox cannot count or there is no such category.</summary>
    public async Task<UserMailCategoryFrequency> GetAsync(string categoryKey, CancellationToken cancellationToken)
        => (await GetAsync(cancellationToken)).FirstOrDefault(x => x.Category == categoryKey);
}

/// <summary>The last count of <see cref="UserMailFrequencies"/> on this instance, until it expires.</summary>
internal sealed class UserMailFrequencyCache
{
    private readonly Lock _gate = new();
    private IReadOnlyList<UserMailCategoryFrequency> _value;
    private DateTime _until;

    public bool TryGet(DateTime now, out IReadOnlyList<UserMailCategoryFrequency> value)
    {
        lock (_gate)
        {
            value = _value != null && now < _until ? _value : null;
            return value != null;
        }
    }

    public void Set(IReadOnlyList<UserMailCategoryFrequency> value, DateTime until)
    {
        lock (_gate)
        {
            _value = value;
            _until = until;
        }
    }
}
