using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// Dispatches audit entries to configured loggers based on AuditOptions filters, applying any
/// registered <see cref="IAuditEnricher"/>s first.
/// </summary>
public class CompositeAuditLogger : IAuditLogger
{
    private readonly IAuditLogger[] _loggers;
    private readonly AuditOptions _options;
    private readonly IAuditLogger _queryLogger;
    private readonly IAuditEnricher[] _enrichers;
    private readonly ILogger<CompositeAuditLogger> _logger;

    public CompositeAuditLogger(IEnumerable<IAuditLogger> loggers, IOptions<AuditOptions> options, IEnumerable<IAuditEnricher> enrichers = null, ILogger<CompositeAuditLogger> logger = null)
    {
        _loggers = loggers.Where(l => l != this).ToArray();
        _options = options.Value;
        _queryLogger = _loggers.FirstOrDefault(l => l is MongoDbAuditLogger);
        _enrichers = enrichers?.ToArray() ?? [];
        _logger = logger;
    }

    public void Log(AuditEntry entry)
    {
        if (!ShouldLog(entry)) return;

        entry = Enrich(entry);

        foreach (var logger in _loggers)
        {
            logger.Log(entry);
        }
    }

    private AuditEntry Enrich(AuditEntry entry)
    {
        if (_enrichers.Length == 0) return entry;

        // Start from the toolkit's own metadata so it wins; then let each enricher fill only gaps, in
        // registration order, so the first writer of a key wins over later ones.
        var merged = entry.Metadata != null
            ? new Dictionary<string, string>(entry.Metadata)
            : new Dictionary<string, string>();
        var before = merged.Count;

        foreach (var enricher in _enrichers)
        {
            var additions = new Dictionary<string, string>();
            try
            {
                enricher.Enrich(entry, additions);
            }
            catch (Exception ex)
            {
                // An audit sink must never take down the operation it records.
                _logger?.LogWarning(ex, "Audit enricher {Enricher} threw; skipping its contribution.", enricher.GetType().Name);
                continue;
            }

            foreach (var pair in additions)
            {
                if (!merged.ContainsKey(pair.Key)) merged[pair.Key] = pair.Value;
            }
        }

        return merged.Count == before ? entry : entry with { Metadata = merged };
    }

    /// <remarks>
    /// Virtual so the query seam can be observed in a test. The composite only queries a
    /// <see cref="MongoDbAuditLogger"/>, which is a concrete class with non-virtual members, so without
    /// this there is no way to assert what query a caller actually issued — and the narrowing that keeps
    /// a caller inside the team they were authorized for is exactly a property of the query.
    /// </remarks>
    public virtual Task<AuditQueryResult> QueryAsync(AuditQuery query)
    {
        return _queryLogger?.QueryAsync(query) ?? Task.FromResult(new AuditQueryResult());
    }

    private bool ShouldLog(AuditEntry entry)
    {
        // Check caller filter
        var callerFlag = entry.CallerSource switch
        {
            AuditCallerSource.Api => AuditCallerFilter.Api,
            AuditCallerSource.Web => AuditCallerFilter.Web,
            _ => AuditCallerFilter.Api | AuditCallerFilter.Web
        };
        if ((_options.CallerFilter & callerFlag) == 0) return false;

        // Check event filter
        var eventFlag = entry.EventType switch
        {
            AuditEventType.ServiceCall => AuditEventFilter.ServiceCalls,
            AuditEventType.AuthSuccess or AuditEventType.AuthFailure => AuditEventFilter.AuthEvents,
            AuditEventType.ScopeDenial or AuditEventType.AccessLevelDenial => AuditEventFilter.Denials,
            AuditEventType.DataChange => AuditEventFilter.DataChanges,
            AuditEventType.RateLimit => AuditEventFilter.RateLimits,
            _ => AuditEventFilter.None
        };
        if ((_options.EventFilter & eventFlag) == 0) return false;

        // Check excluded actions
        if (entry.Action != null && _options.ExcludedActions.Contains(entry.Action, StringComparer.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
