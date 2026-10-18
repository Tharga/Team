using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// <see cref="TeamManagementService{TMember}.GetTeamsAsync{T}"/> filters the caller's own teams to those
/// where their membership grants <c>team:read</c>.
/// </summary>
/// <remarks>
/// This is the one read that cannot be gated by <c>[RequireScope]</c>: it names no team, and a principal
/// carries scope claims only for the *selected* team, so there is nothing in the claims to check the
/// others against. The scopes are recomputed per team from the membership instead — the same inputs the
/// claims builder uses.
/// </remarks>
public class TeamReadScopeFilterTests
{
    private const string UserKey = "user-1";

    /// <summary>
    /// The case the whole feature exists for. <c>Custom</c> grants no base scopes, so a member at that
    /// level holds only their explicit grants — and must not see a roster on the strength of membership
    /// alone.
    /// </summary>
    [Fact]
    public async Task AMemberWithoutTeamRead_DoesNotSeeTheTeam()
    {
        var service = Service(Team("team-a", AccessLevel.Custom));

        var teams = await service.GetTeamsAsync<TestMember>().ToArrayAsync();

        Assert.Empty(teams);
    }

    /// <summary>The no-op case must stay a no-op: <c>team:read</c> sits at Viewer, so members keep working.</summary>
    [Theory]
    [InlineData(AccessLevel.Viewer)]
    [InlineData(AccessLevel.User)]
    [InlineData(AccessLevel.Administrator)]
    [InlineData(AccessLevel.Owner)]
    public async Task AMemberWithTeamRead_SeesTheTeam(AccessLevel accessLevel)
    {
        var service = Service(Team("team-a", accessLevel));

        var teams = await service.GetTeamsAsync<TestMember>().ToArrayAsync();

        Assert.Single(teams);
    }

    /// <summary>
    /// Filtering is per team, not per caller — the same person can hold the scope in one team and not in
    /// another, which is exactly what a claims-based check could not express.
    /// </summary>
    [Fact]
    public async Task FilteringIsPerTeam()
    {
        var service = Service(
            Team("team-a", AccessLevel.Administrator),
            Team("team-b", AccessLevel.Custom));

        var teams = await service.GetTeamsAsync<TestMember>().ToArrayAsync();

        Assert.Equal(["team-a"], teams.Select(x => x.Key));
    }

    /// <summary>A scope override restores the read, since overrides are grants like any other.</summary>
    [Fact]
    public async Task ACustomMemberGrantedTheScopeExplicitly_SeesTheTeam()
    {
        var service = Service(Team("team-a", AccessLevel.Custom, overrides: [TeamScopes.Read]));

        var teams = await service.GetTeamsAsync<TestMember>().ToArrayAsync();

        Assert.Single(teams);
    }

    /// <summary>
    /// An app that never configured scopes must not start being refused. No registry means no scope model,
    /// so there is nothing to enforce.
    /// </summary>
    [Fact]
    public async Task WithNoScopeRegistry_NothingIsFiltered()
    {
        var inner = Substitute.For<ITeamService>();
        inner.GetTeamsAsync<TestMember>().Returns(Teams(Team("team-a", AccessLevel.Custom)));

        var service = new TeamManagementService<TestMember>(inner);

        var teams = await service.GetTeamsAsync<TestMember>().ToArrayAsync();

        Assert.Single(teams);
    }

    private static TeamManagementService<TestMember> Service(params TestTeam[] teams)
    {
        var inner = Substitute.For<ITeamService>();
        inner.GetTeamsAsync<TestMember>().Returns(Teams(teams));

        var user = Substitute.For<IUser>();
        user.Key.Returns(UserKey);
        var userService = Substitute.For<IUserService>();
        userService.GetCurrentUserAsync().Returns(Task.FromResult(user));

        var registry = new ScopeRegistry();
        registry.Register(TeamScopes.Read, AccessLevel.Viewer, "View team details and members.");

        return new TeamManagementService<TestMember>(inner, userService, registry);
    }

    private static async IAsyncEnumerable<ITeam<TestMember>> Teams(params TestTeam[] teams)
    {
        foreach (var team in teams) yield return team;
        await Task.CompletedTask;
    }

    private static TestTeam Team(string key, AccessLevel accessLevel, string[] overrides = null)
        => new()
        {
            Key = key,
            Name = key,
            Members = [new TestMember { Key = UserKey, AccessLevel = accessLevel, ScopeOverrides = overrides }]
        };

    public sealed record TestTeam : ITeam<TestMember>
    {
        public string Key { get; init; }
        public string Name { get; init; }
        public string Icon { get; init; }
        public TestMember[] Members { get; init; }
    }

    public sealed record TestMember : ITeamMember
    {
        public string Key { get; init; }
        public string Name { get; init; }
        public AccessLevel? AccessLevel { get; init; }
        public Invitation Invitation { get; init; }
        public MembershipState? State { get; init; }
        public DateTime? LastSeen { get; init; }
        public string[] TenantRoles { get; init; }
        public string[] ScopeOverrides { get; init; }
    }
}
