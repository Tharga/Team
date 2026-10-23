using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// Decorator over <see cref="IUserService"/> that enforces user-store authorization in the service
/// layer (so the same checks protect the Blazor circuit and any consumer's REST controller):
/// <list type="bullet">
/// <item>Resolve current user / seed-own-name (invitation accept) — pass through (self-service).</item>
/// <item>Set display name — allowed on the caller's own record, otherwise <c>users:manage</c>.</item>
/// <item>Enumerate users, read by key, write activity/directory fields, delete — <c>users:manage</c> (system).</item>
/// </list>
/// The automatic LastSeen stamping and oid backfill are internal self-calls inside
/// <see cref="UserServiceBase"/> and never pass through this decorator.
/// </summary>
public sealed class AuthorizationUserServiceDecorator : IUserService
{
    private readonly IUserService _inner;
    private readonly TeamAuthorizer _authorizer;
    private readonly Func<ITeamService> _teamServiceFactory;

    /// <param name="inner">The undecorated user store.</param>
    /// <param name="authorizer">Claims-based authorization primitives.</param>
    /// <param name="teamServiceFactory">
    /// Resolves <see cref="ITeamService"/> on demand for <see cref="GetTeamMemberUsersAsync"/>. Deliberately
    /// a factory rather than an instance: <c>TeamServiceBase</c> takes an <see cref="IUserService"/>, so
    /// constructor-injecting the team service here would close a dependency cycle. When omitted, the
    /// co-member projection yields the caller alone.
    /// </param>
    public AuthorizationUserServiceDecorator(IUserService inner, TeamAuthorizer authorizer, Func<ITeamService> teamServiceFactory = null)
    {
        _inner = inner;
        _authorizer = authorizer;
        _teamServiceFactory = teamServiceFactory;
    }

    // Self-service — pass through.
    public Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal = null) => _inner.GetCurrentUserAsync(claimsPrincipal);
    public Task SeedUserNameAsync(string userKey, string name) => _inner.SeedUserNameAsync(userKey, name);
    public Task SetOwnIconAsync(byte[] data, string contentType) => _inner.SetOwnIconAsync(data, contentType);
    public Task ClearOwnIconAsync() => _inner.ClearOwnIconAsync();

    public async Task SetUserNameAsync(string userKey, string name)
    {
        await RequireSelfOrUsersManageAsync(userKey, nameof(SetUserNameAsync));
        await _inner.SetUserNameAsync(userKey, name);
    }

    // Administration — users:manage.
    public async IAsyncEnumerable<IUser> GetAsync()
    {
        await RequireUsersManageAsync(nameof(GetAsync));
        await foreach (var user in _inner.GetAsync())
        {
            yield return user;
        }
    }

    // Self-service projection — the caller's co-members, resolved from their own memberships.
    public async Task<IReadOnlyList<IUser>> GetTeamMemberUsersAsync()
    {
        if (!await _authorizer.IsAuthenticatedAsync())
            throw new UnauthorizedAccessException($"{nameof(GetTeamMemberUsersAsync)} requires an authenticated caller.");

        var current = await _inner.GetCurrentUserAsync();
        var visibleKeys = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(current?.Key)) visibleKeys.Add(current.Key);

        var teamService = _teamServiceFactory?.Invoke();
        if (teamService != null)
        {
            await foreach (var team in teamService.GetTeamsAsync())
            {
                await foreach (var member in teamService.GetMembersAsync(team.Key))
                {
                    if (!string.IsNullOrEmpty(member.Key)) visibleKeys.Add(member.Key);
                }
            }
        }

        var users = new List<IUser>();
        await foreach (var user in _inner.GetAsync())
        {
            if (visibleKeys.Contains(user.Key)) users.Add(user);
        }

        return users;
    }

    public async Task<IUser> GetUserByKeyAsync(string userKey)
    {
        await RequireUsersManageAsync(nameof(GetUserByKeyAsync));
        return await _inner.GetUserByKeyAsync(userKey);
    }

    public async Task SetUserLastSeenAsync(string userKey, DateTime lastSeen)
    {
        await RequireUsersManageAsync(nameof(SetUserLastSeenAsync));
        await _inner.SetUserLastSeenAsync(userKey, lastSeen);
    }

    public async Task SetUserDirectoryIdAsync(string userKey, string directoryId)
    {
        await RequireUsersManageAsync(nameof(SetUserDirectoryIdAsync));
        await _inner.SetUserDirectoryIdAsync(userKey, directoryId);
    }

    public async Task SetUserDisabledAsync(string userKey, DateTime? disabledAt, string disabledBy)
    {
        await RequireUsersManageAsync(nameof(SetUserDisabledAsync));
        await _inner.SetUserDisabledAsync(userKey, disabledAt, disabledBy);
    }

    public async Task SetUserEmailAsync(string userKey, string email)
    {
        await RequireUsersManageAsync(nameof(SetUserEmailAsync));
        await _inner.SetUserEmailAsync(userKey, email);
    }

    public async Task DeleteUserAsync(string userKey)
    {
        await RequireUsersManageAsync(nameof(DeleteUserAsync));
        await _inner.DeleteUserAsync(userKey);
    }

    public async Task SetUserIconAsync(string userKey, byte[] data, string contentType)
    {
        await RequireUsersManageAsync(nameof(SetUserIconAsync));
        await _inner.SetUserIconAsync(userKey, data, contentType);
    }

    public async Task ClearUserIconAsync(string userKey)
    {
        await RequireUsersManageAsync(nameof(ClearUserIconAsync));
        await _inner.ClearUserIconAsync(userKey);
    }

    private async Task RequireSelfOrUsersManageAsync(string userKey, string operation)
    {
        if (!string.IsNullOrEmpty(userKey))
        {
            var current = await _inner.GetCurrentUserAsync();
            if (current != null && current.Key == userKey) return;
        }

        await RequireUsersManageAsync(operation);
    }

    private async Task RequireUsersManageAsync(string operation)
    {
        if (await _authorizer.HasSystemScopeAsync(SystemUserScopes.Manage)) return;
        throw new UnauthorizedAccessException($"{operation} requires the '{SystemUserScopes.Manage}' system scope.");
    }
}
