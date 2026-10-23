namespace Tharga.Team.Service.Tests;

/// <summary>
/// The owner rules hold when two member actions overlap (Tharga/Team#342): the rule is checked by the store as part
/// of the write, so a change that lands between the roster read and the write makes the write refuse, and the call
/// fails with the message of the guard that would have stopped it.
/// </summary>
/// <remarks>
/// <see cref="RacingTeamService"/> plays a store whose guarded writes check the rule against the roster as it stands
/// when they run, and lets a test change that roster after the service has read it and before the write — the window.
/// </remarks>
public class OwnerRaceGuardTests
{
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IUser _currentUser = Substitute.For<IUser>();

    public OwnerRaceGuardTests()
    {
        _currentUser.Key.Returns("owner");
        _userService.GetCurrentUserAsync().Returns(_currentUser);
    }

    private static TestMember Member(string key, AccessLevel level) => new() { Key = key, AccessLevel = level, State = MembershipState.Member };

    private RacingTeamService Team(params TestMember[] members)
    {
        var sut = new RacingTeamService(_userService);
        sut.AddTeam("team-1", "Team", members);
        return sut;
    }

    [Fact]
    public async Task ADemotion_ThatLosesTheRaceToATransfer_IsRefused_AsTheOwnerGuard()
    {
        var sut = Team(Member("owner", AccessLevel.Owner), Member("x", AccessLevel.Administrator));
        sut.Race = () => sut.AddTeam("team-1", "Team", Member("owner", AccessLevel.Administrator), Member("x", AccessLevel.Owner));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SetMemberRoleAsync("team-1", "x", AccessLevel.User));

        Assert.Contains("Transfer ownership", ex.Message);
    }

    [Fact]
    public async Task ARemoval_ThatLosesTheRaceToATransfer_IsRefused_AsTheOwnerGuard()
    {
        var sut = Team(Member("owner", AccessLevel.Owner), Member("x", AccessLevel.Administrator));
        sut.Race = () => sut.AddTeam("team-1", "Team", Member("owner", AccessLevel.Administrator), Member("x", AccessLevel.Owner));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RemoveMemberAsync("team-1", "x"));

        Assert.Contains("Transfer ownership", ex.Message);
        Assert.Equal(0, sut.RemoveTeamMemberCallCount);
    }

    [Fact]
    public async Task ALeave_ThatLosesTheRaceToTheOtherAdministratorLeaving_IsRefused_AsTheLastAdministratorGuard()
    {
        _currentUser.Key.Returns("a");
        var sut = Team(Member("a", AccessLevel.Administrator), Member("b", AccessLevel.Administrator), Member("u", AccessLevel.User));
        sut.Race = () => sut.AddTeam("team-1", "Team", Member("a", AccessLevel.Administrator), Member("u", AccessLevel.User));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.LeaveTeamAsync("team-1"));

        Assert.Contains("last administrator", ex.Message);
        Assert.Equal(0, sut.RemoveTeamMemberCallCount);
    }

    [Fact]
    public async Task ATransfer_ThatLosesTheRaceToAnotherTransfer_IsRefused_AsTheOwnerGuard()
    {
        var sut = Team(Member("owner", AccessLevel.Owner), Member("x", AccessLevel.User), Member("y", AccessLevel.User));
        sut.Race = () => sut.AddTeam("team-1", "Team", Member("owner", AccessLevel.Administrator), Member("x", AccessLevel.User), Member("y", AccessLevel.Owner));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.TransferOwnershipAsync<TestMember>("team-1", "x"));

        Assert.Contains("Only the current owner", ex.Message);
    }

    [Fact]
    public async Task ATransfer_ToAMemberRemovedMeanwhile_IsRefused_AsNotAMember()
    {
        var sut = Team(Member("owner", AccessLevel.Owner), Member("x", AccessLevel.User));
        sut.Race = () => sut.AddTeam("team-1", "Team", Member("owner", AccessLevel.Owner));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.TransferOwnershipAsync<TestMember>("team-1", "x"));

        Assert.Contains("not a member", ex.Message);
    }

    [Fact]
    public async Task ARemoval_OfAMemberWhoLeftMeanwhile_IsNotAnError()
    {
        var sut = Team(Member("owner", AccessLevel.Owner), Member("x", AccessLevel.User));
        sut.Race = () => sut.AddTeam("team-1", "Team", Member("owner", AccessLevel.Owner));

        await sut.RemoveMemberAsync("team-1", "x");
    }

    [Fact]
    public async Task WithoutARace_TheGuardedWritesApplyAsBefore()
    {
        var sut = Team(Member("owner", AccessLevel.Owner), Member("x", AccessLevel.Administrator), Member("y", AccessLevel.User));

        await sut.SetMemberRoleAsync("team-1", "y", AccessLevel.Viewer);
        await sut.RemoveMemberAsync("team-1", "y");
        await sut.TransferOwnershipAsync<TestMember>("team-1", "x");

        Assert.Equal(1, sut.RemoveTeamMemberCallCount);
    }

    private sealed class RacingTeamService(IUserService userService) : TestTeamService(userService)
    {
        public Action Race { get; set; }

        private async Task<TestMember[]> RosterAtWriteAsync(string teamKey)
        {
            var race = Race;
            Race = null;
            race?.Invoke();

            return (await GetTeamAsync(teamKey) as TestTeam)?.Members ?? [];
        }

        protected override async Task<bool> SetTeamMemberRoleUnlessOwnerAsync(string teamKey, string userKey, AccessLevel accessLevel)
        {
            var target = (await RosterAtWriteAsync(teamKey)).FirstOrDefault(x => x.Key == userKey);
            return target != null && target.AccessLevel != AccessLevel.Owner;
        }

        protected override async Task<bool> RemoveTeamMemberGuardedAsync(string teamKey, string userKey, bool requireOtherAdministrator)
        {
            var roster = await RosterAtWriteAsync(teamKey);
            var target = roster.FirstOrDefault(x => x.Key == userKey);
            if (target == null || target.AccessLevel == AccessLevel.Owner) return false;

            var othersLeft = roster.Any(x => x.Key != userKey && x.State == MembershipState.Member && x.SuspendedAt == null && x.AccessLevel <= AccessLevel.Administrator);
            if (requireOtherAdministrator && !othersLeft) return false;

            await RemoveTeamMemberAsync(teamKey, userKey);
            return true;
        }

        protected override async Task<bool> TransferTeamOwnershipAsync(string teamKey, string currentOwnerKey, string newOwnerUserKey)
        {
            var roster = await RosterAtWriteAsync(teamKey);
            return roster.Any(x => x.Key == currentOwnerKey && x.AccessLevel == AccessLevel.Owner) && roster.Any(x => x.Key == newOwnerUserKey);
        }
    }
}
