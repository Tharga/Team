namespace Tharga.Team.Service.Tests;

/// <summary>
/// Invitation expiry and the invited name on a store that never overrode the invitation lookups.
/// </summary>
/// <remarks>
/// The enforcement point for expiry is <see cref="TeamServiceBase.SetInvitationResponseAsync"/>, and it used to
/// ask a lookup whose default answered null — which reads as "never expires". A host deriving
/// <see cref="TeamServiceBase"/> directly with a lifetime configured therefore granted membership on expired
/// invitations to any caller that did not pass through the invitation screen first.
/// </remarks>
public class InvitationDefaultLookupTests
{
    private const string TeamKey = "team-1";
    private const string InviteKey = "invite-1";
    private const string InviteeKey = "invitee-key";
    private const string InvitedName = "Alice";

    private static readonly TimeSpan Fortnight = TimeSpan.FromDays(14);

    private static IUserService UserService()
    {
        var userService = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns(InviteeKey);
        user.EMail.Returns("invitee@example.com");
        userService.GetCurrentUserAsync().Returns(user);
        return userService;
    }

    private static TestTeamService Build(IUserService userService, TimeSpan? lifetime, DateTime inviteTime)
    {
        var sut = new TestTeamService(userService, invitationOptions: new InvitationOptions { Lifetime = lifetime })
        {
            UseDefaultInvitationLookups = true
        };

        sut.AddTeam(TeamKey, "Test Team", new TestMember
        {
            Key = "member-key",
            Name = InvitedName,
            State = MembershipState.Invited,
            AccessLevel = AccessLevel.User,
            Invitation = new Invitation
            {
                EMail = "invitee@example.com",
                InviteKey = InviteKey,
                InviteTime = inviteTime
            }
        });

        return sut;
    }

    [Fact]
    public async Task AnExpiredInvitationIsRefused_WithoutOverridingTheLookup()
    {
        var sut = Build(UserService(), Fortnight, DateTime.UtcNow.AddDays(-15));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SetInvitationResponseAsync(TeamKey, InviteeKey, InviteKey, true));
    }

    [Fact]
    public async Task AnInvitationInsideItsLifetimeIsAccepted_WithoutOverridingTheLookup()
    {
        var sut = Build(UserService(), Fortnight, DateTime.UtcNow.AddDays(-1));

        await sut.SetInvitationResponseAsync(TeamKey, InviteeKey, InviteKey, true);
    }

    [Fact]
    public async Task AnExpiredInvitationCanStillBeDeclined_WithoutOverridingTheLookup()
    {
        var sut = Build(UserService(), Fortnight, DateTime.UtcNow.AddDays(-15));

        await sut.SetInvitationResponseAsync(TeamKey, InviteeKey, InviteKey, false);
    }

    /// <summary>
    /// With a lifetime configured, "cannot tell whether it expired" must not read as "has not" — a code nobody
    /// holds is refused.
    /// </summary>
    [Fact]
    public async Task WithALifetime_AcceptingACodeNobodyHoldsIsRefused()
    {
        var sut = Build(UserService(), Fortnight, DateTime.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SetInvitationResponseAsync(TeamKey, InviteeKey, "not-a-code", true));
    }

    /// <summary>A host that never opted into expiry is asked for nothing new.</summary>
    [Fact]
    public async Task WithoutALifetime_AcceptingIsUnchanged()
    {
        var sut = Build(UserService(), lifetime: null, DateTime.UtcNow.AddYears(-3));

        await sut.SetInvitationResponseAsync(TeamKey, InviteeKey, InviteKey, true);
    }

    [Fact]
    public async Task TheInvitedNameIsCarriedOverOnAccept_WithoutOverridingTheLookup()
    {
        var userService = UserService();
        var sut = Build(userService, lifetime: null, DateTime.UtcNow);

        await sut.SetInvitationResponseAsync(TeamKey, InviteeKey, InviteKey, true);

        await userService.Received(1).SeedUserNameAsync(InviteeKey, InvitedName);
    }
}
