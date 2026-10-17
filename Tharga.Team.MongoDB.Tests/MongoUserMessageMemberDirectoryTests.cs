using TeamEntity = Tharga.Team.MongoDB.Tests.MongoSupportSenderDirectoryTests.TeamEntity;
using UserEntity = Tharga.Team.MongoDB.Tests.MongoSupportSenderDirectoryTests.UserEntity;
using Member = Tharga.Team.MongoDB.Tests.MongoSupportSenderDirectoryTests.Member;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Who a staff message can reach: only accepted, unsuspended memberships of teams that are not deleted, of users whose
/// record exists and is not disabled. Asserted against repositories that filter nothing, so the directory holds
/// whatever a host's repository returns.
/// </summary>
public class MongoUserMessageMemberDirectoryTests
{
    private static readonly DateTime Earlier = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IUserRepository<UserEntity> _users = Substitute.For<IUserRepository<UserEntity>>();
    private readonly ITeamRepository<TeamEntity, Member> _teams = Substitute.For<ITeamRepository<TeamEntity, Member>>();

    private MongoUserMessageMemberDirectory<UserEntity, TeamEntity, Member> Directory => new(_users, _teams);

    private void Users(params UserEntity[] users)
        => _users.GetByKeysAsync(Arg.Any<IReadOnlyCollection<string>>()).Returns(x => users.Where(u => ((IReadOnlyCollection<string>)x[0]).Contains(u.Key)).ToArray());

    private void Teams(params TeamEntity[] teams)
    {
        _teams.GetAllTeamsAsync().Returns(_ => teams.ToAsyncEnumerable());
        foreach (var team in teams) _teams.GetAsync(team.Key).Returns(team);
    }

    private static TeamEntity Team(string key, params Member[] members) => new() { Key = key, Name = key.ToUpperInvariant(), Members = members };

    private static Member Membership(string key, AccessLevel level = AccessLevel.User, MembershipState? state = MembershipState.Member, params string[] roles)
        => new() { Key = key, Name = $"{key} in team", State = state, AccessLevel = level, TenantRoles = roles };

    private static UserEntity User(string key, bool disabled = false)
        => new() { Key = key, Identity = $"sub-{key}", Name = $"{key} on record", DisabledAt = disabled ? Earlier : null };

    [Fact]
    public async Task AnActiveMembership_IsReached_WithItsLevelAndRoles()
    {
        Users(User("u-1"));
        Teams(Team("acme", Membership("u-1", AccessLevel.Administrator, MembershipState.Member, "Editor")));

        var member = Assert.Single(await Directory.GetActiveMembersAsync(["acme"], TestContext.Current.CancellationToken));

        Assert.Equal(("acme", "u-1", "u-1 in team", AccessLevel.Administrator), (member.TeamKey, member.UserKey, member.Name, member.AccessLevel));
        Assert.Equal(["Editor"], member.Roles);
    }

    [Theory]
    [InlineData(MembershipState.Invited)]
    [InlineData(MembershipState.Rejected)]
    [InlineData(null)]
    public async Task AMembershipNotAccepted_IsLeftOut(MembershipState? state)
    {
        Users(User("u-1"), User("u-2"));
        Teams(Team("acme", Membership("u-1"), Membership("u-2", state: state)));

        Assert.Equal(["u-1"], (await Directory.GetActiveMembersAsync(["acme"], TestContext.Current.CancellationToken)).Select(x => x.UserKey));
    }

    [Fact]
    public async Task ASuspendedMembership_IsLeftOut()
    {
        Users(User("u-1"), User("u-2"));
        Teams(Team("acme", Membership("u-1"), Membership("u-2") with { SuspendedAt = Earlier }));

        Assert.Equal(["u-1"], (await Directory.GetActiveMembersAsync(["acme"], TestContext.Current.CancellationToken)).Select(x => x.UserKey));
    }

    [Fact]
    public async Task ADisabledAccount_AndOneWithNoRecord_AreLeftOut()
    {
        Users(User("u-1"), User("u-2", disabled: true));
        Teams(Team("acme", Membership("u-1"), Membership("u-2"), Membership("u-3")));

        Assert.Equal(["u-1"], (await Directory.GetActiveMembersAsync(["acme"], TestContext.Current.CancellationToken)).Select(x => x.UserKey));
    }

    [Fact]
    public async Task ADeletedTeam_IsLeftOut_FromTheTeamsAndFromEveryTeamsMembers()
    {
        Users(User("u-1"), User("u-2"));
        Teams(Team("acme", Membership("u-1")), Team("gone", Membership("u-2")) with { DeletedAt = Earlier });

        Assert.Equal(["acme"], (await Directory.GetTeamsAsync(TestContext.Current.CancellationToken)).Select(x => x.Key));
        Assert.Equal(["u-1"], (await Directory.GetActiveMembersOfEveryTeamAsync(TestContext.Current.CancellationToken)).Select(x => x.UserKey));
        Assert.Empty(await Directory.GetActiveMembersAsync(["gone"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OnlyTheTeamsNamed_AreRead()
    {
        Users(User("u-1"), User("u-2"));
        Teams(Team("acme", Membership("u-1")), Team("beta", Membership("u-2")));

        Assert.Equal(["u-2"], (await Directory.GetActiveMembersAsync(["beta", "beta", "unknown"], TestContext.Current.CancellationToken)).Select(x => x.UserKey));
        await _teams.Received(1).GetAsync("beta");
    }

    [Fact]
    public async Task AMemberInSeveralTeams_IsReturnedOncePerTeam_AndTheUsersAreReadOnce()
    {
        Users(User("u-1"));
        Teams(Team("acme", Membership("u-1", AccessLevel.Owner)), Team("beta", Membership("u-1") with { Name = null }));

        var members = await Directory.GetActiveMembersOfEveryTeamAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["acme", "beta"], members.Select(x => x.TeamKey));
        Assert.Equal("u-1 on record", members[1].Name);
        await _users.Received(1).GetByKeysAsync(Arg.Any<IReadOnlyCollection<string>>());
    }

    [Fact]
    public async Task AHostRepository_FindsUsersByKey_OneAtATime()
    {
        IUserRepository<UserEntity> repository = Substitute.For<IUserRepository<UserEntity>>();
        repository.GetByKeyAsync("u-1").Returns(User("u-1"));

        var found = await ((IUserRepository<UserEntity>)new KeyOnlyRepository(repository)).GetByKeysAsync(["u-1", "u-1", "u-2", ""]);

        Assert.Equal(["u-1"], found.Select(x => x.Key));
    }

    private sealed class KeyOnlyRepository(IUserRepository<UserEntity> inner) : IUserRepository<UserEntity>
    {
        public IAsyncEnumerable<UserEntity> GetAsync() => inner.GetAsync();
        public Task<UserEntity> GetAsync(string identity) => inner.GetAsync(identity);
        public Task<UserEntity> GetByKeyAsync(string userKey) => inner.GetByKeyAsync(userKey);
        public Task AddAsync(UserEntity user) => inner.AddAsync(user);
        public Task SetNameAsync(string userKey, string name) => inner.SetNameAsync(userKey, name);
    }
}
