using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tharga.Team.Service.Audit;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// DispatchProxy that intercepts service method calls and enforces
/// <see cref="RequireScopeAttribute"/> by checking scope claims on the current principal (resolved via
/// <see cref="ITeamPrincipalAccessor"/>, so it works for both HTTP and interactive Blazor callers).
/// Methods without the attribute throw InvalidOperationException (fail-closed). Also verifies a TeamKey
/// claim is present. Logs audit entries when IAuditLogger is available.
/// </summary>
public class ScopeProxy<T> : DispatchProxy where T : class
{
    private T _target;
    private ITeamPrincipalAccessor _principalAccessor;
    private IAuditLogger _auditLogger;
    private ServiceScopeKind _scopeKind;

    /// <param name="scopeKind">
    /// Deliberately required. A default would pick an authorization policy on the caller's behalf, which
    /// is how a service ends up enforcing something other than what its author assumed.
    /// </param>
    public static T Create(T target, ITeamPrincipalAccessor principalAccessor, ServiceScopeKind scopeKind, IAuditLogger auditLogger = null)
    {
        var proxy = Create<T, ScopeProxy<T>>() as ScopeProxy<T>;
        proxy._target = target;
        proxy._principalAccessor = principalAccessor;
        proxy._auditLogger = auditLogger;
        proxy._scopeKind = scopeKind;
        return proxy as T;
    }

    /// <summary>Overload for HTTP-only callers — adapts an <see cref="IHttpContextAccessor"/> to the default accessor.</summary>
    public static T Create(T target, IHttpContextAccessor httpContextAccessor, ServiceScopeKind scopeKind, IAuditLogger auditLogger = null)
        => Create(target, new HttpContextTeamPrincipalAccessor(httpContextAccessor), scopeKind, auditLogger);

    protected override object Invoke(MethodInfo targetMethod, object[] args)
    {
        var attribute = GetAttribute(targetMethod);
        if (attribute == null)
            throw new InvalidOperationException(
                $"Method '{typeof(T).Name}.{targetMethod.Name}' is missing the [RequireScope] attribute. " +
                $"All methods on services registered with AddScopedWithScopes must declare their required scope.");

        var (feature, action) = AuditEntry.ParseScope(attribute.Scope);

        return ProxyInvoker.Invoke(targetMethod, args, _target, _principalAccessor,
            enforce: principal =>
            {
                CheckScope(principal, attribute.Scope, _scopeKind, targetMethod, args);
                return _scopeKind == ServiceScopeKind.Team
                    ? TeamAccess.ForTeam(ResolveTeamKey(targetMethod, args))
                    : TeamAccess.System(attribute.Scope);
            },
            audit: (principal, ms, success, ex) =>
            {
                var scopeResult = !success && ex is UnauthorizedAccessException uae && uae.Message.Contains("Missing required scope")
                    ? AuditScopeResult.Denied
                    : AuditScopeResult.Allowed;
                LogAudit(principal, attribute.Scope, feature, action, targetMethod.Name, ms, success, scopeResult, success ? null : ex?.Message);
            });
    }

    private void LogAudit(ClaimsPrincipal user, string scope, string feature, string action, string methodName, long durationMs, bool success, AuditScopeResult scopeResult, string errorMessage = null)
    {
        if (_auditLogger == null) return;

        var callerSource = user?.Identity?.AuthenticationType switch
        {
            ApiKeyConstants.SchemeName => AuditCallerSource.Api,
            "Cookies" or "AuthenticationTypes.Federation" => AuditCallerSource.Web,
            _ => AuditCallerSource.Unknown
        };

        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = scopeResult == AuditScopeResult.Denied ? AuditEventType.ScopeDenial : AuditEventType.ServiceCall,
            Feature = feature,
            Action = action,
            MethodName = methodName,
            DurationMs = durationMs,
            Success = success,
            ErrorMessage = errorMessage,
            CallerType = callerSource == AuditCallerSource.Api ? AuditCallerType.ApiKey : AuditCallerType.User,
            CorrelationId = AuditHelper.ResolveCorrelationId(AuditHelper.DeclaredCorrelationId(user)),
            CallerIdentity = user?.FindFirst(ClaimTypes.Name)?.Value
                ?? user?.FindFirst("preferred_username")?.Value
                ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user?.FindFirst("name")?.Value,
            TeamKey = user?.FindFirst(TeamClaimTypes.TeamKey)?.Value,
            AccessLevel = user?.FindFirst(TeamClaimTypes.AccessLevel)?.Value,
            CallerSource = callerSource,
            ScopeChecked = scope,
            ScopeResult = scopeResult,
        };

        _auditLogger.Log(entry);
    }

    private RequireScopeAttribute GetAttribute(MethodInfo methodInfo)
    {
        var interfaceMethod = typeof(T).GetMethod(
            methodInfo.Name,
            methodInfo.GetParameters().Select(p => p.ParameterType).ToArray());
        return interfaceMethod?.GetCustomAttribute<RequireScopeAttribute>()
               ?? methodInfo.GetCustomAttribute<RequireScopeAttribute>();
    }

    private static void CheckScope(ClaimsPrincipal user, string requiredScope, ServiceScopeKind scopeKind, MethodInfo method, object[] args)
    {
        if (scopeKind == ServiceScopeKind.System)
        {
            if (!TeamScopePolicy.HasSystemScope(user, requiredScope))
                throw new UnauthorizedAccessException($"Missing required scope '{requiredScope}'.");
            return;
        }

        var targetTeamKey = ResolveTeamKey(method, args);
        if (string.IsNullOrEmpty(targetTeamKey))
            throw new UnauthorizedAccessException(
                $"'{typeof(T).Name}.{method.Name}' is registered as a team service but the call names no team.");

        if (!TeamScopePolicy.HasTeamScope(user, requiredScope, targetTeamKey))
            throw new UnauthorizedAccessException(
                $"This operation on team '{targetTeamKey}' requires the '{requiredScope}' scope on that team.");
    }

    /// <summary>
    /// The team the call acts on, taken from the first parameter. A team service authorizes against the
    /// team named in the call's own arguments, not merely the one the caller happens to have selected —
    /// otherwise holding a scope for team A authorizes acting on team B.
    /// </summary>
    /// <remarks>
    /// Matched by parameter name, agreeing with <see cref="ServiceScopeValidation"/>: a first parameter of
    /// type string is not necessarily a team, so binding positionally would silently authorize against
    /// whatever string happened to come first.
    /// </remarks>
    private static string ResolveTeamKey(MethodInfo method, object[] args)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0 || args is not { Length: > 0 }) return null;

        return parameters[0].ParameterType == typeof(string) && parameters[0].Name == ServiceScopeValidation.TeamKeyParameterName
            ? args[0] as string
            : null;
    }
}
