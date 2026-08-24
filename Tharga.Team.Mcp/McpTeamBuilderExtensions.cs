using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tharga.Mcp;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Mcp;

/// <summary>
/// Extension methods on <see cref="IThargaMcpBuilder"/> for wiring the Tharga.Team bridge
/// into the MCP pipeline.
/// </summary>
public static class McpTeamBuilderExtensions
{
    /// <summary>
    /// Registers the Team bridge: populates <see cref="IMcpContext"/> from the current <see cref="HttpContext"/>,
    /// enables <see cref="IMcpScopeChecker"/>, and registers built-in <c>mcp:*</c> scopes.
    /// </summary>
    public static IThargaMcpBuilder AddTeam(this IThargaMcpBuilder builder, Action<McpTeamOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new McpTeamOptions();
        configure?.Invoke(options);

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        // Replace the default AsyncLocal accessor with an HttpContext-backed one.
        var existing = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(IMcpContextAccessor));
        if (existing != null) builder.Services.Remove(existing);
        builder.Services.AddSingleton<IMcpContextAccessor, HttpContextMcpContextAccessor>();

        builder.Services.TryAddSingleton<IMcpScopeChecker, McpScopeChecker>();

        // MCP callers are agents presenting an API key — there is no user. Contribute the scheme so
        // RequireAuth accepts that credential without the host knowing about schemes at all; a bare
        // RequireAuthorization() resolves to the application's default scheme, which in a Blazor host is
        // OIDC, and answered an agent with a 302 to a login page (Tharga/Mcp#18).
        if (!builder.Options.AuthenticationSchemes.Contains(ApiKeyConstants.SchemeName))
        {
            builder.Options.AuthenticationSchemes.Add(ApiKeyConstants.SchemeName);
        }

        // Register built-in mcp:* scopes into both registries, because both routes to holding one are
        // legitimate: an access level grants it inside a team, while an app role or a system API key
        // grants it system-wide. Registering only as a team scope left it grantable but unsatisfiable —
        // the checker read system claims alone. Each extension creates its registry if missing.
        // A host may already have registered these by hand — registering mcp:discover as a system scope was
        // the documented workaround while the checker read system claims only, so the consumers this fix is
        // for are exactly the ones most likely to have it.
        //
        // The guard is on the team registry only, and the asymmetry is deliberate: ScopeRegistry.Register
        // still throws on a duplicate, because a team scope also carries an access level and a grant-only
        // flag that two registrations can genuinely disagree about. SystemScopeRegistry.Register skips a
        // name already present, so the system half needs no guard (Tharga/Team#237).
        builder.Services.AddThargaScopes(scopes =>
        {
            if (scopes.All.All(s => s.Name != McpScopes.Discover))
                scopes.Register(McpScopes.Discover, AccessLevel.Viewer, "Discover and list available MCP tools and resources.");
        });

        builder.Services.AddThargaSystemScopes(scopes =>
            scopes.Register(McpScopes.Discover, "Discover and list available MCP tools and resources."));

        // Always-on user-scope and team-scope resource providers. They self-gate on the
        // principal's UserId / TeamKey claim, so anonymous and system-only callers see nothing.
        builder.AddResourceProvider<TeamUserResourceProvider>();
        builder.AddResourceProvider<TeamResourceProvider>();

        // Opt-in system-scope resource providers (diagnostic data for Developers).
        if (options.ExposeSystemResources)
        {
            builder.AddResourceProvider<TeamSystemResourceProvider>();
        }

        return builder;
    }
}
