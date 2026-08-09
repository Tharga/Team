using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Features.Audit;

public partial class AuditLogView : ComponentBase
{
    [Inject] private IServiceProvider ServiceProvider { get; init; }
    [Inject] private ITeamDirectoryService TeamDirectoryService { get; init; }
    [Inject] private NotificationService NotificationService { get; init; }
    [Inject] private IJSRuntime JS { get; init; }
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; init; }
    [Inject] private IThargaTextProvider TextProvider { get; init; }

    /// <summary>Resolved once in <see cref="OnInitializedAsync"/>; read synchronously in markup. See <see cref="TextSet"/>.</summary>
    private TextSet _text = TextSet.Empty;

    [Parameter] public string TeamKey { get; set; }
    [Parameter] public AuditCallerType? RestrictCallerType { get; set; }

    /// <summary>
    /// Optional fixed filter dimensions. When set, the matching top-bar controls are hidden — the caller
    /// cannot change them — and the underlying query is forced to the pinned values regardless of
    /// in-component state.
    /// </summary>
    [Parameter] public AuditPinnedFilter PinnedFilter { get; set; }

    /// <summary>
    /// How long entries are kept, described for the reader. Null when retention is unlimited, so that an
    /// empty result reads as "nothing happened" rather than "it aged out".
    /// </summary>
    private string RetentionText
        => AuditRetentionText.Describe(
            ServiceProvider.GetService<Microsoft.Extensions.Options.IOptions<AuditOptions>>()?.Value.RetentionDays);

    private const int ChartQueryLimit = 5000;

    private bool _hasAccess;

    /// <summary>
    /// Whether <see cref="_hasAccess"/> has been computed yet. Without this the view cannot tell "denied"
    /// from "not asked yet", and renders the former on every first frame.
    /// </summary>
    private bool _accessResolved;
    private CompositeAuditLogger _auditLogger;
    private IAuditReadService _auditReadService;
    private IAuditOversightService _auditOversightService;
    private bool _auditLoggerMissing;
    private bool? _mongoAvailable;
    private IReadOnlyList<AuditEntry> _entries = Array.Empty<AuditEntry>();
    private IReadOnlyList<AuditEntry> _chartEntries = Array.Empty<AuditEntry>();
    private int _totalCount;
    private int _pageSize = 8;
    private RadzenDataGrid<AuditEntry> _grid;
    private bool _initialLoadDone;

    // Caller name resolution
    internal Dictionary<string, string> _callerNameCache = new(StringComparer.OrdinalIgnoreCase);

    // Top-bar filters
    private string _datePeriod = AuditPeriod.Today;
    private IEnumerable<string> _filterTeams = Enumerable.Empty<string>();
    private IEnumerable<string> _filterSources = Enumerable.Empty<string>();
    private IEnumerable<string> _filterFeatures = Enumerable.Empty<string>();
    private IEnumerable<string> _filterActions = Enumerable.Empty<string>();
    private IEnumerable<AuditEventType> _filterEventTypes = Enumerable.Empty<AuditEventType>();
    private IEnumerable<bool> _filterSuccess = Enumerable.Empty<bool>();
    private string _timeGrouping = "hourly";

    // Dynamic filter options
    private List<TeamInfo> _teams = new();
    private List<string> _sources = new();
    private List<string> _features = new();
    private List<string> _actions = new();
    internal static readonly int[] PageSizeOptionsValues = [8, 16, 32, 64];
    internal static readonly AuditEventType[] EventTypeOptions = Enum.GetValues<AuditEventType>();

    /// <summary>
    /// Routes a read to the service that can authorize it: the team-bound one when a team is named, the
    /// oversight one otherwise. Both carry <c>[RequireScope]</c>, so this method decides nothing.
    /// </summary>
    /// <remarks>
    /// <b><see cref="TeamKey"/> counts here, not only at the query sites</b> (Tharga/Team#175). It used to be
    /// read when building a query but not when choosing the service, so the access probe — which passes no
    /// team of its own — fell through to the oversight service and refused a caller holding that team's
    /// <c>audit:read</c>. A component that filters to one team on the caller's behalf is already asserting the
    /// caller may see that team, so the parameter belongs in this decision.
    /// <para>
    /// Pinning still wins: a pinned filter is the stronger statement, and <c>ApplyPinnedFilter</c> has already
    /// forced it onto <c>query.TeamKey</c> by the time a grid query arrives here.
    /// </para>
    /// </remarks>
    private async Task<AuditQueryResult> QueryAsync(AuditQuery query)
    {
        var teamKey = AuditTeamScope.Resolve(query.TeamKey, PinnedFilter?.TeamKey, TeamKey);

        if (!string.IsNullOrEmpty(teamKey) && _auditReadService != null)
        {
            try
            {
                return await _auditReadService.QueryAsync(teamKey, query);
            }
            catch (UnauthorizedAccessException) when (_auditOversightService != null)
            {
                // No grant on that team; a system grant may still cover it, narrowed by the filter.
            }
        }

        if (_auditOversightService == null) throw new UnauthorizedAccessException("No audit service is registered.");

        return await _auditOversightService.QueryAllAsync(query with { TeamKey = teamKey });
    }

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        // Before the not-configured branch below, which returns early: that alert is user-facing too, and
        // resolving after it would leave the one message shown to a misconfigured host in English.
        _text = await TextProvider.ResolveAsync(AuditLogViewText.All);

        _auditReadService = ServiceProvider.GetService<IAuditReadService>();
        _auditOversightService = ServiceProvider.GetService<IAuditOversightService>();
        _auditLogger = ServiceProvider.GetService<CompositeAuditLogger>();

        if (_auditLogger == null || (_auditReadService == null && _auditOversightService == null))
        {
            _auditLoggerMissing = true;
            _accessResolved = true;
            return;
        }

        // Access is decided by asking the service, not by restating its rule here. The view used to call
        // AuditAccess.CanRead -- correct at the time, but a second place holding the rule, and the MCP
        // surface proved how that ends: it grew a third rule and nothing noticed. Whether to *show* the
        // view is still a UI decision, so it is answered before rendering rather than by letting a
        // control appear and then throw.
        try
        {
            await QueryAsync(new AuditQuery { Take = 1 });
            _hasAccess = true;
            _mongoAvailable = true;
        }
        catch (UnauthorizedAccessException)
        {
            _hasAccess = false;
        }
        catch
        {
            // Reached the gate, so access is fine; the store is not.
            _hasAccess = true;
            _mongoAvailable = false;
        }

        _accessResolved = true;
        if (!_hasAccess) return;

        if (_mongoAvailable != true) return;

        if (string.IsNullOrEmpty(TeamKey))
        {
            await foreach (var team in TeamDirectoryService.GetTeamsAsync())
            {
                _teams.Add(new TeamInfo(team.Key, team.Name));
            }
        }

        // Filter options describe what the reader can actually reach, so they are drawn from inside the
        // pinned scope. Built from the unpinned log they described the whole system: a system API key
        // offering a Team filter it can never match, a team key offering the one team it is already
        // bound to, a user offering features they have never touched.
        var optionQuery = ApplyPinnedFilter(new AuditQuery
        {
            TeamKey = TeamKey,
            From = DateTime.UtcNow.AddDays(-30),
            Take = ChartQueryLimit
        });

        var recentResult = await QueryAsync(optionQuery);
        _features = recentResult.Items.Where(e => e.Feature != null).Select(e => e.Feature).Distinct().OrderBy(f => f).ToList();
        _actions = recentResult.Items.Where(e => e.Action != null).Select(e => e.Action).Distinct().OrderBy(a => a).ToList();
        _sources = recentResult.Items.Select(e => e.CallerSource.ToString()).Distinct().OrderBy(s => s).ToList();

        // Narrow the team list the same way, but only for a pinned dialog. The unpinned page is a
        // browsing surface where picking a team with no recent activity is a legitimate thing to do.
        if (PinnedFilter != null)
        {
            var observed = recentResult.Items
                .Where(e => e.TeamKey != null)
                .Select(e => e.TeamKey)
                .ToHashSet(StringComparer.Ordinal);

            _teams = _teams.Where(t => observed.Contains(t.Key)).ToList();
        }

        await BuildCallerNameCacheAsync();
    }

    private async Task BuildCallerNameCacheAsync()
    {
        var userService = ServiceProvider.GetService<IUserService>();
        if (userService != null)
        {
            await foreach (var user in userService.GetAsync())
            {
                if (!string.IsNullOrEmpty(user.Identity))
                    _callerNameCache.TryAdd(user.Identity, user.EMail ?? user.Identity);
                if (!string.IsNullOrEmpty(user.EMail))
                    _callerNameCache.TryAdd(user.EMail, user.EMail);
            }
        }
    }

    internal string GetCallerDisplayName(AuditEntry entry)
    {
        if (string.IsNullOrEmpty(entry.CallerIdentity)) return "";
        return _callerNameCache.TryGetValue(entry.CallerIdentity, out var name) ? name : entry.CallerIdentity;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_mongoAvailable == true && _grid != null && !_initialLoadDone)
        {
            _initialLoadDone = true;
            await _grid.Reload();
        }
    }

    private async Task OnFilterChanged()
    {
        if (_grid != null)
        {
            _grid.Reset();
            await _grid.FirstPage(true);
        }
    }

    private async Task OnLoadData(LoadDataArgs args)
    {
        try
        {
            var query = BuildQuery(args.Skip ?? 0, args.Top ?? _pageSize, args.OrderBy, args.Filters);
            var result = await QueryAsync(query);
            _entries = result.Items;
            _totalCount = result.TotalCount;

            if (!_initialLoadDone)
            {
                _initialLoadDone = true;
                _chartEntries = Array.Empty<AuditEntry>();
            }
        }
        catch (Exception ex)
        {
            NotificationService.Notify(NotificationSeverity.Error, _text[AuditLogViewText.NotifyQueryFailed], ex.Message);
        }
    }

    private async Task OnTabChange(int tabIndex)
    {
        if (tabIndex > 0 && !_chartEntries.Any())
        {
            await LoadChartDataAsync();
        }
    }

    private AuditQuery BuildQuery(int skip = 0, int take = 0, string orderBy = null, IEnumerable<FilterDescriptor> filters = null)
    {
        // Extract in-grid filter values
        string callerFilter = null;
        string methodFilter = null;
        if (filters != null)
        {
            foreach (var f in filters)
            {
                if (f.Property == nameof(AuditEntry.CallerIdentity) && f.FilterValue is string cv && !string.IsNullOrWhiteSpace(cv))
                    callerFilter = cv;
                else if (f.Property == nameof(AuditEntry.MethodName) && f.FilterValue is string mv && !string.IsNullOrWhiteSpace(mv))
                    methodFilter = mv;
            }
        }

        var (from, to) = GetDateRange();
        var teamKeys = _filterTeams?.ToArray();
        var features = _filterFeatures?.ToArray();
        var actions = _filterActions?.ToArray();
        var eventTypes = _filterEventTypes?.ToArray();
        var sources = _filterSources?.ToArray();

        // Map source strings to enum for CallerSource filter
        AuditCallerSource? callerSource = null;
        AuditCallerSource[] callerSources = null;
        if (sources is { Length: > 0 })
        {
            callerSources = sources
                .Select(s => Enum.TryParse<AuditCallerSource>(s, out var v) ? v : (AuditCallerSource?)null)
                .Where(v => v != null)
                .Select(v => v.Value)
                .ToArray();
            if (callerSources.Length == 1)
            {
                callerSource = callerSources[0];
                callerSources = null;
            }
            else if (callerSources.Length == 0)
            {
                callerSources = null;
            }
        }

        // Parse sort from Radzen's OrderBy string, e.g. "Timestamp desc"
        string sortField = null;
        var sortDesc = true;
        if (!string.IsNullOrEmpty(orderBy))
        {
            var parts = orderBy.Split(' ', 2);
            sortField = parts[0];
            sortDesc = parts.Length < 2 || parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
        }

        // Success filter from top bar
        var successValues = _filterSuccess?.ToArray();
        bool? success = null;
        if (successValues is { Length: 1 })
            success = successValues[0];

        var query = new AuditQuery
        {
            TeamKey = TeamKey,
            TeamKeys = teamKeys is { Length: > 0 } ? teamKeys : null,
            Features = features is { Length: > 0 } ? features : null,
            Actions = actions is { Length: > 0 } ? actions : null,
            EventTypes = eventTypes is { Length: > 0 } ? eventTypes : null,
            CallerSource = callerSource,
            CallerType = RestrictCallerType,
            CallerIdentity = callerFilter,
            MethodName = methodFilter,
            Success = success,
            From = from,
            To = to,
            Skip = skip,
            Take = take > 0 ? take : _pageSize,
            SortField = sortField,
            SortDescending = sortDesc
        };

        return ApplyPinnedFilter(query);
    }

    /// <summary>
    /// Forces the pinned dimensions onto a query. Pinned filters always win — they override any local
    /// <c>_filterX</c> state — so both the grid query and the filter-option query go through here and
    /// cannot disagree about what the dialog is scoped to.
    /// </summary>
    private AuditQuery ApplyPinnedFilter(AuditQuery query)
    {
        if (PinnedFilter == null) return query;

        query = query with
        {
            CallerKeyId = PinnedFilter.CallerKeyId ?? query.CallerKeyId,
            CallerType = PinnedFilter.CallerType ?? query.CallerType,
            TeamKey = PinnedFilter.TeamKey ?? query.TeamKey,
            CallerIdentity = PinnedFilter.CallerIdentity ?? query.CallerIdentity,
            CallerUserIdentity = PinnedFilter.CallerUserIdentity ?? query.CallerUserIdentity,
            Feature = PinnedFilter.Feature ?? query.Feature,
            Action = PinnedFilter.Action ?? query.Action,
        };

        // When the pinned single-value Feature/Action is set, suppress the multi-value collections
        // so the In() filter doesn't widen results past the pinned value.
        if (PinnedFilter.Feature != null) query = query with { Features = null };
        if (PinnedFilter.Action != null) query = query with { Actions = null };
        if (PinnedFilter.TeamKey != null) query = query with { TeamKeys = null };

        return query;
    }

    private (DateTime? from, DateTime? to) GetDateRange()
    {
        return (AuditPeriod.ResolveFrom(_datePeriod, DateTime.UtcNow, DateTime.Today), null);
    }


    private async Task LoadChartDataAsync()
    {
        try
        {
            var result = await QueryAsync(BuildQuery(take: ChartQueryLimit));
            _chartEntries = result.Items;
        }
        catch (Exception ex)
        {
            NotificationService.Notify(NotificationSeverity.Error, _text[AuditLogViewText.NotifyChartDataFailed], ex.Message);
        }
    }

    // Chart helpers
    public class ChartItem { public string Label { get; set; } public int Count { get; set; } }
    public class ChartValue { public string Period { get; set; } public double Value { get; set; } }
    public class ChartCount { public string Period { get; set; } public int Count { get; set; } }

    private List<ChartCount> GetCallsOverTime()
    {
        var grouped = _timeGrouping == "hourly"
            ? _chartEntries.GroupBy(e => e.Timestamp.ToLocalTime().ToString("MM-dd HH:00"))
            : _chartEntries.GroupBy(e => e.Timestamp.ToLocalTime().ToString("yyyy-MM-dd"));
        return grouped.OrderBy(g => g.Key).Select(g => new ChartCount { Period = g.Key, Count = g.Count() }).ToList();
    }

    private List<ChartItem> GetSuccessFailure() => new()
    {
        new ChartItem { Label = "Success", Count = _chartEntries.Count(e => e.Success) },
        new ChartItem { Label = "Failure", Count = _chartEntries.Count(e => !e.Success) }
    };

    private List<ChartItem> GetByFeature() =>
        _chartEntries.Where(e => e.Feature != null).GroupBy(e => e.Feature)
            .Select(g => new ChartItem { Label = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(10).ToList();

    private List<ChartItem> GetTopCallers() =>
        _chartEntries.Where(e => e.CallerIdentity != null).GroupBy(e => GetCallerDisplayName(e))
            .Select(g => new ChartItem { Label = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(10).ToList();

    private List<ChartValue> GetResponseTimeOverTime()
    {
        var grouped = _timeGrouping == "hourly"
            ? _chartEntries.GroupBy(e => e.Timestamp.ToLocalTime().ToString("MM-dd HH:00"))
            : _chartEntries.GroupBy(e => e.Timestamp.ToLocalTime().ToString("yyyy-MM-dd"));
        return grouped.OrderBy(g => g.Key).Select(g => new ChartValue { Period = g.Key, Value = g.Average(e => e.DurationMs) }).ToList();
    }

    private List<ChartValue> GetResponseTimeByFeature() =>
        _chartEntries.Where(e => e.Feature != null).GroupBy(e => e.Feature)
            .Select(g => new ChartValue { Period = g.Key, Value = g.Average(e => e.DurationMs) }).OrderByDescending(x => x.Value).Take(10).ToList();

    private List<AuditEntry> GetSlowest() =>
        _chartEntries.OrderByDescending(e => e.DurationMs).Take(10).ToList();

    private async Task ExportAsync(string format)
    {
        try
        {
            var result = await QueryAsync(BuildQuery(take: 100_000));
            var exportEntries = result.Items;

            if (!exportEntries.Any())
            {
                NotificationService.Notify(NotificationSeverity.Warning, _text[AuditLogViewText.NotifyNoDataToExport]);
                return;
            }

            string content;
            string mimeType;
            string fileName;

            if (format == "json")
            {
                content = System.Text.Json.JsonSerializer.Serialize(exportEntries, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                mimeType = "application/json";
                fileName = $"audit-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                var includeTeam = string.IsNullOrEmpty(TeamKey);
                // Subject is the exact-matchable identifier; CallerID is the display string it was
                // resolved from. Both, because a reader correlating rows to one user needs the former and
                // a reader skimming needs the latter.
                sb.AppendLine(includeTeam
                    ? "Timestamp,Team,Caller,CallerID,Subject,Source,Feature,Action,Method,Duration,Success,EventType,Scope,ScopeResult,ErrorMessage,Metadata"
                    : "Timestamp,Caller,CallerID,Subject,Source,Feature,Action,Method,Duration,Success,EventType,Scope,ScopeResult,ErrorMessage,Metadata");
                foreach (var e in exportEntries)
                {
                    var team = includeTeam ? $"{Escape(e.TeamKey)}," : "";
                    var callerName = Escape(GetCallerDisplayName(e));
                    sb.AppendLine($"{e.Timestamp:O},{team}{callerName},{Escape(e.CallerIdentity)},{Escape(e.CallerUserIdentity)},{e.CallerSource},{Escape(e.Feature)},{Escape(e.Action)},{Escape(e.MethodName)},{e.DurationMs},{e.Success},{e.EventType},{Escape(e.ScopeChecked)},{e.ScopeResult},{Escape(e.ErrorMessage)},{Escape(FormatMetadata(e.Metadata))}");
                }
                content = sb.ToString();
                mimeType = "text/csv";
                fileName = $"audit-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            var base64 = Convert.ToBase64String(bytes);
            await JS.InvokeVoidAsync("eval", $"{{const a=document.createElement('a');a.href='data:{mimeType};base64,{base64}';a.download='{fileName}';a.click();}}");
        }
        catch (Exception ex)
        {
            NotificationService.Notify(NotificationSeverity.Error, _text[AuditLogViewText.NotifyExportFailed], ex.Message);
        }
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    // Metadata is arbitrary key/values; a single JSON-encoded column keeps the CSV rectangular and
    // round-trips cleanly. Escape() then quotes it, since JSON contains commas and quotes.
    internal static string FormatMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata is not { Count: > 0 }) return "";
        var ordered = metadata.OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Value);
        return System.Text.Json.JsonSerializer.Serialize(ordered);
    }

    // Audit calls are not HTTP, so there is no numeric status; the failure's EventType is the closest
    // "response code" — with a plain exception (Success=false on an otherwise normal event) surfaced as "Error".
    internal static string BuildFailureCode(AuditEntry entry)
    {
        if (entry is null || entry.Success) return null;
        return entry.EventType switch
        {
            AuditEventType.ScopeDenial => "ScopeDenial",
            AuditEventType.AccessLevelDenial => "AccessLevelDenial",
            AuditEventType.AuthFailure => "AuthFailure",
            AuditEventType.RateLimit => "RateLimit",
            _ => "Error"
        };
    }

    /// <summary>
    /// Multi-line hover-tooltip detail for a failed entry in the "OK" column: the failure code, the
    /// authorization scope and its result when present, and the reason text. Null for successful entries.
    /// </summary>
    internal static string BuildFailureDetail(AuditEntry entry, TextSet text = null)
    {
        if (entry is null || entry.Success) return null;
        text ??= TextSet.Empty;
        var lines = new List<string> { BuildFailureCode(entry) };
        if (!string.IsNullOrEmpty(entry.ScopeChecked))
            lines.Add(text.Format(AuditLogViewText.FailureScope, entry.ScopeChecked, entry.ScopeResult));
        if (!string.IsNullOrEmpty(entry.ErrorMessage))
            lines.Add(text.Format(AuditLogViewText.FailureReason, entry.ErrorMessage));
        return string.Join("\n", lines);
    }

    private string GetTeamName(string teamKey)
    {
        if (string.IsNullOrEmpty(teamKey)) return "";
        var team = _teams.Find(t => t.Key == teamKey);
        return team?.Name ?? teamKey;
    }

    private class TeamInfo
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public TeamInfo(string key, string name) { Key = key; Name = name; }
    }
}
