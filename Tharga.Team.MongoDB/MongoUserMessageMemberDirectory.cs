using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>MongoDB adapter for <see cref="IUserMessageMemberDirectory"/>, over the team and user repositories, for staff messages and automatic notices.</summary>
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
    internal const int SearchChunkSize = 100;
    internal const int MaxUsersScanned = 1000;

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

    /// <remarks>Reads only the teams the user is in, and only the user's own record.</remarks>
    public async Task<UserMessageMember[]> GetActiveMembershipsOfUserAsync(string userKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userKey)) return [];

        var found = new List<TTeamEntity>();
        await foreach (var team in teams.GetTeamsByUserAsync(userKey).WithCancellation(cancellationToken)) found.Add(team);

        return await ActiveAsync(found, userKey);
    }

    /// <remarks>Reads the teams and nothing else: no user is read, so a role held only by a disabled user may be offered and reach nobody.</remarks>
    public async Task<UserMessageRolesAndLevels> GetRolesAndLevelsAsync(CancellationToken cancellationToken = default)
    {
        var found = new List<TTeamEntity>();
        await foreach (var team in teams.GetAllTeamsAsync().WithCancellation(cancellationToken)) found.Add(team);

        var memberships = Memberships(found).Select(x => x.Member).Where(x => x.AccessLevel != null).ToArray();

        return new UserMessageRolesAndLevels
        {
            Roles = [.. memberships.SelectMany(x => x.TenantRoles ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            AccessLevels = [.. memberships.Select(x => x.AccessLevel.Value).Distinct().Order()]
        };
    }

    /// <remarks>
    /// <b>Users first, then their teams</b>: the user records matching the text are read as a stream a chunk at a time,
    /// and only those chunks' teams are read, so a search reads what it needs rather than every team and every user.
    /// It stops at the limit, or after <see cref="MaxUsersScanned"/> matching users when few of them belong to the
    /// teams searched.
    /// </remarks>
    public async Task<UserMessageMember[]> SearchActiveMembersAsync(string text, string[] teamKeys, int limit, CancellationToken cancellationToken = default)
    {
        var wanted = text?.Trim();
        var within = teamKeys?.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(wanted) || limit < 1 || within is { Count: 0 }) return [];

        var found = new List<UserMessageMember>();
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var chunk = new List<TUserEntity>();
        var scanned = 0;

        await foreach (var user in users.SearchAsync(wanted).WithCancellation(cancellationToken))
        {
            if (user.DisabledAt != null || string.IsNullOrWhiteSpace(user.Key)) continue;

            chunk.Add(user);
            scanned++;
            if (chunk.Count < SearchChunkSize && scanned < MaxUsersScanned) continue;

            await AddMembershipsAsync(chunk, within, limit, found, reached);
            chunk.Clear();
            if (reached.Count >= limit || scanned >= MaxUsersScanned) break;
        }

        if (chunk.Count > 0 && reached.Count < limit) await AddMembershipsAsync(chunk, within, limit, found, reached);

        return [.. found];
    }

    private async Task AddMembershipsAsync(List<TUserEntity> chunk, HashSet<string> within, int limit, List<UserMessageMember> found, HashSet<string> reached)
    {
        var byKey = chunk.GroupBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var memberships = Memberships(await teams.GetTeamsByUsersAsync([.. byKey.Keys]) ?? [])
            .Where(x => byKey.ContainsKey(x.Member.Key) && (within == null || within.Contains(x.Team.Key)))
            .ToLookup(x => x.Member.Key, StringComparer.Ordinal);

        foreach (var user in byKey.Values)
        {
            if (reached.Count >= limit) return;
            if (!memberships.Contains(user.Key) || !reached.Add(user.Key)) continue;

            found.AddRange(memberships[user.Key].Select(x => ToMember(x.Team, x.Member, user)));
        }
    }

    private async Task<UserMessageMember[]> ActiveAsync(IEnumerable<TTeamEntity> found, string onlyUserKey = null)
    {
        var memberships = Memberships(found).Where(x => onlyUserKey == null || string.Equals(x.Member.Key, onlyUserKey, StringComparison.Ordinal)).ToArray();
        var enabled = (await users.GetByKeysAsync([.. memberships.Select(x => x.Member.Key).Distinct(StringComparer.Ordinal)]))
            .Where(x => x.DisabledAt == null)
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        return [.. memberships
            .Where(x => enabled.ContainsKey(x.Member.Key))
            .Select(x => ToMember(x.Team, x.Member, enabled[x.Member.Key]))];
    }

    private static UserMessageMember ToMember(TTeamEntity team, TMember member, TUserEntity user) => new()
    {
        TeamKey = team.Key,
        UserKey = member.Key,
        Name = !string.IsNullOrWhiteSpace(member.Name) ? member.Name : user.Name,
        EMail = string.IsNullOrWhiteSpace(user.EMail) ? null : user.EMail.Trim(),
        AccessLevel = member.AccessLevel,
        Roles = member.TenantRoles ?? [],
        ScopeOverrides = member.ScopeOverrides ?? []
    };

    /// <summary>The accepted, unsuspended memberships of the teams that are not deleted.</summary>
    internal static IEnumerable<(TTeamEntity Team, TMember Member)> Memberships(IEnumerable<TTeamEntity> found)
        => found
            .Where(x => x != null && x.DeletedAt == null)
            .SelectMany(team => (team.Members ?? [])
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Key) && x.State == MembershipState.Member && x.SuspendedAt == null)
                .Select(member => (team, member)));
}
