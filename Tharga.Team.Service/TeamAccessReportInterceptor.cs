using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tharga.MongoDB.Interception;

namespace Tharga.Team.Service;

/// <summary>
/// Reports every database operation that no authorization decision covers, and lets it through.
/// </summary>
/// <remarks>
/// <b>The log-only counterpart of <see cref="TeamAccessInterceptor"/></b>, for finding out whether anything reaches
/// the database unscoped before deciding to enforce (Tharga/Team#397). It never rejects, so it is safe to register
/// on a running site. Register it with:
/// <code>
/// builder.AddMongoDB(o => o.AddCollectionInterceptor&lt;TeamAccessReportInterceptor&gt;());
/// </code>
/// Each distinct call path is logged once, at warning level, with the collection, the operation and the application
/// frames that led to it, so a reader of the log can find the code that needs a <see cref="TeamAccess"/> scope.
/// "Distinct" is the collection, the operation and the call path: the same worker polling every minute produces one
/// line, not one per poll.
/// <para>
/// Like the enforcing interceptor it runs at <see cref="InterceptionPoint.Invocation"/> only, and it asserts that a
/// decision was recorded, not that the right one was.
/// </para>
/// </remarks>
public sealed class TeamAccessReportInterceptor : ICollectionInterceptor
{
    private const int MaxFrames = 8;

    private readonly ILogger<TeamAccessReportInterceptor> _logger;
    private readonly ConcurrentDictionary<string, UnscopedCall> _observed = new();

    /// <summary>For a host that constructs the interceptor itself, with no logger: calls are still collected, see <see cref="Observed"/>.</summary>
    public TeamAccessReportInterceptor()
    {
    }

    public TeamAccessReportInterceptor(ILogger<TeamAccessReportInterceptor> logger)
    {
        _logger = logger;
    }

    public InterceptionPoint Points => InterceptionPoint.Invocation;

    /// <summary>The distinct unscoped call paths seen so far.</summary>
    public IReadOnlyCollection<UnscopedCall> Observed => [.. _observed.Values];

    public ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)
    {
        if (TeamAccess.Current == null) Report(call);

        return ValueTask.FromResult(InterceptDecision.Proceed);
    }

    private void Report(CollectionCallInfo call)
    {
        var path = CallPath(new StackTrace(skipFrames: 1, fNeedFileInfo: false));
        var key = $"{call.CollectionName}|{call.Operation}|{path}";

        if (!_observed.TryAdd(key, new UnscopedCall(call.CollectionName, call.Operation, path))) return;

        _logger?.LogWarning(
            "Unscoped database call: {Collection}.{Operation} reached the database with no TeamAccess scope. Call path: {CallPath}",
            call.CollectionName, call.Operation, path);
    }

    /// <summary>
    /// The frames that identify the caller: everything except the runtime, the framework and the database layer,
    /// innermost first, so the first entry is the code that made the call.
    /// </summary>
    internal static string CallPath(StackTrace trace)
    {
        var frames = trace.GetFrames()
            .Select(f => f.GetMethod())
            .Where(m => m?.DeclaringType != null)
            .Select(m => $"{m.DeclaringType.FullName}.{m.Name}")
            .Where(name => !IsInfrastructure(name))
            .Take(MaxFrames);

        return string.Join(" <- ", frames);
    }

    private static bool IsInfrastructure(string name)
        => name.StartsWith("System.", StringComparison.Ordinal)
           || name.StartsWith("Microsoft.", StringComparison.Ordinal)
           || name.StartsWith("Tharga.MongoDB.", StringComparison.Ordinal)
           || name.StartsWith("MongoDB.", StringComparison.Ordinal)
           || name.StartsWith("Tharga.Team.Service.TeamAccessReportInterceptor.", StringComparison.Ordinal);
}

/// <summary>One distinct call path that reached the database with no authorization recorded.</summary>
/// <param name="Collection">The collection the call touched.</param>
/// <param name="Operation">The operation, for example <c>GetOneAsync</c>.</param>
/// <param name="CallPath">The application frames that led to the call, innermost first.</param>
public sealed record UnscopedCall(string Collection, string Operation, string CallPath);
