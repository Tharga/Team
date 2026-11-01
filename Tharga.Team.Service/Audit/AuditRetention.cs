namespace Tharga.Team.Service.Audit;

/// <summary>
/// Resolves the MongoDB TTL expiry for audit entries from <see cref="AuditOptions.RetentionDays"/>.
/// </summary>
internal static class AuditRetention
{
    /// <summary>
    /// Upper bound for a finite retention. Values above this (and null / non-positive) are treated as
    /// "keep forever" — both as the intended meaning and to avoid <see cref="TimeSpan.FromDays"/> overflow
    /// (~10.7M days). ~10,000 years is comfortably below that ceiling.
    /// </summary>
    public const int MaxRetentionDays = 3_650_000;

    /// <summary>
    /// The TTL <c>ExpireAfter</c> for the given retention, or <c>null</c> when retention is disabled
    /// (<paramref name="retentionDays"/> is null, &lt;= 0, or above <see cref="MaxRetentionDays"/>) — in
    /// which case no TTL index should be created and entries are kept indefinitely.
    /// </summary>
    public static TimeSpan? GetExpireAfter(int? retentionDays)
        => retentionDays is > 0 and <= MaxRetentionDays ? TimeSpan.FromDays(retentionDays.Value) : null;

    /// <summary>
    /// The retention in days the options ask for. A host that registered no options gets the default of
    /// <see cref="AuditOptions"/>; one that set <see cref="AuditOptions.RetentionDays"/> to <c>null</c> keeps
    /// <c>null</c>, which means keep forever.
    /// </summary>
    public static int? ResolveDays(Microsoft.Extensions.Options.IOptions<AuditOptions> options)
        => (options?.Value ?? new AuditOptions()).RetentionDays;
}
