using System.Reflection;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// Checks that a service interface is wholly the kind it is registered as, so the classification can be
/// true of every method rather than most of them.
/// </summary>
/// <remarks>
/// Both directions are enforced. Rejecting a team service whose method names no team is the obvious half;
/// rejecting a system service that <i>does</i> name one is what stops a mixed interface being registered
/// as a system service specifically to escape the team check. Without the second rule the scheme is
/// advisory.
/// The team key is matched by parameter <b>name</b>, not type: <c>RefreshSystemKeyAsync(string key)</c>
/// takes a string first and is not team-bound, so type alone cannot tell the two apart.
/// </remarks>
internal static class ServiceScopeValidation
{
    public const string TeamKeyParameterName = "teamKey";

    public static void Validate(Type serviceType, ServiceScopeKind scopeKind)
    {
        var offenders = scopeKind == ServiceScopeKind.Team
            ? serviceType.GetMethods().Where(m => !NamesATeam(m)).Select(m => $"{m.Name} names no '{TeamKeyParameterName}' parameter").ToArray()
            : serviceType.GetMethods().Where(TakesATeam).Select(m => $"{m.Name} takes a '{TeamKeyParameterName}' parameter")
                .Concat(serviceType.GetMethods().Where(AllowsSystemGrant).Select(m => $"{m.Name} sets {nameof(RequireScopeAttribute.AllowSystemGrant)}"))
                .ToArray();

        if (offenders.Length == 0) return;

        var expectation = scopeKind == ServiceScopeKind.Team
            ? $"Every method on a team service must take the team it acts on as its first parameter, named '{TeamKeyParameterName}'."
            : $"No method on a system service may take a '{TeamKeyParameterName}' parameter — move those to a team service. " +
              $"Nor may it set {nameof(RequireScopeAttribute.AllowSystemGrant)}, which only widens a team service: a system service is already authorized by the system grant alone.";

        throw new InvalidOperationException(
            $"'{serviceType.Name}' cannot be registered as a {scopeKind} service. {expectation} " +
            $"Offending members: {string.Join(", ", offenders)}.");
    }

    private static bool NamesATeam(MethodInfo method)
    {
        var parameters = method.GetParameters();
        return parameters.Length > 0
               && parameters[0].ParameterType == typeof(string)
               && parameters[0].Name == TeamKeyParameterName;
    }

    private static bool TakesATeam(MethodInfo method)
        => method.GetParameters().Any(p => p.Name == TeamKeyParameterName);

    private static bool AllowsSystemGrant(MethodInfo method)
        => method.GetCustomAttribute<RequireScopeAttribute>()?.AllowSystemGrant == true;
}
