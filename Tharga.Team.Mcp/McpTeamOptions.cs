namespace Tharga.Team.Mcp;

/// <summary>
/// Configuration for the Tharga.Team.Mcp bridge.
/// </summary>
public sealed class McpTeamOptions
{
    /// <summary>
    /// Role that must be present on the caller for <see cref="Tharga.Mcp.McpScope.System"/> calls.
    /// Defaults to <c>"Developer"</c> to match Tharga.Team conventions.
    /// </summary>
    public string DeveloperRole { get; set; } = "Developer";

    /// <summary>
    /// When true, registers read-only system-scope resource providers that expose cross-tenant
    /// team, API-key, role, and audit-log data for diagnostic use by Developers.
    /// Default false — opt in only if you want diagnostic data surfaced over MCP.
    /// </summary>
    public bool ExposeSystemResources { get; set; }

    /// <summary>
    /// When true, registers tools over a team's support AI connection (<see cref="SupportAiConnectionToolProvider"/>):
    /// read it, list models, test, connect, use the platform's model and turn the assistant off. Default false.
    /// </summary>
    /// <remarks>
    /// <b>Opt-in, as the REST endpoints are only mapped when the host maps them.</b> Every tool is checked by the service,
    /// which requires <c>assistant:manage</c> on the team, but an upgrade should not hand a management surface to agents
    /// that already hold the scope. Needs the connection service, which <c>AddThargaSupportAiConnections</c> registers;
    /// without it nothing is offered.
    /// </remarks>
    public bool ExposeSupportAiConnection { get; set; }
}
