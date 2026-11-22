namespace Tharga.Team.Support.Cases;

/// <summary>
/// Which limit holds for a team that has none of its own: the default staff set, otherwise the host's option.
/// </summary>
/// <remarks>One place, so what the allowance enforces and what staff are shown as the default cannot disagree.</remarks>
internal static class AllowanceLimits
{
    public static async Task<(decimal? Limit, bool Stored)> DefaultAsync(ISupportAssistantLimitStore store, SupportAssistantAllowanceOptions options, CancellationToken cancellationToken)
    {
        var stored = store == null ? null : await store.GetAsync(StoredSupportAssistantLimit.DefaultKey, cancellationToken);
        return stored != null ? (stored.Limit, true) : (options.DefaultLimit, false);
    }
}
