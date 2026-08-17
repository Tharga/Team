using System.Text.Json;
using Tharga.Mcp;
using Tharga.Team;

namespace Tharga.Team.Mcp;

/// <summary>
/// MCP resource provider that surfaces the caller's *current* team under <c>team://team*</c>.
/// Scope is <see cref="McpScope.Team"/>; the resource set is gated on a <c>TeamKey</c> claim being
/// present on the principal — anonymous or non-team callers see no resources and read attempts throw
/// <see cref="UnauthorizedAccessException"/>.
///
/// Cross-tenant enumeration (reading other teams the caller does not belong to) is intentionally
/// not supported here — that lives in a System-scope provider once <c>ITeamManagementService.GetAllTeamsAsync</c>
/// is added.
/// </summary>
public sealed class TeamResourceProvider : IMcpResourceProvider
{
    private readonly ITeamManagementService _teamService;
    private readonly IApiKeyAdministrationService _apiKeyAdministrationService;

    public const string TeamUri = "team://team";
    public const string MembersUri = "team://team/members";
    public const string ApiKeysUri = "team://team/apikeys";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    public TeamResourceProvider(
        ITeamManagementService teamService,
        IApiKeyAdministrationService apiKeyAdministrationService = null)
    {
        _teamService = teamService;
        _apiKeyAdministrationService = apiKeyAdministrationService;
    }

    public McpScope Scope => McpScope.Team;

    public Task<IReadOnlyList<McpResourceDescriptor>> ListResourcesAsync(IMcpContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(context.AsTeamContext()?.TeamId))
            return Task.FromResult<IReadOnlyList<McpResourceDescriptor>>(Array.Empty<McpResourceDescriptor>());

        var list = new List<McpResourceDescriptor>
        {
            new()
            {
                Uri = TeamUri,
                Name = "Current Team",
                Description = "Metadata for the caller's current team (Key, Name, Icon, ConsentedRoles).",
                MimeType = "application/json",
            },
            new()
            {
                Uri = MembersUri,
                Name = "Current Team Members",
                Description = "Members of the caller's current team.",
                MimeType = "application/json",
            },
        };

        if (_apiKeyAdministrationService != null)
        {
            list.Add(new McpResourceDescriptor
            {
                Uri = ApiKeysUri,
                Name = "Current Team API Keys",
                Description = "API keys for the caller's current team. Raw key values are redacted.",
                MimeType = "application/json",
            });
        }

        return Task.FromResult<IReadOnlyList<McpResourceDescriptor>>(list);
    }

    public async Task<McpResourceContent> ReadResourceAsync(string uri, IMcpContext context, CancellationToken cancellationToken)
    {
        var teamKey = context.AsTeamContext()?.TeamId;
        if (string.IsNullOrEmpty(teamKey))
            throw new UnauthorizedAccessException("No team selected.");

        return uri switch
        {
            TeamUri => await ReadTeamAsync(teamKey, cancellationToken),
            MembersUri => await ReadMembersAsync(teamKey, cancellationToken),
            ApiKeysUri => await ReadApiKeysAsync(teamKey, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown resource URI '{uri}'."),
        };
    }

    /// <remarks>
    /// Reads by key rather than by scanning the caller's memberships. <c>GetTeamsAsync()</c> resolves the
    /// current <i>user</i>, so an API key — authenticated, and carrying the team in a server-issued claim —
    /// had no teams and could never read the very resource <c>ListResourcesAsync</c> had just advertised
    /// to it. Listing gated on the claim while reading gated on membership, and the two disagreed.
    /// <para>
    /// <b>Safe only because <paramref name="teamKey"/> comes from <c>TeamMcpContext.TeamId</c></b> — the
    /// caller's own <c>TeamKey</c> claim, issued by the server — and never from the request. A caller can
    /// therefore only ever name its own team. <see cref="ITeamManagementService.GetTeamByKeyAsync"/> is deliberately
    /// unauthorized ("regardless of the caller's membership"), so passing a caller-supplied key here would
    /// be a cross-team read with nothing to stop it.
    /// </para>
    /// </remarks>
    private async Task<McpResourceContent> ReadTeamAsync(string teamKey, CancellationToken cancellationToken)
    {
        var team = await _teamService.GetTeamByKeyAsync(teamKey);
        if (team == null) throw new InvalidOperationException($"Team '{teamKey}' not found.");

        var payload = new
        {
            key = team.Key,
            name = team.Name,
            icon = team.Icon,
            consentedRoles = team.ConsentedRoles ?? Array.Empty<string>(),
        };

        return new McpResourceContent
        {
            Uri = TeamUri,
            Text = JsonSerializer.Serialize(payload, _jsonOptions),
            MimeType = "application/json",
        };
    }

    private async Task<McpResourceContent> ReadMembersAsync(string teamKey, CancellationToken cancellationToken)
    {
        var items = new List<object>();
        await foreach (var member in _teamService.GetMembersAsync(teamKey).WithCancellation(cancellationToken))
        {
            items.Add(new
            {
                key = member.Key,
                name = member.Name,
                accessLevel = member.AccessLevel,
                state = member.State,
                tenantRoles = member.TenantRoles ?? Array.Empty<string>(),
                scopeOverrides = member.ScopeOverrides ?? Array.Empty<string>(),
                invited = member.Invitation != null,
            });
        }

        return new McpResourceContent
        {
            Uri = MembersUri,
            Text = JsonSerializer.Serialize(new { teamKey, items }, _jsonOptions),
            MimeType = "application/json",
        };
    }

    private async Task<McpResourceContent> ReadApiKeysAsync(string teamKey, CancellationToken cancellationToken)
    {
        if (_apiKeyAdministrationService == null)
            throw new InvalidOperationException("IApiKeyAdministrationService is not registered.");

        var items = new List<object>();
        await foreach (var key in _apiKeyAdministrationService.GetKeysAsync(teamKey).WithCancellation(cancellationToken))
        {
            items.Add(new
            {
                key = key.Key,
                name = key.Name,
                accessLevel = key.AccessLevel,
                roles = key.Roles ?? Array.Empty<string>(),
                scopeOverrides = key.ScopeOverrides ?? Array.Empty<string>(),
                expiryDate = key.ExpiryDate,
                createdAt = key.CreatedAt,
                createdBy = key.CreatedBy,
                // Raw ApiKey value intentionally omitted — redaction pattern.
            });
        }

        return new McpResourceContent
        {
            Uri = ApiKeysUri,
            Text = JsonSerializer.Serialize(new { teamKey, items }, _jsonOptions),
            MimeType = "application/json",
        };
    }
}
