using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service;

/// <summary>
/// DispatchProxy that intercepts service method calls and enforces
/// <see cref="RequireAccessLevelAttribute"/> by reading TeamKey and AccessLevel claims from the current
/// principal (resolved via <see cref="ITeamPrincipalAccessor"/>, so it works for both HTTP and interactive
/// Blazor callers). Methods without the attribute are blocked (fail-closed). Logs audit entries when
/// IAuditLogger is available.
/// </summary>
public class AccessLevelProxy<T> : DispatchProxy where T : class
{
    private T _target;
    private ITeamPrincipalAccessor _principalAccessor;
    private IAuditLogger _auditLogger;
    private AuditMode _defaultAuditMode = AuditMode.Access;

    public static T Create(T target, ITeamPrincipalAccessor principalAccessor, IAuditLogger auditLogger = null)
        => Create(target, principalAccessor, auditLogger, AuditMode.Access);

    /// <summary>
    /// As above, with the host's default audit mode for methods whose attribute does not declare one.
    /// </summary>
    /// <remarks>
    /// An overload rather than an optional parameter, so an assembly already compiled against the existing
    /// signature keeps binding.
    /// </remarks>
    public static T Create(T target, ITeamPrincipalAccessor principalAccessor, IAuditLogger auditLogger, AuditMode defaultAuditMode)
    {
        var proxy = Create<T, AccessLevelProxy<T>>() as AccessLevelProxy<T>;
        proxy._target = target;
        proxy._principalAccessor = principalAccessor;
        proxy._auditLogger = auditLogger;
        proxy._defaultAuditMode = defaultAuditMode;
        return proxy as T;
    }

    /// <summary>Back-compat overload — adapts an <see cref="IHttpContextAccessor"/> to the default accessor.</summary>
    public static T Create(T target, IHttpContextAccessor httpContextAccessor, IAuditLogger auditLogger = null)
        => Create(target, new HttpContextTeamPrincipalAccessor(httpContextAccessor), auditLogger);

    /// <inheritdoc cref="Create(T, ITeamPrincipalAccessor, IAuditLogger, AuditMode)"/>
    public static T Create(T target, IHttpContextAccessor httpContextAccessor, IAuditLogger auditLogger, AuditMode defaultAuditMode)
        => Create(target, new HttpContextTeamPrincipalAccessor(httpContextAccessor), auditLogger, defaultAuditMode);

    protected override object Invoke(MethodInfo targetMethod, object[] args)
    {
        var attribute = GetAttribute(targetMethod);
        if (attribute == null)
            throw new InvalidOperationException(
                $"Method '{typeof(T).Name}.{targetMethod.Name}' is missing the [RequireAccessLevel] attribute. " +
                $"All methods on services registered with AddScopedWithAccessLevel must declare their required access level.");

        return ProxyInvoker.Invoke(targetMethod, args, _target, _principalAccessor,
            enforce: principal =>
            {
                CheckAccessLevel(principal, attribute.MinimumLevel);
                return TeamAccess.ForTeam(principal?.FindFirst(TeamClaimTypes.TeamKey)?.Value);
            },
            audit: (principal, ms, success, ex) =>
            {
                var denied = !success && ex is UnauthorizedAccessException;
                var mode = AuditModeResolver.Resolve(attribute.Audit, _defaultAuditMode);
                if (!AuditModeResolver.ShouldWrite(mode, denied)) return;

                LogAudit(principal, attribute.MinimumLevel, targetMethod.Name, ms, success,
                    AuditModeResolver.EventTypeFor(mode, denied, AuditEventType.AccessLevelDenial),
                    success ? null : ex?.Message);
            });
    }

    private void LogAudit(ClaimsPrincipal user, AccessLevel minimumLevel, string methodName, long durationMs, bool success,
        AuditEventType? eventType = null, string errorMessage = null)
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
            EventType = eventType ?? AuditEventType.ServiceCall,
            Feature = typeof(T).Name,
            Action = methodName,
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
            ScopeChecked = $"AccessLevel>={minimumLevel}",
            ScopeResult = success ? AuditScopeResult.Allowed : AuditScopeResult.Denied,
        };

        _auditLogger.Log(entry);
    }

    private RequireAccessLevelAttribute GetAttribute(MethodInfo methodInfo)
    {
        var interfaceMethod = typeof(T).GetMethod(
            methodInfo.Name,
            methodInfo.GetParameters().Select(p => p.ParameterType).ToArray());
        return interfaceMethod?.GetCustomAttribute<RequireAccessLevelAttribute>()
               ?? methodInfo.GetCustomAttribute<RequireAccessLevelAttribute>();
    }

    private static void CheckAccessLevel(ClaimsPrincipal user, AccessLevel minimumLevel)
    {
        var teamKey = user?.FindFirst(TeamClaimTypes.TeamKey)?.Value;
        if (string.IsNullOrEmpty(teamKey))
        {
            var selectedTeamKey = user?.FindFirst(TeamClaimTypes.SelectedTeamKey)?.Value;
            throw new UnauthorizedAccessException(string.IsNullOrEmpty(selectedTeamKey)
                ? "No team selected."
                : $"Access denied for the selected team '{selectedTeamKey}'.");
        }

        var accessLevelValue = user?.FindFirst(TeamClaimTypes.AccessLevel)?.Value;
        if (accessLevelValue == null || !Enum.TryParse<AccessLevel>(accessLevelValue, out var accessLevel))
            throw new UnauthorizedAccessException("Access level claim not found.");

        if (accessLevel > minimumLevel)
            throw new UnauthorizedAccessException(
                $"Access level '{accessLevel}' is insufficient. Minimum required: {minimumLevel}.");
    }
}
