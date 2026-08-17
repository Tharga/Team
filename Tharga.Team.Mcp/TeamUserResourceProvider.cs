using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Tharga.Mcp;
using Tharga.Team;

namespace Tharga.Team.Mcp;

/// <summary>
/// MCP resource provider that surfaces the authenticated caller's own user identity and
/// team memberships under <c>team://me</c>. Scope is <see cref="McpScope.User"/> —
/// the dispatcher's hierarchy filter lets Team and System callers see this too.
/// </summary>
public sealed class TeamUserResourceProvider : IMcpResourceProvider
{
    private readonly IUserService _userService;
    private readonly ITeamManagementService _teamService;
    private readonly ITeamDirectoryService _teamDirectory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public const string MeUri = "team://me";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    public TeamUserResourceProvider(
        IUserService userService,
        ITeamManagementService teamService,
        ITeamDirectoryService teamDirectory,
        IHttpContextAccessor httpContextAccessor)
    {
        _userService = userService;
        _teamService = teamService;
        _teamDirectory = teamDirectory;
        _httpContextAccessor = httpContextAccessor;
    }

    public McpScope Scope => McpScope.User;

    public Task<IReadOnlyList<McpResourceDescriptor>> ListResourcesAsync(IMcpContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(context.AsTeamContext()?.UserId))
            return Task.FromResult<IReadOnlyList<McpResourceDescriptor>>(Array.Empty<McpResourceDescriptor>());

        return Task.FromResult<IReadOnlyList<McpResourceDescriptor>>(new[]
        {
            new McpResourceDescriptor
            {
                Uri = MeUri,
                Name = "Current User",
                Description = "The authenticated caller's user identity and team memberships.",
                MimeType = "application/json",
            }
        });
    }

    public async Task<McpResourceContent> ReadResourceAsync(string uri, IMcpContext context, CancellationToken cancellationToken)
    {
        if (uri != MeUri)
            throw new InvalidOperationException($"Unknown resource URI '{uri}'.");

        var principal = _httpContextAccessor.HttpContext?.User;
        var user = await _userService.GetCurrentUserAsync(principal);
        if (user == null)
            throw new UnauthorizedAccessException("Authentication required.");

        var memberships = new List<object>();
        await foreach (var team in _teamDirectory.GetTeamsAsync().WithCancellation(cancellationToken))
        {
            var memberRow = await FindMemberAsync(team.Key, user.Key, cancellationToken);
            memberships.Add(new
            {
                teamKey = team.Key,
                teamName = team.Name,
                accessLevel = memberRow?.AccessLevel,
                state = memberRow?.State,
            });
        }

        var payload = new
        {
            user = new
            {
                key = user.Key,
                identity = user.Identity,
                name = user.Name,
                email = user.EMail,
            },
            memberships,
        };

        return new McpResourceContent
        {
            Uri = MeUri,
            Text = JsonSerializer.Serialize(payload, _jsonOptions),
            MimeType = "application/json",
        };
    }

    private async Task<ITeamMember> FindMemberAsync(string teamKey, string userKey, CancellationToken cancellationToken)
    {
        await foreach (var member in _teamService.GetMembersAsync(teamKey).WithCancellation(cancellationToken))
        {
            if (member.Key == userKey) return member;
        }
        return null;
    }
}
