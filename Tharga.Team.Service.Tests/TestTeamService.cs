namespace Tharga.Team.Service.Tests;

/// <summary>
/// Concrete TeamServiceBase for testing. Supports configurable team/member data.
/// </summary>
internal class TestTeamService : TeamServiceBase
{
    private readonly Dictionary<string, TestTeam> _teams = new();

    public TestTeamService(IUserService userService, ITeamCache cache = null, InvitationOptions invitationOptions = null)
        : base(userService, cache: cache, invitationOptions: invitationOptions) { }

    /// <summary>Counts calls so a test can tell "renewed the existing invitation" from "added a second one".</summary>
    public int AddTeamMemberCallCount { get; private set; }

    protected override Task<Invitation> GetInvitationInternalAsync(string teamKey, string inviteKey)
    {
        _teams.TryGetValue(teamKey, out var team);
        var member = team?.Members?.FirstOrDefault(x => x.Invitation != null && x.Invitation.InviteKey == inviteKey);
        return Task.FromResult(member?.Invitation);
    }

    protected override Task SetTeamMemberInvitationExpiryAsync(string teamKey, string inviteKey, DateTime? expiresAt)
    {
        _teams.TryGetValue(teamKey, out var team);
        if (team?.Members == null) return Task.CompletedTask;

        var index = Array.FindIndex(team.Members, x => x.Invitation != null && x.Invitation.InviteKey == inviteKey);
        if (index >= 0)
        {
            team.Members[index] = team.Members[index] with
            {
                Invitation = team.Members[index].Invitation with { ExpiresAt = expiresAt }
            };
        }

        return Task.CompletedTask;
    }

    public void AddTeam(string teamKey, string name, params TestMember[] members)
    {
        _teams[teamKey] = new TestTeam
        {
            Key = teamKey,
            Name = name,
            Members = members
        };
    }

    protected override IAsyncEnumerable<ITeam> GetTeamsAsync(IUser user) => _teams.Values.ToAsyncEnumerable<ITeam>();

    public int GetTeamCallCount { get; private set; }

    protected override Task<ITeam> GetTeamAsync(string teamKey)
    {
        GetTeamCallCount++;
        if (teamKey == null) return Task.FromResult<ITeam>(null);
        _teams.TryGetValue(teamKey, out var team);
        return Task.FromResult<ITeam>(team);
    }

    protected override Task<ITeam> CreateTeamAsync(string teamKey, string name, IUser user, string displayName = null)
    {
        var team = new TestTeam { Key = teamKey, Name = name, Members = [new TestMember { Key = user.Key, Name = displayName, AccessLevel = AccessLevel.Owner, State = MembershipState.Member }] };
        _teams[teamKey] = team;
        return Task.FromResult<ITeam>(team);
    }

    protected override Task SetTeamNameAsync(string teamKey, string name) => Task.CompletedTask;
    protected override Task DeleteTeamAsync(string teamKey) { _teams.Remove(teamKey); return Task.CompletedTask; }
    protected override Task AddTeamMemberAsync(string teamKey, InviteUserModel model)
    {
        AddTeamMemberCallCount++;
        return Task.CompletedTask;
    }
    /// <summary>
    /// Actually drops the member, so a test can assert <i>who</i> left rather than only that nothing threw.
    /// </summary>
    protected override Task RemoveTeamMemberAsync(string teamKey, string userKey)
    {
        if (_teams.TryGetValue(teamKey, out var team) && team.Members != null)
            _teams[teamKey] = team with { Members = team.Members.Where(x => x.Key != userKey).ToArray() };

        return Task.CompletedTask;
    }

    protected override Task<ITeam> SetTeamMemberInvitationResponseAsync(string teamKey, string userKey, string inviteKey, bool accept)
    {
        _teams.TryGetValue(teamKey, out var team);
        return Task.FromResult<ITeam>(team);
    }

    protected override Task<string> GetInvitedMemberNameAsync(string teamKey, string inviteKey)
    {
        if (!_teams.TryGetValue(teamKey, out var team)) return Task.FromResult<string>(null);
        var member = team.Members.FirstOrDefault(m => m.Invitation != null && m.Invitation.InviteKey == inviteKey);
        return Task.FromResult(member?.Name);
    }

    protected override Task SetTeamMemberLastSeenAsync(string teamKey, string userKey) => Task.CompletedTask;

    public int GetTeamMembersCallCount { get; private set; }

    protected override Task<ITeamMember> GetTeamMembersAsync(string teamKey, string userKey)
    {
        GetTeamMembersCallCount++;
        if (_teams.TryGetValue(teamKey, out var team))
        {
            var member = team.Members.FirstOrDefault(m => m.Key == userKey);
            return Task.FromResult<ITeamMember>(member);
        }
        return Task.FromResult<ITeamMember>(null);
    }

    protected override Task SetTeamMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel) => Task.CompletedTask;
    public readonly List<(string TeamKey, string UserKey, DateTime? SuspendedAt, string SuspendedBy)> SuspendCalls = [];

    /// <summary>Stands in for a host store that never overrode the hook, so the base throws.</summary>
    public bool SimulateNoSuspendHook { get; init; }

    protected override Task SetTeamMemberSuspendedAsync(string teamKey, string userKey, DateTime? suspendedAt, string suspendedBy)
    {
        if (SimulateNoSuspendHook) return base.SetTeamMemberSuspendedAsync(teamKey, userKey, suspendedAt, suspendedBy);

        SuspendCalls.Add((teamKey, userKey, suspendedAt, suspendedBy));
        return Task.CompletedTask;
    }

    protected override Task SetTeamMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles) => Task.CompletedTask;
    protected override Task SetTeamMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides) => Task.CompletedTask;

    public string LastSetMemberName_TeamKey;
    public string LastSetMemberName_UserKey;
    public string LastSetMemberName_Name;
    public int SetMemberNameCallCount;
    protected override Task SetTeamMemberNameAsync(string teamKey, string userKey, string name)
    {
        SetMemberNameCallCount++;
        LastSetMemberName_TeamKey = teamKey;
        LastSetMemberName_UserKey = userKey;
        LastSetMemberName_Name = name;
        return Task.CompletedTask;
    }

    protected override Task SetTeamConsentInternalAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel) => Task.CompletedTask;
    protected override IAsyncEnumerable<ITeam> GetConsentedTeamsInternalAsync(string[] userRoles) => AsyncEnumerable.Empty<ITeam>();

    protected override Task SetTeamCustomRolesInternalAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles)
    {
        if (_teams.TryGetValue(teamKey, out var team))
            _teams[teamKey] = team with { CustomRoles = customRoles };
        return Task.CompletedTask;
    }

    /// <summary>
    /// Puts custom roles into the store <b>without</b> going through <c>SetTeamCustomRolesAsync</c>, so a
    /// test can establish what the store holds without the write path invalidating the cache first.
    /// </summary>
    public void SeedCustomRoles(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles)
    {
        if (_teams.TryGetValue(teamKey, out var team))
            _teams[teamKey] = team with { CustomRoles = customRoles };
    }
}

internal record TestTeam : ITeam<TestMember>
{
    public string Key { get; init; }
    public string Name { get; init; }
    public string Icon { get; init; }
    public string[] ConsentedRoles { get; init; }
    public AccessLevel? ConsentAccessLevel { get; init; }
    public IReadOnlyList<TenantRoleDefinition> CustomRoles { get; init; }
    public TestMember[] Members { get; init; } = [];
}

internal record TestMember : ITeamMember
{
    public string Key { get; init; }
    public string Name { get; init; }
    public Invitation Invitation { get; init; }
    public DateTime? LastSeen { get; init; }
    public MembershipState? State { get; init; }
    public AccessLevel AccessLevel { get; init; }
    public string[] TenantRoles { get; init; } = [];
    public string[] ScopeOverrides { get; init; } = [];
    public DateTime? SuspendedAt { get; init; }
    public string SuspendedBy { get; init; }
}
