using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// Shared helper for building audit entries from HTTP context.
/// Used by both ScopeProxy and audit decorators.
/// </summary>
internal static class AuditHelper
{
    /// <summary>
    /// The id that groups every entry written for one unit of work: the declared one, else the ambient
    /// trace, else a fresh id.
    /// </summary>
    /// <remarks>
    /// <b>This used to be derived from <c>HttpContext.TraceIdentifier</c> parsed as a <see cref="Guid"/>,
    /// which can never succeed.</b> That identifier is <c>{ConnectionId}:{RequestNumber:X8}</c> —
    /// <c>0HMVDBP0M8BSM:00000001</c> — so the parse failed on every request and each entry fell through to
    /// its own new id. Measured on a consumer's database: 417 entries, 417 distinct correlation ids, and
    /// the grouping the field is documented to provide had never worked outside declared background work
    /// (Tharga/Team#260).
    /// <para>
    /// <see cref="Activity.Current"/> is what actually spans a request, and it is already flowing wherever
    /// the host has tracing on. Its trace id is sixteen bytes, as a Guid is, so the mapping is the value
    /// itself rather than a hash of it — the same id can be matched against the traces in a telemetry tool.
    /// </para>
    /// <para>
    /// <b>Every writer resolves it here.</b> The two enforcement proxies build their entries inline rather
    /// than through <see cref="BuildEntry"/>, so a fix applied only there would still have left a
    /// consumer's entry and the proxy trace of the same call disagreeing — which is the pair the grouping
    /// exists to join.
    /// </para>
    /// </remarks>
    public static Guid ResolveCorrelationId(Guid? declared)
        => declared ?? FromActivity() ?? Guid.NewGuid();

    /// <summary>
    /// The ambient actor's correlation id, but only where that actor is the one being recorded. A real
    /// principal always wins, so a scope left open on a pooled thread cannot pull a person's entries into
    /// a background job's group.
    /// </summary>
    public static Guid? DeclaredCorrelationId(ClaimsPrincipal user)
        => user?.Identity?.IsAuthenticated == true ? null : AuditContextAccessor.Ambient?.CorrelationId;

    private static Guid? FromActivity()
    {
        var traceId = Activity.Current?.TraceId;
        if (traceId is null || traceId.Value == default) return null;

        return Guid.ParseExact(traceId.Value.ToHexString(), "N");
    }

    public static AuditEntry BuildEntry(
        IHttpContextAccessor httpContextAccessor,
        string feature,
        string action,
        string methodName,
        long durationMs,
        bool success,
        string errorMessage = null,
        string teamKey = null,
        IReadOnlyDictionary<string, string> metadata = null,
        AuditEventType eventType = AuditEventType.ServiceCall)
    {
        var user = httpContextAccessor?.HttpContext?.User;
        var identity = user?.Identity;

        var callerSource = identity?.AuthenticationType switch
        {
            ApiKeyConstants.SchemeName => AuditCallerSource.Api,
            "Cookies" or "AuthenticationTypes.Federation" => AuditCallerSource.Web,
            _ => AuditCallerSource.Unknown
        };

        // Only positive evidence names an actor. This used to fall through to User for anything that was
        // not an API key, so a caller with no HttpContext at all — a hosted service, a message handler —
        // was recorded as a person with a null identity (Tharga/Team#163). An authenticated principal
        // under an unrecognised scheme is still a person; the absence of one is not.
        var callerType = callerSource switch
        {
            AuditCallerSource.Api => AuditCallerType.ApiKey,
            AuditCallerSource.Web => AuditCallerType.User,
            _ => identity?.IsAuthenticated == true ? AuditCallerType.User : AuditCallerType.Unknown
        };

        // A declared background actor fills in only where no authenticated caller was found. A real
        // principal always wins: a scope left open on a pooled thread must never be able to relabel a
        // genuine user's action as the system's.
        var ambient = identity?.IsAuthenticated == true ? null : AuditContextAccessor.Ambient;
        if (ambient != null)
        {
            callerType = ambient.CallerType;
            callerSource = ambient.CallerSource;
        }

        return new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = eventType,
            Feature = feature,
            Action = action,
            MethodName = methodName,
            DurationMs = durationMs,
            Success = success,
            ErrorMessage = errorMessage,
            CallerType = callerType,
            CorrelationId = ResolveCorrelationId(ambient?.CorrelationId),
            CallerIdentity = user?.FindFirst(ClaimTypes.Name)?.Value
                ?? user?.FindFirst("preferred_username")?.Value
                ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user?.FindFirst("name")?.Value
                ?? ambient?.Identity,
            CallerKeyId = user?.FindFirst(TeamClaimTypes.ApiKeyId)?.Value,
            // Deliberately no fallback chain: this is the subject or nothing, which is what makes it
            // exact-matchable. CallerIdentity stays the human-readable one.
            CallerUserIdentity = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            TeamKey = teamKey ?? user?.FindFirst(TeamClaimTypes.TeamKey)?.Value ?? ambient?.TeamKey,
            AccessLevel = user?.FindFirst(TeamClaimTypes.AccessLevel)?.Value,
            CallerSource = callerSource,
            Metadata = metadata is { Count: > 0 } ? new Dictionary<string, string>(metadata) : null,
        };
    }
}
