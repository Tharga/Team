using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>MongoDB adapter for <see cref="IUserMessageMemberDirectory"/>, over the team and user repositories.</summary>
/// <remarks>
/// <b>Reads the repositories, not the services</b>, because the services list only what the caller is a member of and a
/// staff message reaches teams its writer belongs to none of; the message service in front of this is what is checked.
/// A membership counts when it is accepted and not suspended, in a team the repository returns as not deleted, for a
/// user whose record exists and is not disabled.
/// </remarks>
internal sealed class MongoUserMessageMemberDirectory<TUserEntity, TTeamEntity, TMember>(
    IUserRepository<TUserEntity> users,
    ITeamRepository<TTeamEntity, TMember> teams) : IUserMessageMemberDirectory
    where TUserEntity : EntityBase, IUser
    where TTeamEntity : TeamEntityBase<TMember>
    where TMember : TeamMemberBase
{
    public async Task<UserMessageTeam[]> GetTeamsAsync(CancellationToken cancellationToken = default)
    {
        var found = new List<UserMessageTeam>();

        await foreach (var team in teams.GetAllTeamsAsync().WithCancellation(cancellationToken))
        {
            if (team.DeletedAt == null) found.Add(new UserMessageTeam { Key = team.Key, Name = team.Name });
        }

        return [.. found];
    }

    public async Task<UserMessageMember[]> GetActiveMembersAsync(string[] teamKeys, CancellationToken cancellationToken = default)
    {
        var found = new List<TTeamEntity>();

        foreach (var teamKey in (teamKeys ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
        {
            var team = await teams.GetAsync(teamKey);
            if (team != null) found.Add(team);
        }

        return await ActiveAsync(found);
    }

    public async Task<UserMessageMember[]> GetActiveMembersOfEveryTeamAsync(CancellationToken cancellationToken = default)
    {
        var found = new List<TTeamEntity>();

        await foreach (var team in teams.GetAllTeamsAsync().WithCancellation(cancellationToken)) found.Add(team);

        return await ActiveAsync(found);
    }

    private async Task<UserMessageMember[]> ActiveAsync(IEnumerable<TTeamEntity> found)
    {
        var memberships = Memberships(found).ToArray();
        var enabled = (await users.GetByKeysAsync([.. memberships.Select(x => x.Member.Key).Distinct(StringComparer.Ordinal)]))
            .Where(x => x.DisabledAt == null)
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        return [.. memberships
            .Where(x => enabled.ContainsKey(x.Member.Key))
            .Select(x => new UserMessageMember
            {
                TeamKey = x.Team.Key,
                UserKey = x.Member.Key,
                Name = !string.IsNullOrWhiteSpace(x.Member.Name) ? x.Member.Name : enabled[x.Member.Key].Name,
                AccessLevel = x.Member.AccessLevel,
                Roles = x.Member.TenantRoles ?? []
            })];
    }

    /// <summary>The accepted, unsuspended memberships of the teams that are not deleted.</summary>
    internal static IEnumerable<(TTeamEntity Team, TMember Member)> Memberships(IEnumerable<TTeamEntity> found)
        => found
            .Where(x => x != null && x.DeletedAt == null)
            .SelectMany(team => (team.Members ?? [])
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Key) && x.State == MembershipState.Member && x.SuspendedAt == null)
                .Select(member => (team, member)));
}
