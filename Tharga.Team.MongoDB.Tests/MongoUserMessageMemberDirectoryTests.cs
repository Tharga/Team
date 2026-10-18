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

    private static Member Membership(string key, AccessLevel? level = AccessLevel.User, MembershipState? state = MembershipState.Member, params string[] roles)
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

    private void Found(string text, params UserEntity[] users) => _users.SearchAsync(text).Returns(_ => users.ToAsyncEnumerable());

    private void TeamsOfUsers(params TeamEntity[] teams)
        => _teams.GetTeamsByUsersAsync(Arg.Any<IReadOnlyCollection<string>>())
            .Returns(x => teams.Where(t => (t.Members ?? []).Any(m => ((IReadOnlyCollection<string>)x[0]).Contains(m.Key))).ToArray());

    private static UserEntity Person(string key, string name, string email = null, bool disabled = false)
        => new() { Key = key, Identity = $"sub-{key}", Name = name, EMail = email, DisabledAt = disabled ? Earlier : null };

    [Fact]
    public async Task ASearch_ReadsTheMatchingUsersFirst_ThenOnlyTheirTeams_AndReturnsTheirActiveMemberships_WithTheAddress()
    {
        Found("ann", Person("u-1", "Anna", " anna@example.org "), Person("u-2", "Annika"));
        TeamsOfUsers(Team("acme", Membership("u-1"), Membership("u-9")), Team("beta", Membership("u-1", AccessLevel.Owner)));

        var found = await Directory.SearchActiveMembersAsync("ann", null, 20, TestContext.Current.CancellationToken);

        Assert.Equal([("acme", "u-1"), ("beta", "u-1")], found.Select(x => (x.TeamKey, x.UserKey)));
        Assert.All(found, x => Assert.Equal("anna@example.org", x.EMail));
        Assert.Equal("u-1 in team", found[0].Name);
        await _teams.Received(1).GetTeamsByUsersAsync(Arg.Is<IReadOnlyCollection<string>>(x => x.Order().SequenceEqual(new[] { "u-1", "u-2" })));
        _teams.DidNotReceive().GetAllTeamsAsync();
        await _users.DidNotReceiveWithAnyArgs().GetByKeysAsync(default);
    }

    [Fact]
    public async Task ASearch_KeepsOnlyTheTeamsAsked()
    {
        Found("ann", Person("u-1", "Anna"), Person("u-2", "Annika"));
        TeamsOfUsers(Team("acme", Membership("u-1")), Team("beta", Membership("u-1"), Membership("u-2")));

        var found = await Directory.SearchActiveMembersAsync("ann", ["acme"], 20, TestContext.Current.CancellationToken);

        Assert.Equal([("acme", "u-1")], found.Select(x => (x.TeamKey, x.UserKey)));
    }

    [Fact]
    public async Task ASearch_LeavesOutDisabledUsers_AndMembershipsNotAcceptedOrSuspended_OrOfDeletedTeams()
    {
        Found("a", Person("u-1", "Anna", disabled: true), Person("u-2", "Alva"), Person("u-3", "Ada"), Person("u-4", "Asa"));
        TeamsOfUsers(
            Team("acme", Membership("u-1"), Membership("u-2", state: MembershipState.Invited), Membership("u-3") with { SuspendedAt = Earlier }),
            Team("gone", Membership("u-4")) with { DeletedAt = Earlier });

        Assert.Empty(await Directory.SearchActiveMembersAsync("a", null, 20, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASearch_StopsReadingUsers_OnceItHasEnough()
    {
        var read = 0;
        _users.SearchAsync("person").Returns(_ => Counted(Enumerable.Range(0, 500).Select(i => Person($"u-{i:D3}", $"Person {i}")), () => read++));
        _teams.GetTeamsByUsersAsync(Arg.Any<IReadOnlyCollection<string>>())
            .Returns(x => [Team("acme", [.. ((IReadOnlyCollection<string>)x[0]).Select(k => Membership(k))])]);

        var found = await Directory.SearchActiveMembersAsync("person", null, 3, TestContext.Current.CancellationToken);

        Assert.Equal(3, found.Select(x => x.UserKey).Distinct().Count());
        Assert.True(read <= MongoUserMessageMemberDirectory<UserEntity, TeamEntity, Member>.SearchChunkSize, $"{read} users were read for three.");
        await _teams.Received(1).GetTeamsByUsersAsync(Arg.Any<IReadOnlyCollection<string>>());
    }

    [Fact]
    public async Task ASearch_GivesUp_AfterTheMostUsersItReads()
    {
        var read = 0;
        _users.SearchAsync("person").Returns(_ => Counted(Enumerable.Range(0, 5000).Select(i => Person($"u-{i:D4}", $"Person {i}")), () => read++));
        _teams.GetTeamsByUsersAsync(Arg.Any<IReadOnlyCollection<string>>()).Returns([]);

        var found = await Directory.SearchActiveMembersAsync("person", ["acme"], 20, TestContext.Current.CancellationToken);

        Assert.Empty(found);
        Assert.Equal(MongoUserMessageMemberDirectory<UserEntity, TeamEntity, Member>.MaxUsersScanned, read);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    [InlineData("ann", new string[0])]
    public async Task ASearch_ForNothing_OrInNoTeam_ReadsNothing(string text, string[] teamKeys)
    {
        Assert.Empty(await Directory.SearchActiveMembersAsync(text, teamKeys, 20, TestContext.Current.CancellationToken));
        _users.DidNotReceiveWithAnyArgs().SearchAsync(default);
    }

    [Fact]
    public async Task UsersAddedByKey_AreReadThroughTheirOwnTeams_ActiveMembershipsOnly()
    {
        Users(User("u-1"), User("u-2", disabled: true), User("u-9"));
        TeamsOfUsers(
            Team("acme", Membership("u-1", AccessLevel.Owner), Membership("u-2"), Membership("u-9")),
            Team("beta", Membership("u-1", state: MembershipState.Invited)),
            Team("gamma", Membership("u-1") with { SuspendedAt = Earlier }),
            Team("delta", Membership("u-1", null)));

        var found = await Directory.GetActiveMembershipsOfUsersAsync(["u-1", "u-2", "u-1", " "], TestContext.Current.CancellationToken);

        Assert.Equal([("acme", "u-1", (AccessLevel?)AccessLevel.Owner), ("delta", "u-1", null)], found.Select(x => (x.TeamKey, x.UserKey, x.AccessLevel)));
        await _teams.Received(1).GetTeamsByUsersAsync(Arg.Is<IReadOnlyCollection<string>>(x => x.Order().SequenceEqual(new[] { "u-1", "u-2" })));
        _teams.DidNotReceive().GetAllTeamsAsync();
    }

    [Fact]
    public async Task NoUserAddedByKey_ReadsNothing()
    {
        Assert.Empty(await Directory.GetActiveMembershipsOfUsersAsync([], TestContext.Current.CancellationToken));
        await _teams.DidNotReceiveWithAnyArgs().GetTeamsByUsersAsync(default);
    }

    [Fact]
    public async Task TheRolesAndLevels_AreReadFromTheTeamsAlone()
    {
        Teams(
            Team("acme", Membership("u-1", AccessLevel.Owner, MembershipState.Member, "Editor"), Membership("u-2", AccessLevel.Viewer, MembershipState.Invited, "Ghost")),
            Team("beta", Membership("u-3", AccessLevel.User, MembershipState.Member, "Auditor", "Editor")),
            Team("gone", Membership("u-4", AccessLevel.Administrator, MembershipState.Member, "Old")) with { DeletedAt = Earlier });

        var held = await Directory.GetRolesAndLevelsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Auditor", "Editor"], held.Roles);
        Assert.Equal([AccessLevel.Owner, AccessLevel.User], held.AccessLevels);
        await _users.DidNotReceiveWithAnyArgs().GetByKeysAsync(default);
    }

    [Fact]
    public async Task AMemberWithNoAccessLevel_IsActive_WithNoLevel_AndOffersNoLevelOrRoleOfTheirOwn()
    {
        Users(User("u-1"), User("u-2"));
        Teams(Team("acme", Membership("u-1", AccessLevel.User, MembershipState.Member, "Editor"), Membership("u-2", null, MembershipState.Member, "Ghost")));

        var members = await Directory.GetActiveMembersAsync(["acme"], TestContext.Current.CancellationToken);
        var held = await Directory.GetRolesAndLevelsAsync(TestContext.Current.CancellationToken);

        Assert.Equal([("u-1", (AccessLevel?)AccessLevel.User), ("u-2", null)], members.Select(x => (x.UserKey, x.AccessLevel)));
        Assert.Equal(["Editor"], held.Roles);
        Assert.Equal([AccessLevel.User], held.AccessLevels);
    }

    [Fact]
    public async Task AHostRepository_SearchesUsersByNameOrAddress_IgnoringCase_AndLeavesOutTheDisabled()
    {
        var repository = Substitute.For<IUserRepository<UserEntity>>();
        repository.GetAsync().Returns(_ => new[]
        {
            Person("u-1", "Anna Andersson"),
            Person("u-2", "Bo", "BO.ANNERSTEN@example.org"),
            Person("u-3", "Annika", disabled: true),
            Person("u-4", "Cia")
        }.ToAsyncEnumerable());

        var found = await ((IUserRepository<UserEntity>)new KeyOnlyRepository(repository)).SearchAsync(" ann ").ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["u-1", "u-2"], found.Select(x => x.Key));
        Assert.Empty(await ((IUserRepository<UserEntity>)new KeyOnlyRepository(repository)).SearchAsync(" ").ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheBuiltInRepository_SearchesNameAndAddress_WithAnEscapedCaseInsensitivePattern()
    {
        var rendered = UserRepository<UserEntity>.SearchFilter("a.b+c").Render(new global::MongoDB.Driver.RenderArgs<UserEntity>(
            global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<UserEntity>(), global::MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry));

        var parts = rendered["$or"].AsBsonArray.Select(x => x.AsBsonDocument).ToArray();
        var expected = new global::MongoDB.Bson.BsonRegularExpression(@"a\.b\+c", "i");
        Assert.Equal(expected, parts[0]["Name"].AsBsonRegularExpression);
        Assert.Equal(expected, parts[1]["EMail"].AsBsonRegularExpression);
    }

    [Fact]
    public void TheBuiltInRepository_FindsTheLiveTeamsOfManyUsers_InOneFilter()
    {
        var rendered = TeamRepository<TeamEntity, Member>.MembersOf(["u-1", "u-2"]).Render(new global::MongoDB.Driver.RenderArgs<TeamEntity>(
            global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<TeamEntity>(), global::MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry)).ToString();

        Assert.Contains("\"DeletedAt\" : null", rendered);
        Assert.Contains("$elemMatch", rendered);
        Assert.Contains("\"Key\" : { \"$in\" : [\"u-1\", \"u-2\"] }", rendered);
    }

    private static async IAsyncEnumerable<UserEntity> Counted(IEnumerable<UserEntity> users, Action onRead)
    {
        foreach (var user in users)
        {
            onRead();
            yield return user;
            await Task.Yield();
        }
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
