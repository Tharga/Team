namespace Tharga.Team.Service.Tests;

/// <summary>
/// The rules a request for team access must satisfy, and what approving one does to the team's consent.
/// </summary>
/// <remarks>
/// Who may request, approve or deny is the authorization decorator's job (<c>AccessRequestAuthorizationTests</c>).
/// These are the rules of the operations themselves, enforced in <see cref="TeamServiceBase"/> so a host that wires
/// the service without the decorator still cannot approve its own request or request a level nobody may hold.
/// </remarks>
public class TeamAccessRequestTests
{
    private const string TeamKey = "team-1";
    private const string Requester = "dev-1";
    private const string Manager = "owner-1";

    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IUser _user = Substitute.For<IUser>();

    public TeamAccessRequestTests()
    {
        As(Requester);
        _userService.GetCurrentUserAsync().Returns(_user);
    }

    private void As(string userKey)
    {
        _user.Key.Returns(userKey);
        _user.Name.Returns($"Name of {userKey}");
    }

    private TestTeamService Service(bool noHooks = false)
    {
        var service = new TestTeamService(_userService) { SimulateNoAccessRequestHooks = noHooks };
        service.AddTeam(TeamKey, "Team", new TestMember { Key = Manager, AccessLevel = AccessLevel.Owner, State = MembershipState.Member });
        return service;
    }

    private static TeamAccessRequest Single(TestTeamService service) => Assert.Single(service.Team(TeamKey).AccessRequests);

    // --- requesting ---

    [Fact]
    public async Task ARequest_IsStoredPending_WithWhatWasAsked()
    {
        var service = Service();

        var returned = await service.RequestTeamAccessAsync(TeamKey, AccessLevel.User, TimeSpan.FromHours(8), "  Investigating a sync failure  ");

        var stored = Single(service);
        Assert.Equal(returned.Id, stored.Id);
        Assert.Equal(TeamAccessRequestStatus.Pending, stored.Status);
        Assert.Equal(Requester, stored.RequesterKey);
        Assert.Equal(AccessLevel.User, stored.AccessLevel);
        Assert.Equal(TimeSpan.FromHours(8), stored.Duration);
        Assert.Equal("Investigating a sync failure", stored.Message);
    }

    [Fact]
    public async Task ARequest_MayAskForNoEnd()
    {
        var service = Service();

        await service.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, duration: null, message: null);

        Assert.Null(Single(service).Duration);
    }

    [Theory]
    [InlineData(AccessLevel.Owner)]
    [InlineData(AccessLevel.Custom)]
    public async Task ALevelThatCannotBeRequested_IsRefused(AccessLevel level)
    {
        var service = Service();

        await Assert.ThrowsAsync<ArgumentException>(() => service.RequestTeamAccessAsync(TeamKey, level, TimeSpan.FromHours(1), null));
        Assert.Null(service.Team(TeamKey).AccessRequests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ANonPositiveDuration_IsRefused(int hours)
        => await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service().RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, TimeSpan.FromHours(hours), null));

    [Fact]
    public async Task AnOverlongMessage_IsRefused()
        => await Assert.ThrowsAsync<ArgumentException>(() =>
            Service().RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, null, new string('x', TeamAccessRequestRules.MaxMessageLength + 1)));

    /// <summary>Membership outranks consent, so an approved request would grant a member nothing.</summary>
    [Fact]
    public async Task AMember_CannotRequest()
    {
        As(Manager);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().RequestTeamAccessAsync(TeamKey, AccessLevel.Administrator, null, null));

        Assert.Contains("already a member", ex.Message);
    }

    [Fact]
    public async Task AnUnknownTeam_IsRefused()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => Service().RequestTeamAccessAsync("nope", AccessLevel.Viewer, null, null));

    [Fact]
    public async Task ASecondRequest_ReplacesTheRequestersPendingOne()
    {
        var service = Service();

        var first = await service.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, TimeSpan.FromHours(1), null);
        var second = await service.RequestTeamAccessAsync(TeamKey, AccessLevel.User, TimeSpan.FromHours(1), null);

        var requests = service.Team(TeamKey).AccessRequests;
        Assert.Equal(TeamAccessRequestStatus.Cancelled, requests.Single(x => x.Id == first.Id).Status);
        Assert.Equal(TeamAccessRequestStatus.Pending, requests.Single(x => x.Id == second.Id).Status);
    }

    // --- cancelling ---

    [Fact]
    public async Task TheRequester_CanCancel()
    {
        var service = Service();
        var request = await service.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, null, null);

        await service.CancelTeamAccessRequestAsync(TeamKey, request.Id);

        Assert.Equal(TeamAccessRequestStatus.Cancelled, Single(service).Status);
    }

    [Fact]
    public async Task SomeoneElse_CannotCancel()
    {
        var service = Service();
        var request = await service.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, null, null);

        As(Manager);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CancelTeamAccessRequestAsync(TeamKey, request.Id));
        Assert.Equal(TeamAccessRequestStatus.Pending, Single(service).Status);
    }

    // --- approving ---

    [Fact]
    public async Task Approving_ConsentsTheRolesAtTheRequestedLevel_ForTheWindow()
    {
        var service = Service();
        service.SeedConsent(TeamKey, ["Developer"], AccessLevel.Viewer);
        var request = await service.RequestTeamAccessAsync(TeamKey, AccessLevel.Administrator, TimeSpan.FromHours(2), null);

        As(Manager);
        var before = DateTime.UtcNow;
        await service.ApproveTeamAccessRequestAsync(TeamKey, request.Id, ["Developer"]);

        var team = service.Team(TeamKey);
        Assert.Equal(["Developer"], team.ConsentedRoles);
        Assert.Equal(AccessLevel.Administrator, team.ConsentAccessLevel);
        Assert.InRange(team.TemporaryConsent.ExpiresAt, before.AddHours(2), DateTime.UtcNow.AddHours(2));
        Assert.Equal(["Developer"], team.TemporaryConsent.PreviousConsentedRoles);
        Assert.Equal(AccessLevel.Viewer, team.TemporaryConsent.PreviousConsentAccessLevel);

        var approved = Single(service);
        Assert.Equal(TeamAccessRequestStatus.Approved, approved.Status);
        Assert.Equal(Manager, approved.DecidedBy);
        Assert.Equal(team.TemporaryConsent.ExpiresAt, approved.GrantedUntil);
    }

    /// <summary>No end asked for, so the consent is standing — nothing to return to.</summary>
    [Fact]
    public async Task ApprovingARequestWithNoEnd_LeavesStandingConsent()
    {
        var service = Service();
        var request = await service.RequestTeamAccessAsync(TeamKey, AccessLevel.User, duration: null, message: null);

        As(Manager);
        await service.ApproveTeamAccessRequestAsync(TeamKey, request.Id, ["Developer"]);

        Assert.Null(service.Team(TeamKey).TemporaryConsent);
        Assert.Null(Single(service).GrantedUntil);
    }

    [Fact]
    public async Task TheRequester_CannotApproveTheirOwnRequest()
    {
        var service = Service();
        var request = await service.RequestTeamAccessAsync(TeamKey, AccessLevel.Administrator, TimeSpan.FromHours(1), null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApproveTeamAccessRequestAsync(TeamKey, request.Id, ["Developer"]));
        Assert.Null(service.Team(TeamKey).ConsentedRoles);
    }

    [Fact]
    public async Task ApprovingWithNoRoles_IsRefused()
    {
        var service = Service();
        var request = await service.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, null, null);

        As(Manager);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ApproveTeamAccessRequestAsync(TeamKey, request.Id, []));
    }

    [Fact]
    public async Task ARequestAlreadyDecided_CannotBeApproved()
    {
        var service = Service();
        var request = await service.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, null, null);
        await service.CancelTeamAccessRequestAsync(TeamKey, request.Id);

        As(Manager);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveTeamAccessRequestAsync(TeamKey, request.Id, ["Developer"]));

        Assert.Contains("already been decided", ex.Message);
        Assert.Null(service.Team(TeamKey).ConsentedRoles);
    }

    // --- denying ---

    [Fact]
    public async Task Denying_MarksItDenied_AndLeavesConsentAlone()
    {
        var service = Service();
        service.SeedConsent(TeamKey, ["Developer"], AccessLevel.Viewer);
        var request = await service.RequestTeamAccessAsync(TeamKey, AccessLevel.Administrator, TimeSpan.FromHours(1), null);

        As(Manager);
        await service.DenyTeamAccessRequestAsync(TeamKey, request.Id);

        Assert.Equal(TeamAccessRequestStatus.Denied, Single(service).Status);
        Assert.Equal(AccessLevel.Viewer, service.Team(TeamKey).ConsentAccessLevel);
    }

    // --- a host store without the hooks ---

    [Fact]
    public async Task AStoreWithoutTheHooks_SaysSo()
    {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => Service(noHooks: true).RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, null, null));

        Assert.Contains(nameof(ITeam.AccessRequests), ex.Message);
    }
}
