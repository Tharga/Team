namespace Tharga.Team.Service.Tests;

public class TeamServiceBaseEventTests
{
    private readonly TestTeamService _sut;

    public TeamServiceBaseEventTests()
    {
        var userService = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns("user-1");
        user.EMail.Returns("test@example.com");
        userService.GetCurrentUserAsync().Returns(user);

        _sut = new TestTeamService(userService);
        _sut.AddTeam("team-1", "Test Team",
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.User, State = MembershipState.Member });
    }

    [Fact]
    public async Task AddMemberAsync_FiresTeamsListChangedEvent()
    {
        var fired = false;
        _sut.TeamsListChangedEvent += (_, _) => fired = true;

        await _sut.AddMemberAsync("team-1", new InviteUserModel { Email = "new@example.com" });

        Assert.True(fired);
    }

    [Fact]
    public async Task SetMemberRoleAsync_FiresTeamsListChangedEvent()
    {
        var fired = false;
        _sut.TeamsListChangedEvent += (_, _) => fired = true;

        await _sut.SetMemberRoleAsync("team-1", "user-2", AccessLevel.Administrator);

        Assert.True(fired);
    }

    [Fact]
    public async Task SetMemberTenantRolesAsync_FiresTeamsListChangedEvent()
    {
        var fired = false;
        _sut.TeamsListChangedEvent += (_, _) => fired = true;

        await _sut.SetMemberTenantRolesAsync("team-1", "user-2", new[] { "Editor" });

        Assert.True(fired);
    }

    [Fact]
    public async Task SetMemberScopeOverridesAsync_FiresTeamsListChangedEvent()
    {
        var fired = false;
        _sut.TeamsListChangedEvent += (_, _) => fired = true;

        await _sut.SetMemberScopeOverridesAsync("team-1", "user-2", new[] { "feature:read" });

        Assert.True(fired);
    }

    [Fact]
    public async Task SetInvitationResponseAsync_Reject_FiresTeamsListChangedEvent()
    {
        var fired = false;
        _sut.TeamsListChangedEvent += (_, _) => fired = true;

        await _sut.SetInvitationResponseAsync("team-1", "user-2", "invite-key", false);

        Assert.True(fired);
    }

    [Fact]
    public async Task SetTeamConsentAsync_FiresTeamsListChangedEvent()
    {
        var fired = false;
        _sut.TeamsListChangedEvent += (_, _) => fired = true;

        await _sut.SetTeamConsentAsync("team-1", new[] { "Developer" });

        Assert.True(fired);
    }

    [Fact]
    public async Task CreateTeamAsync_FiresTeamsListChangedEvent()
    {
        var fired = false;
        _sut.TeamsListChangedEvent += (_, _) => fired = true;

        await _sut.CreateTeamAsync("Test Team");

        Assert.True(fired);
    }

    [Fact]
    public async Task RemoveMemberAsync_FiresTeamsListChangedEvent()
    {
        var fired = false;
        _sut.TeamsListChangedEvent += (_, _) => fired = true;

        await _sut.RemoveMemberAsync("team-1", "user-2");

        Assert.True(fired);
    }

    [Fact]
    public async Task SetInvitationResponseAsync_Accept_FiresTeamsListChangedEvent()
    {
        var fired = false;
        _sut.TeamsListChangedEvent += (_, _) => fired = true;

        await _sut.SetInvitationResponseAsync("team-1", "user-2", "invite-key", true);

        Assert.True(fired);
    }

    [Fact]
    public async Task TransferOwnershipAsync_FiresTeamsListChangedEvent()
    {
        var fired = false;
        _sut.TeamsListChangedEvent += (_, _) => fired = true;

        await _sut.TransferOwnershipAsync<TestMember>("team-1", "user-2");

        Assert.True(fired);
    }

    /// <summary>
    /// Creating a team still selects it. The event is how a service that picks a team on the caller's
    /// behalf tells the UI, and creation is the case that genuinely has nobody else to decide
    /// (Tharga/Team#287).
    /// </summary>
    [Fact]
    public async Task CreateTeamAsync_FiresSelectTeamEvent()
    {
        var fired = false;
        _sut.SelectTeamEvent += (_, _) => fired = true;

        await _sut.CreateTeamAsync("Test Team");

        Assert.True(fired);
    }

    /// <summary>
    /// Accepting an invitation does not, because the screen that accepted it selects the team itself
    /// (Tharga/Team#287).
    /// </summary>
    /// <remarks>
    /// The event could not be awaited by whoever raised it — <c>TeamStateService</c> bridges it with an
    /// <c>async void</c> handler — so the selection it wrote raced the navigation the invitation screen
    /// was already performing, and the reload it triggered of its own was a second navigation for one
    /// click. Two writers for one piece of state is the defect; removing one of them is the fix.
    /// </remarks>
    [Fact]
    public async Task SetInvitationResponseAsync_Accept_DoesNotFireSelectTeamEvent()
    {
        var fired = false;
        _sut.SelectTeamEvent += (_, _) => fired = true;

        await _sut.SetInvitationResponseAsync("team-1", "user-2", "invite-key", true);

        Assert.False(fired);
    }
}
