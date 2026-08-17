using System.Text.Json;
using Tharga.Mcp;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Mcp;

/// <summary>
/// Read-only MCP resource provider that surfaces system-scope Team data for diagnostic use.
/// Only available to callers with the Developer role (see <see cref="TeamMcpContext.IsDeveloper"/>, recovered
/// from the context via <c>AsTeamContext()</c> — <see cref="IMcpContext"/> itself carries no identity).
/// Registered by <c>AddTeam</c> when <see cref="McpTeamOptions.ExposeSystemResources"/> is true.
/// </summary>
public sealed class TeamSystemResourceProvider : IMcpResourceProvider
{
    private readonly IApiKeyAdministrationService _apiKeyAdministrationService;
    private readonly ITenantRoleRegistry _tenantRoleRegistry;
    private readonly CompositeAuditLogger _auditLogger;

    public const string SystemKeysUri = "team://system/apikeys";
    public const string RolesUri = "team://system/roles";
    public const string AuditUri = "team://system/audit";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IAuditOversightService _auditOversightService;

    /// <remarks>
    /// <c>auditOversightService</c> reads audit across every team and carries the authorization with it.
    /// It is injected instead of the logger so this provider performs no audit check of its own — see
    /// <see cref="ReadAuditAsync"/>.
    /// </remarks>
    public TeamSystemResourceProvider(
        IApiKeyAdministrationService apiKeyAdministrationService = null,
        ITenantRoleRegistry tenantRoleRegistry = null,
        CompositeAuditLogger auditLogger = null,
        IAuditOversightService auditOversightService = null)
    {
        _apiKeyAdministrationService = apiKeyAdministrationService;
        _tenantRoleRegistry = tenantRoleRegistry;
        _auditLogger = auditLogger;
        _auditOversightService = auditOversightService;
    }

    public McpScope Scope => McpScope.System;

    /// <remarks>
    /// <b>A resource is listed only if this caller could read it.</b> Listing something they cannot read
    /// advertises data they may not have; omitting something they can read hides a capability they hold.
    /// Both were true here at once — the read moved onto <c>audit:read</c> while the list one method above
    /// still asked for the Developer role, so a scope-holder without the role was refused the listing and
    /// granted the read, and a role-holder without the scope got the reverse.
    /// <para>
    /// Each entry therefore asks the same question its read asks, rather than one check standing in for
    /// all of them.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<McpResourceDescriptor>> ListResourcesAsync(IMcpContext context, CancellationToken cancellationToken)
    {
        var list = new List<McpResourceDescriptor>();
        var isDeveloper = context.AsTeamContext()?.IsDeveloper == true;

        if (isDeveloper && _apiKeyAdministrationService != null)
        {
            list.Add(new McpResourceDescriptor
            {
                Uri = SystemKeysUri,
                Name = "System API Keys",
                Description = "Cross-tenant system API keys (not bound to a team). Raw key values are redacted.",
                MimeType = "application/json",
            });
        }

        if (isDeveloper && _tenantRoleRegistry != null)
        {
            list.Add(new McpResourceDescriptor
            {
                Uri = RolesUri,
                Name = "Tenant Roles",
                Description = "Registered tenant roles and their granted scopes.",
                MimeType = "application/json",
            });
        }

        // Asked of the service, which is what the read is gated on -- a registered logger says the feature
        // exists, not that this caller may use it.
        if (await CanReadAuditAsync())
        {
            list.Add(new McpResourceDescriptor
            {
                Uri = AuditUri,
                Name = "Audit Log",
                Description = "Most recent ~100 audit entries from the last 7 days.",
                MimeType = "application/json",
            });
        }

        return list;
    }

    /// <summary>
    /// Whether this caller could read audit, answered by attempting the smallest possible read through
    /// the gated service.
    /// </summary>
    /// <remarks>
    /// Asking the service rather than re-deriving its rule is the point: a second copy of "may this caller
    /// read audit" is exactly what left the list and the read disagreeing. The cost is one bounded query
    /// during discovery, which is a listing operation already.
    /// </remarks>
    private async Task<bool> CanReadAuditAsync()
    {
        if (_auditOversightService == null) return false;

        try
        {
            await _auditOversightService.QueryAllAsync(new AuditQuery { Take = 1 });
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task<McpResourceContent> ReadResourceAsync(string uri, IMcpContext context, CancellationToken cancellationToken)
    {
        // Audit is deliberately outside the role check: IAuditOversightService carries
        // [RequireScope(audit:read)] and decides for itself, which is the whole point of moving the gate
        // into the service. Leaving the role check in front of it would refuse a holder of the system
        // scope who does not also hold the role -- exactly the divergence this set out to remove.
        if (uri == AuditUri) return await ReadAuditAsync();

        if (context.AsTeamContext()?.IsDeveloper != true)
            throw new UnauthorizedAccessException("System resources require the Developer role.");

        return uri switch
        {
            SystemKeysUri => await ReadSystemKeysAsync(cancellationToken),
            RolesUri => ReadRoles(),
            _ => throw new InvalidOperationException($"Unknown resource URI '{uri}'."),
        };
    }

    private async Task<McpResourceContent> ReadSystemKeysAsync(CancellationToken cancellationToken)
    {
        if (_apiKeyAdministrationService == null)
            throw new InvalidOperationException("IApiKeyAdministrationService is not registered.");

        var keys = new List<object>();
        await foreach (var key in _apiKeyAdministrationService.GetSystemKeysAsync().WithCancellation(cancellationToken))
        {
            keys.Add(new
            {
                key.Key,
                key.Name,
                SystemScopes = key.SystemScopes ?? Array.Empty<string>(),
                key.ExpiryDate,
                key.CreatedAt,
                key.CreatedBy,
            });
        }

        return new McpResourceContent
        {
            Uri = SystemKeysUri,
            Text = JsonSerializer.Serialize(new { items = keys }, _jsonOptions),
            MimeType = "application/json",
        };
    }

    private McpResourceContent ReadRoles()
    {
        if (_tenantRoleRegistry == null)
            throw new InvalidOperationException("ITenantRoleRegistry is not registered.");

        var items = _tenantRoleRegistry.All.Select(r => new
        {
            r.Name,
            r.Scopes,
        });

        return new McpResourceContent
        {
            Uri = RolesUri,
            Text = JsonSerializer.Serialize(new { items }, _jsonOptions),
            MimeType = "application/json",
        };
    }

    /// <remarks>
    /// Goes through <see cref="IAuditOversightService"/>, which carries <c>[RequireScope(audit:read)]</c>
    /// as a system grant, rather than reading the logger directly behind an <c>IsDeveloper</c> check.
    /// <para>
    /// <b>That check was a third rule for the same question.</b> The UI and REST asked whether the caller
    /// held <c>audit:read</c>; this asked whether they held a host-configurable role, so the same API key
    /// got different answers from different surfaces. Behaviour changes accordingly: a holder of that
    /// role without <c>audit:read</c> loses access here, and a holder of the system scope without the
    /// role gains it.
    /// </para>
    /// </remarks>
    private async Task<McpResourceContent> ReadAuditAsync()
    {
        if (_auditOversightService == null)
            throw new InvalidOperationException("IAuditOversightService is not registered.");

        var query = new AuditQuery
        {
            From = DateTime.UtcNow.AddDays(-7),
            Take = 100,
            SortDescending = true,
        };

        var result = await _auditOversightService.QueryAllAsync(query);

        return new McpResourceContent
        {
            Uri = AuditUri,
            Text = JsonSerializer.Serialize(new
            {
                total = result.TotalCount,
                items = result.Items,
            }, _jsonOptions),
            MimeType = "application/json",
        };
    }
}
