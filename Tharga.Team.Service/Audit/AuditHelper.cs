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
            // A job's correlation id groups every entry it writes. Without one each entry gets its own
            // generated id, which is exactly the grouping a worker needs and cannot reconstruct later.
            CorrelationId = ambient?.CorrelationId
                ?? (Guid.TryParse(httpContextAccessor?.HttpContext?.TraceIdentifier, out var traceId) ? traceId : Guid.NewGuid()),
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
