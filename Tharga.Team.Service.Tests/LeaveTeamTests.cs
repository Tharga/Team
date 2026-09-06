namespace Tharga.Team.Service.Tests;

public class LeaveTeamTests
{
    private readonly IUserService _userService;
    private readonly IUser _currentUser;

    public LeaveTeamTests()
    {
        _userService = Substitute.For<IUserService>();
        _currentUser = Substitute.For<IUser>();
        _currentUser.Key.Returns("user-1");
        _currentUser.EMail.Returns("owner@example.com");
        _userService.GetCurrentUserAsync().Returns(_currentUser);
    }

    [Fact]
    public async Task RegularUser_CanLeaveTeam()
    {
        _currentUser.Key.Returns("user-2");
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.User, State = MembershipState.Member });

        await sut.RemoveMemberAsync("team-1", "user-2");
        // No exception = success
    }

    [Fact]
    public async Task Owner_CannotLeaveTeam()
    {
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.User, State = MembershipState.Member });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RemoveMemberAsync("team-1", "user-1"));

        Assert.Contains("Transfer ownership", ex.Message);
    }

    [Fact]
    public async Task LastAdmin_CannotLeaveTeam()
    {
        _currentUser.Key.Returns("user-2");
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.Administrator, State = MembershipState.Member },
            new TestMember { Key = "user-3", AccessLevel = AccessLevel.User, State = MembershipState.Member });

        // user-2 is the only admin (besides the owner) — but owner counts as admin-or-above
        // So there IS another admin-or-above (the owner). This should succeed.
        await sut.RemoveMemberAsync("team-1", "user-2");
    }

    [Fact]
    public async Task Admin_CanLeaveWhenOtherAdminExists()
    {
        _currentUser.Key.Returns("user-2");
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.Administrator, State = MembershipState.Member },
            new TestMember { Key = "user-3", AccessLevel = AccessLevel.Administrator, State = MembershipState.Member });

        await sut.RemoveMemberAsync("team-1", "user-2");
        // No exception = success
    }

    [Fact]
    public async Task OwnerCanRemoveOtherMember()
    {
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.User, State = MembershipState.Member });

        // Owner removing another member (not self) should always work
        await sut.RemoveMemberAsync("team-1", "user-2");
    }

    [Fact]
    public async Task TransferOwnership_Success()
    {
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.User, State = MembershipState.Member });

        await sut.TransferOwnershipAsync<TestMember>("team-1", "user-2");
        // No exception = success
    }

    [Fact]
    public async Task TransferOwnership_NonOwner_Throws()
    {
        _currentUser.Key.Returns("user-2");
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.Administrator, State = MembershipState.Member });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.TransferOwnershipAsync<TestMember>("team-1", "user-1"));

        Assert.Contains("Only the current owner", ex.Message);
    }

    [Fact]
    public async Task TransferOwnership_ToSelf_Throws()
    {
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.User, State = MembershipState.Member });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.TransferOwnershipAsync<TestMember>("team-1", "user-1"));

        Assert.Contains("Cannot transfer ownership to yourself", ex.Message);
    }

    [Fact]
    public async Task TransferOwnership_ToNonMember_Throws()
    {
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.TransferOwnershipAsync<TestMember>("team-1", "user-99"));

        Assert.Contains("not a member", ex.Message);
    }

    // ---- Ownership may only change through TransferOwnership ----
    //
    // SetMemberRoleAsync is authorized on member:manage alone, so without these guards a team
    // administrator can promote themselves to Owner (bypassing the ownership check that
    // TransferOwnershipAsync performs) or demote the sitting Owner, leaving a team that cannot
    // transfer ownership at all because transfer requires the caller to be the owner.

    [Fact]
    public async Task SetMemberRole_CannotGrantOwner()
    {
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.Administrator, State = MembershipState.Member });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SetMemberRoleAsync("team-1", "user-2", AccessLevel.Owner));

        Assert.Contains("Transfer ownership", ex.Message);
    }

    [Fact]
    public async Task SetMemberRole_CannotDemoteTheOwner()
    {
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.Administrator, State = MembershipState.Member });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SetMemberRoleAsync("team-1", "user-1", AccessLevel.Administrator));

        Assert.Contains("Transfer ownership", ex.Message);
    }

    [Fact]
    public async Task SetMemberRole_BetweenNonOwnerLevels_IsAllowed()
    {
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.User, State = MembershipState.Member });

        await sut.SetMemberRoleAsync("team-1", "user-2", AccessLevel.Administrator);
    }

    [Fact]
    public async Task TransferOwnership_StillPromotesAndDemotes()
    {
        // Transfer goes through the protected storage method, so the guards above must not block it.
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.User, State = MembershipState.Member });

        await sut.TransferOwnershipAsync<TestMember>("team-1", "user-2");
    }

    // ---- LeaveTeamAsync: the self-service path ----
    //
    // Leaving used to be RemoveMemberAsync(teamKey, self), which the authorization decorator gates on
    // member:manage -- registered at Administrator, so an ordinary member could not leave the team they
    // were in. These assert the domain half; AuthorizationTeamServiceDecoratorTests asserts that the
    // operation is reachable with no scopes at all.

    [Fact]
    public async Task Leave_RegularUser_Succeeds()
    {
        _currentUser.Key.Returns("user-2");
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.User, State = MembershipState.Member });

        await sut.LeaveTeamAsync("team-1");

        var remaining = await sut.GetMembersAsync("team-1").ToArrayAsync();
        Assert.Equal(["user-1"], remaining.Select(x => x.Key));
    }

    [Fact]
    public async Task Leave_Viewer_Succeeds()
    {
        _currentUser.Key.Returns("user-2");
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.Viewer, State = MembershipState.Member });

        await sut.LeaveTeamAsync("team-1");

        var remaining = await sut.GetMembersAsync("team-1").ToArrayAsync();
        Assert.DoesNotContain(remaining, x => x.Key == "user-2");
    }

    [Fact]
    public async Task Leave_Owner_Throws()
    {
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.User, State = MembershipState.Member });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.LeaveTeamAsync("team-1"));

        Assert.Contains("Transfer ownership", ex.Message);
    }

    [Fact]
    public async Task Leave_AdministratorWithAnOwnerPresent_Succeeds()
    {
        _currentUser.Key.Returns("user-2");
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.Administrator, State = MembershipState.Member });

        await sut.LeaveTeamAsync("team-1");

        var remaining = await sut.GetMembersAsync("team-1").ToArrayAsync();
        Assert.Equal(["user-1"], remaining.Select(x => x.Key));
    }

    /// <summary>
    /// The one case the last-administrator guard actually bites: no owner, so nobody outranks the
    /// departing administrator. With an owner present there is always another admin-or-above.
    /// </summary>
    [Fact]
    public async Task Leave_LastAdministratorOfOwnerlessTeam_Throws()
    {
        _currentUser.Key.Returns("user-2");
        var sut = CreateService(
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.Administrator, State = MembershipState.Member },
            new TestMember { Key = "user-3", AccessLevel = AccessLevel.User, State = MembershipState.Member });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.LeaveTeamAsync("team-1"));

        Assert.Contains("last administrator", ex.Message);
    }

    /// <summary>
    /// Suspension is the state someone most wants out of, and letting them go strands nothing: the Owner
    /// cannot be suspended, so a suspended member is never the one whose departure orphans a team. Pinned
    /// here because nothing in the leave path consults suspension — this asserts that stays true.
    /// </summary>
    [Fact]
    public async Task Leave_SuspendedMember_Succeeds()
    {
        _currentUser.Key.Returns("user-2");
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember
            {
                Key = "user-2",
                AccessLevel = AccessLevel.User,
                State = MembershipState.Member,
                SuspendedAt = DateTime.UtcNow,
                SuspendedBy = "user-1"
            });

        await sut.LeaveTeamAsync("team-1");

        var remaining = await sut.GetMembersAsync("team-1").ToArrayAsync();
        Assert.Equal(["user-1"], remaining.Select(x => x.Key));
    }

    [Fact]
    public async Task Leave_NonMember_Throws()
    {
        _currentUser.Key.Returns("user-99");
        var sut = CreateService(
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.LeaveTeamAsync("team-1"));

        Assert.Contains("not a member", ex.Message);
    }

    /// <summary>
    /// A team whose roster cannot be read refuses rather than proceeding. An owner slipping past the
    /// guard strands a team only the teams:set-owner system scope can repair, so this fails closed.
    /// </summary>
    [Fact]
    public async Task Leave_UnknownTeam_Throws()
    {
        _currentUser.Key.Returns("user-2");
        var sut = CreateService(
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.User, State = MembershipState.Member });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.LeaveTeamAsync("no-such-team"));
    }

    private TestTeamService CreateService(params TestMember[] members)
    {
        var sut = new TestTeamService(_userService);
        sut.AddTeam("team-1", "Test Team", members);
        return sut;
    }
}
