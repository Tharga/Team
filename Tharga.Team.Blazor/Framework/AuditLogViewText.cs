namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>AuditLogView</c> — filters, grid, charts and notifications.</summary>
/// <remarks>
/// <b>Several keys are reused across contexts that render the same word.</b> <see cref="Team"/> labels both
/// the filter and the grid column, and translating them apart would be a distinction without a difference in
/// every language checked. Where a word is genuinely doing two jobs it gets two keys instead — see
/// <see cref="All"/>, which is the filter value "all of them", not the tab strip.
/// <para>
/// <b>What is deliberately not here: the CSV and JSON export headers.</b> They are an interchange format
/// whose column names a downstream import parses by name. Translating them would silently break every
/// consumer's import the first time someone switched language, so they stay literal in
/// <c>AuditLogView.razor.cs</c> and the scan excludes comma-separated field lists as data rather than
/// display text.
/// </para>
/// </remarks>
public static class AuditLogViewText
{
    public static readonly TextKey NotConfigured = new("team.auditLogView.notConfigured", "Audit logging is not configured. Call {0} in Program.cs to enable this view.");
    public static readonly TextKey AccessDenied = new("team.auditLogView.accessDenied", "Access denied.");
    public static readonly TextKey MongoRequired = new("team.auditLogView.mongoRequired", "Enable MongoDB audit storage to use this view.");

    public static readonly TextKey TabLog = new("team.auditLogView.tabLog", "Log");
    public static readonly TextKey TabUsage = new("team.auditLogView.tabUsage", "Usage");
    public static readonly TextKey TabPerformance = new("team.auditLogView.tabPerformance", "Performance");

    public static readonly TextKey Period = new("team.auditLogView.period", "Period");
    public static readonly TextKey PeriodToday = new("team.auditLogView.periodToday", "Today");
    public static readonly TextKey PeriodSevenDays = new("team.auditLogView.periodSevenDays", "7 days");
    public static readonly TextKey PeriodThirtyDays = new("team.auditLogView.periodThirtyDays", "30 days");
    public static readonly TextKey PeriodNinetyDays = new("team.auditLogView.periodNinetyDays", "90 days");

    /// <summary>The unfiltered value of a filter — "no restriction", not "everything on screen".</summary>
    public static readonly TextKey FilterAll = new("team.auditLogView.filterAll", "All");

    public static readonly TextKey Team = new("team.auditLogView.team", "Team");
    public static readonly TextKey Source = new("team.auditLogView.source", "Source");
    public static readonly TextKey ScopeFeature = new("team.auditLogView.scopeFeature", "Scope feature");
    public static readonly TextKey ScopeAction = new("team.auditLogView.scopeAction", "Scope action");
    public static readonly TextKey Event = new("team.auditLogView.event", "Event");
    public static readonly TextKey Result = new("team.auditLogView.result", "Result");
    public static readonly TextKey Success = new("team.auditLogView.success", "Success");
    public static readonly TextKey Failure = new("team.auditLogView.failure", "Failure");

    public static readonly TextKey Export = new("team.auditLogView.export", "Export");
    public static readonly TextKey ExportCsv = new("team.auditLogView.exportCsv", "CSV");
    public static readonly TextKey ExportJson = new("team.auditLogView.exportJson", "JSON");

    /// <summary>Radzen's paging summary. The three placeholders are first row, last row, total.</summary>
    public static readonly TextKey PagingSummary = new("team.auditLogView.pagingSummary", "Showing {0}-{1} of {2}");

    public static readonly TextKey ColumnTime = new("team.auditLogView.columnTime", "Time");
    public static readonly TextKey ColumnCaller = new("team.auditLogView.columnCaller", "Caller");
    public static readonly TextKey ColumnScope = new("team.auditLogView.columnScope", "Scope");
    public static readonly TextKey ColumnMethod = new("team.auditLogView.columnMethod", "Method");
    public static readonly TextKey ColumnDuration = new("team.auditLogView.columnDuration", "Duration");
    public static readonly TextKey ColumnFeature = new("team.auditLogView.columnFeature", "Feature");
    public static readonly TextKey ColumnAction = new("team.auditLogView.columnAction", "Action");
    public static readonly TextKey ColumnDurationMs = new("team.auditLogView.columnDurationMs", "Duration (ms)");

    public static readonly TextKey DetailName = new("team.auditLogView.detailName", "Detail");
    public static readonly TextKey DetailValue = new("team.auditLogView.detailValue", "Value");
    public static readonly TextKey DetailEmpty = new("team.auditLogView.detailEmpty", "(empty)");
    public static readonly TextKey NoAdditionalDetails = new("team.auditLogView.noAdditionalDetails", "No additional details.");

    /// <summary>Placeholders: the entry count, then the retention sentence (which may be empty).</summary>
    public static readonly TextKey TotalEntries = new("team.auditLogView.totalEntries", "{0} total entries. {1}");

    public static readonly TextKey ChartCallsOverTime = new("team.auditLogView.chartCallsOverTime", "Calls over time");
    public static readonly TextKey GroupingHourly = new("team.auditLogView.groupingHourly", "Hourly");
    public static readonly TextKey GroupingDaily = new("team.auditLogView.groupingDaily", "Daily");
    public static readonly TextKey AxisCount = new("team.auditLogView.axisCount", "Count");
    public static readonly TextKey SeriesCalls = new("team.auditLogView.seriesCalls", "Calls");
    public static readonly TextKey ChartSuccessVsFailure = new("team.auditLogView.chartSuccessVsFailure", "Success vs Failure");
    public static readonly TextKey SeriesStatus = new("team.auditLogView.seriesStatus", "Status");
    public static readonly TextKey ChartByFeature = new("team.auditLogView.chartByFeature", "By Feature");
    public static readonly TextKey ChartTopCallers = new("team.auditLogView.chartTopCallers", "Top Callers");
    public static readonly TextKey NoDataForFilters = new("team.auditLogView.noDataForFilters", "No data available for the selected filters.");
    public static readonly TextKey ChartAverageResponseTime = new("team.auditLogView.chartAverageResponseTime", "Average Response Time");
    public static readonly TextKey ChartResponseTimeByFeature = new("team.auditLogView.chartResponseTimeByFeature", "Response Time by Feature");
    public static readonly TextKey ChartSlowestOperations = new("team.auditLogView.chartSlowestOperations", "Slowest Operations (Top 10)");
    public static readonly TextKey SeriesAverageMs = new("team.auditLogView.seriesAverageMs", "Avg ms");

    public static readonly TextKey NotifyQueryFailed = new("team.auditLogView.notifyQueryFailed", "Query failed");
    public static readonly TextKey NotifyChartDataFailed = new("team.auditLogView.notifyChartDataFailed", "Chart data failed");
    public static readonly TextKey NotifyNoDataToExport = new("team.auditLogView.notifyNoDataToExport", "No data to export");
    public static readonly TextKey NotifyExportFailed = new("team.auditLogView.notifyExportFailed", "Export failed");

    /// <summary>Failure tooltip lines. Placeholders: the scope checked, then its result.</summary>
    public static readonly TextKey FailureScope = new("team.auditLogView.failureScope", "Scope: {0} ({1})");
    public static readonly TextKey FailureReason = new("team.auditLogView.failureReason", "Reason: {0}");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        NotConfigured, AccessDenied, MongoRequired,
        TabLog, TabUsage, TabPerformance,
        Period, PeriodToday, PeriodSevenDays, PeriodThirtyDays, PeriodNinetyDays, FilterAll,
        Team, Source, ScopeFeature, ScopeAction, Event, Result, Success, Failure,
        Export, ExportCsv, ExportJson, PagingSummary,
        ColumnTime, ColumnCaller, ColumnScope, ColumnMethod, ColumnDuration, ColumnFeature, ColumnAction, ColumnDurationMs,
        DetailName, DetailValue, DetailEmpty, NoAdditionalDetails, TotalEntries,
        ChartCallsOverTime, GroupingHourly, GroupingDaily, AxisCount, SeriesCalls,
        ChartSuccessVsFailure, SeriesStatus, ChartByFeature, ChartTopCallers, NoDataForFilters,
        ChartAverageResponseTime, ChartResponseTimeByFeature, ChartSlowestOperations, SeriesAverageMs,
        NotifyQueryFailed, NotifyChartDataFailed, NotifyNoDataToExport, NotifyExportFailed,
        FailureScope, FailureReason,
    ];
}
