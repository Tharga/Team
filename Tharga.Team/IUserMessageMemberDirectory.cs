using System.ComponentModel;

namespace Tharga.Team;

/// <summary>Reads the teams and their active members a message may reach; internal and unchecked, so write messages through <see cref="IUserMessageService"/>, never by injecting this.</summary>
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

    /// <summary>The member's access level in the team.</summary>
    public AccessLevel AccessLevel { get; init; }

    /// <summary>The tenant roles the member holds in the team.</summary>
    public string[] Roles { get; init; } = [];
}
