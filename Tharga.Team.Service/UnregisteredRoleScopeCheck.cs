using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tharga.Team.Service;

/// <summary>
/// Logs a warning at startup for every scope named by a code-registered tenant role that is absent from
/// <see cref="IScopeRegistry"/>, naming the role and the scope.
/// </summary>
/// <remarks>
/// <b>A misspelled scope on a role grants nothing and reports nothing.</b> Role scopes are plain strings
/// that <c>TenantRoleRegistry.Register</c> stores without validating, so <c>"case:raed"</c> registers
/// happily, is unioned into the effective set happily, and is then never matched by any
/// <see cref="RequireScopeAttribute"/>. The result is indistinguishable from a scope nobody needed — which
/// for a scope guarding regulated records is the failure that matters most and reports least.
/// <para>
/// <b>This warns rather than throws, deliberately.</b> Naming an unregistered scope on a code role is the
/// documented way to obtain grant-only behaviour on 3.13 and earlier, so throwing would break every host
/// that followed the guide as written. <see cref="ScopeRegistry.RegisterGrantOnly"/> is the replacement:
/// it keeps the registry entry, so a typo becomes visible while the scope is still granted by no access
/// level. A host that has moved every such scope across can treat any remaining warning as a defect.
/// </para>
/// </remarks>
internal sealed class UnregisteredRoleScopeCheck(
    ITenantRoleRegistry roles,
    IScopeRegistry scopes,
    ILogger<UnregisteredRoleScopeCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (roles == null || scopes == null) return Task.CompletedTask;

        var registered = scopes.All.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var role in roles.All)
        {
            foreach (var scope in (role.Scopes ?? []).Where(s => !registered.Contains(s)))
            {
                logger?.LogWarning(
                    "The code-registered role '{Role}' names scope '{Scope}', which is not registered. It will " +
                    "be granted to holders of the role but matches no registered scope, so a misspelling here " +
                    "is indistinguishable from a scope nobody needed. Register it with ConfigureScopes — use " +
                    "RegisterGrantOnly if it must not be granted by access level.",
                    role.Name, scope);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
