namespace Tharga.Team.Service.Tests;

/// <summary>
/// Invitations that expire, and extending one without reissuing its code.
/// </summary>
/// <remarks>
/// From Tharga/Team#249. The requirement that shapes the design is not "invitations expire" but
/// <b>"extending one keeps the same code"</b> — someone who has already mailed a link must be able to give it
/// more time without the recipient's link dying. That is why the expiry lives on the invitation record
/// rather than being derived from <see cref="Invitation.InviteTime"/> plus a configured lifetime: extending a
/// derived expiry would mean rewriting the creation time, which would falsify it.
/// </remarks>
public class InvitationExpiryTests
{
    private const string TeamKey = "team-1";
    private const string InviteKey = "invite-1";
    private const string EMail = "invitee@example.com";

    private static readonly TimeSpan Fortnight = TimeSpan.FromDays(14);

    private static IUserService UserService()
    {
        var userService = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns("invitee-key");
        user.EMail.Returns(EMail);
        userService.GetCurrentUserAsync().Returns(user);
        return userService;
    }

    private static TestTeamService Build(TimeSpan? lifetime, DateTime? expiresAt, DateTime? inviteTime = null)
    {
        var sut = new TestTeamService(UserService(), invitationOptions: new InvitationOptions { Lifetime = lifetime });

        sut.AddTeam(TeamKey, "Test Team", new TestMember
        {
            Key = "member-key",
            Name = "Alice",
            State = MembershipState.Invited,
            AccessLevel = AccessLevel.User,
            Invitation = new Invitation
            {
                EMail = EMail,
                InviteKey = InviteKey,
                InviteTime = inviteTime ?? DateTime.UtcNow,
                ExpiresAt = expiresAt
            }
        });

        return sut;
    }

    private static Invitation InvitationOn(TestTeamService sut)
    {
        var team = (TestTeam)sut.GetTeamsAsync().ToBlockingEnumerable().Single(x => x.Key == TeamKey);
        return team.Members.Single().Invitation;
    }

    /// <summary>The default. Invitations did not expire before this existed, and by default they still do not.</summary>
    [Fact]
    public async Task WithNoLifetimeConfigured_AnOldInvitationIsStillAccepted()
    {
        var sut = Build(lifetime: null, expiresAt: null, inviteTime: DateTime.UtcNow.AddYears(-3));

        await sut.SetInvitationResponseAsync(TeamKey, "invitee-key", InviteKey, true);
    }

    [Fact]
    public async Task WithALifetime_AnInvitationInsideItIsAccepted()
    {
        var sut = Build(lifetime: Fortnight, expiresAt: null, inviteTime: DateTime.UtcNow.AddDays(-1));

        await sut.SetInvitationResponseAsync(TeamKey, "invitee-key", InviteKey, true);
    }

    [Fact]
    public async Task WithALifetime_AnInvitationPastItIsRefused()
    {
        var sut = Build(lifetime: Fortnight, expiresAt: null, inviteTime: DateTime.UtcNow.AddDays(-15));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SetInvitationResponseAsync(TeamKey, "invitee-key", InviteKey, true));
    }

    /// <summary>
    /// The stored expiry wins over the configured lifetime. Without this an extension would be undone by the
    /// lifetime the moment it was read back, which is the whole mechanism failing quietly.
    /// </summary>
    [Fact]
    public async Task AStoredExpiryOverridesTheConfiguredLifetime()
    {
        var sut = Build(lifetime: Fortnight, expiresAt: DateTime.UtcNow.AddDays(7), inviteTime: DateTime.UtcNow.AddDays(-100));

        await sut.SetInvitationResponseAsync(TeamKey, "invitee-key", InviteKey, true);
    }

    /// <summary>Declining stays possible, or an expired invitation could never be cleared by its recipient.</summary>
    [Fact]
    public async Task AnExpiredInvitationCanStillBeDeclined()
    {
        var sut = Build(lifetime: Fortnight, expiresAt: null, inviteTime: DateTime.UtcNow.AddDays(-15));

        await sut.SetInvitationResponseAsync(TeamKey, "invitee-key", InviteKey, false);
    }

    /// <summary><b>The requirement.</b> Extending moves the expiry and leaves the code alone.</summary>
    [Fact]
    public async Task Extending_MovesTheExpiryAndKeepsTheCode()
    {
        var sut = Build(lifetime: Fortnight, expiresAt: DateTime.UtcNow.AddDays(-1), inviteTime: DateTime.UtcNow.AddDays(-20));

        await sut.ExtendInvitationAsync(TeamKey, InviteKey);

        var invitation = InvitationOn(sut);

        Assert.Equal(InviteKey, invitation.InviteKey);
        Assert.NotNull(invitation.ExpiresAt);
        Assert.True(invitation.ExpiresAt > DateTime.UtcNow.AddDays(13));
    }

    /// <summary>And the extended invitation is then acceptable again — the point of extending it.</summary>
    [Fact]
    public async Task AnExtendedInvitationIsAcceptedAgain()
    {
        var sut = Build(lifetime: Fortnight, expiresAt: DateTime.UtcNow.AddDays(-1), inviteTime: DateTime.UtcNow.AddDays(-20));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SetInvitationResponseAsync(TeamKey, "invitee-key", InviteKey, true));

        await sut.ExtendInvitationAsync(TeamKey, InviteKey);

        await sut.SetInvitationResponseAsync(TeamKey, "invitee-key", InviteKey, true);
    }

    [Fact]
    public async Task ExtendingAnUnknownCode_Throws()
    {
        var sut = Build(lifetime: Fortnight, expiresAt: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExtendInvitationAsync(TeamKey, "not-a-code"));
    }

    /// <summary>
    /// Re-inviting the same address renews the invitation it already has rather than issuing a second live
    /// code for one seat.
    /// </summary>
    [Fact]
    public async Task ReInvitingTheSameAddress_RenewsRatherThanAddingASecondInvitation()
    {
        var sut = Build(lifetime: Fortnight, expiresAt: DateTime.UtcNow.AddDays(-1), inviteTime: DateTime.UtcNow.AddDays(-20));

        await sut.AddMemberAsync(TeamKey, new InviteUserModel { Email = EMail, AccessLevel = AccessLevel.User });

        var invitation = InvitationOn(sut);

        Assert.Equal(0, sut.AddTeamMemberCallCount);
        Assert.Equal(InviteKey, invitation.InviteKey);
        Assert.True(invitation.ExpiresAt > DateTime.UtcNow.AddDays(13));
    }

    /// <summary>Matching an address is case-insensitive, or the same person invited twice gets two codes.</summary>
    [Fact]
    public async Task ReInviting_MatchesTheAddressCaseInsensitively()
    {
        var sut = Build(lifetime: Fortnight, expiresAt: null);

        await sut.AddMemberAsync(TeamKey, new InviteUserModel { Email = EMail.ToUpperInvariant(), AccessLevel = AccessLevel.User });

        Assert.Equal(0, sut.AddTeamMemberCallCount);
    }

    /// <summary>A different address is a different person and gets its own invitation.</summary>
    [Fact]
    public async Task InvitingADifferentAddress_AddsANewInvitation()
    {
        var sut = Build(lifetime: Fortnight, expiresAt: null);

        await sut.AddMemberAsync(TeamKey, new InviteUserModel { Email = "someone.else@example.com", AccessLevel = AccessLevel.User });

        Assert.Equal(1, sut.AddTeamMemberCallCount);
    }

    /// <summary>
    /// A host that never configured a lifetime must not be asked for an expiry seam it has not implemented —
    /// the dedupe still applies, the extension simply has nothing to move.
    /// </summary>
    [Fact]
    public async Task ReInviting_WithNoLifetime_DoesNotRequireTheExpirySeam()
    {
        var sut = Build(lifetime: null, expiresAt: null);

        await sut.AddMemberAsync(TeamKey, new InviteUserModel { Email = EMail, AccessLevel = AccessLevel.User });

        Assert.Equal(0, sut.AddTeamMemberCallCount);
        Assert.Null(InvitationOn(sut).ExpiresAt);
    }
}
