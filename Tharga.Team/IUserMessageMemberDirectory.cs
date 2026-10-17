using System.ComponentModel;

namespace Tharga.Team;

/// <summary>Reads the teams and their active members a message or a notice may reach; internal and unchecked, so write messages through <see cref="IUserMessageService"/>, never by injecting this.</summary>
/// <remarks>
/// An active member has accepted, is not suspended and has a user record that is not disabled, in a team that is not
/// deleted. <c>Tharga.Team.MongoDB</c> registers one over the team and user repositories; a host with its own stores
/// registers its own.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUserMessageMemberDirectory
{
    /// <summary>Every team that is not deleted.</summary>
    Task<UserMessageTeam[]> GetTeamsAsync(CancellationToken cancellationToken = default);

    /// <summary>The active memberships of the teams named, one per member and team; a team that does not exist or is deleted has none.</summary>
    Task<UserMessageMember[]> GetActiveMembersAsync(string[] teamKeys, CancellationToken cancellationToken = default);

    /// <summary>The active memberships of every team, one per member and team.</summary>
    Task<UserMessageMember[]> GetActiveMembersOfEveryTeamAsync(CancellationToken cancellationToken = default);

    /// <summary>The active memberships of one user, one per team; by default read from every team's, which a host's own directory may answer more directly.</summary>
    async Task<UserMessageMember[]> GetActiveMembershipsOfUserAsync(string userKey, CancellationToken cancellationToken = default)
        => [.. (await GetActiveMembersOfEveryTeamAsync(cancellationToken) ?? []).Where(x => x != null && string.Equals(x.UserKey, userKey, StringComparison.Ordinal))];

    /// <summary>The tenant roles and access levels the members of every team hold; by default read from every team's active memberships, which a host's own directory may answer without reading its users.</summary>
    async Task<UserMessageRolesAndLevels> GetRolesAndLevelsAsync(CancellationToken cancellationToken = default)
    {
        var memberships = (await GetActiveMembersOfEveryTeamAsync(cancellationToken) ?? []).Where(x => x != null).ToArray();

        return new UserMessageRolesAndLevels
        {
            Roles = [.. memberships.SelectMany(x => x.Roles ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            AccessLevels = [.. memberships.Select(x => x.AccessLevel).Distinct().Order()]
        };
    }

    /// <summary>The active memberships, in the teams named or in every team when <paramref name="teamKeys"/> is null, of at most <paramref name="limit"/> users whose name or address holds <paramref name="text"/>, ignoring case.</summary>
    /// <remarks>By default read from the teams' active memberships, which a host's own directory may answer by searching its users first.</remarks>
    async Task<UserMessageMember[]> SearchActiveMembersAsync(string text, string[] teamKeys, int limit, CancellationToken cancellationToken = default)
    {
        var wanted = text?.Trim();
        if (string.IsNullOrEmpty(wanted) || limit < 1 || teamKeys is { Length: 0 }) return [];

        var memberships = teamKeys == null
            ? await GetActiveMembersOfEveryTeamAsync(cancellationToken)
            : await GetActiveMembersAsync(teamKeys, cancellationToken);

        return [.. (memberships ?? [])
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.UserKey))
            .GroupBy(x => x.UserKey, StringComparer.Ordinal)
            .Where(x => x.Any(y => Holds(y.Name, wanted) || Holds(y.EMail, wanted)))
            .Take(limit)
            .SelectMany(x => x)];

        static bool Holds(string value, string part) => value?.Contains(part, StringComparison.OrdinalIgnoreCase) == true;
    }
}

/// <summary>The tenant roles and access levels members hold, which a message may be written to.</summary>
public sealed record UserMessageRolesAndLevels
{
    /// <summary>The tenant roles, distinct and in order.</summary>
    public string[] Roles { get; init; } = [];

    /// <summary>The access levels, distinct and in order.</summary>
    public AccessLevel[] AccessLevels { get; init; } = [];
}

/// <summary>One active membership a message may reach.</summary>
public sealed record UserMessageMember
{
    /// <summary>The team's key.</summary>
    public required string TeamKey { get; init; }

    /// <summary>The user's key.</summary>
    public required string UserKey { get; init; }

    /// <summary>The member's name, or null.</summary>
    public string Name { get; init; }

    /// <summary>The address on the user's record, or null; filled where a directory reads it.</summary>
    public string EMail { get; init; }

    /// <summary>The member's access level in the team.</summary>
    public AccessLevel AccessLevel { get; init; }

    /// <summary>The tenant roles the member holds in the team.</summary>
    public string[] Roles { get; init; } = [];

    /// <summary>The scopes granted to the member in the team beyond their access level and roles.</summary>
    public string[] ScopeOverrides { get; init; } = [];
}
