namespace Tharga.Team.Service.Tests;

/// <summary>
/// The Owner and last-administrator guards on removing a member, on stores the base cannot read the usual way.
/// </summary>
/// <remarks>
/// <see cref="TeamServiceBase.RemoveMemberAsync"/> used to read the roster by reflecting a <c>Members</c> array.
/// A team type with no such property, or with one that is a list, read as "no roster" — and with no roster the
/// guards were skipped and the member removed anyway, the Owner included.
/// </remarks>
public class RosterGuardTests
{
    private const string TeamKey = "team-1";
    private const string OwnerKey = "owner";
    private const string AdminKey = "admin";
    private const string OtherAdminKey = "other-admin";
    private const string UserKey = "user";

    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IUser _currentUser = Substitute.For<IUser>();

    public RosterGuardTests()
    {
        _currentUser.Key.Returns(OwnerKey);
        _userService.GetCurrentUserAsync().Returns(_currentUser);
    }

    private TestTeamService Build(TestTeamShape shape, params TestMember[] members)
    {
        var sut = new TestTeamService(_userService) { Shape = shape };
        sut.AddTeam(TeamKey, "Test Team", members);
        return sut;
    }

    private static TestMember Member(string key, AccessLevel level, MembershipState state = MembershipState.Member, DateTime? suspendedAt = null)
        => new() { Key = key, AccessLevel = level, State = state, SuspendedAt = suspendedAt };

    private static TestMember[] OwnedTeam() =>
    [
        Member(OwnerKey, AccessLevel.Owner),
        Member(UserKey, AccessLevel.User)
    ];

    [Theory]
    [InlineData(TestTeamShape.WithoutMembers)]
    [InlineData(TestTeamShape.MembersAsList)]
    public async Task RemovingTheOwner_IsRefused_WhateverShapeTheTeamHas(TestTeamShape shape)
    {
        _currentUser.Key.Returns(AdminKey);
        var sut = Build(shape, OwnedTeam());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RemoveMemberAsync(TeamKey, OwnerKey));

        Assert.Equal(0, sut.RemoveTeamMemberCallCount);
    }

    /// <summary>A roster the base cannot read fails closed: nobody is removed without the guards having run.</summary>
    [Fact]
    public async Task WithoutARoster_RemovingAnyoneIsRefused()
    {
        var sut = Build(TestTeamShape.WithoutMembers, OwnedTeam());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RemoveMemberAsync(TeamKey, UserKey));

        Assert.Equal(0, sut.RemoveTeamMemberCallCount);
    }

    [Theory]
    [InlineData(TestTeamShape.MembersAsArray)]
    [InlineData(TestTeamShape.MembersAsList)]
    public async Task RemovingAnOrdinaryMember_Works(TestTeamShape shape)
    {
        var sut = Build(shape, OwnedTeam());

        await sut.RemoveMemberAsync(TeamKey, UserKey);

        Assert.Equal(1, sut.RemoveTeamMemberCallCount);
    }

    /// <summary>Withdrawing an invitation is a removal too, and the invitee is on the roster in any state.</summary>
    [Fact]
    public async Task RemovingAnInvitedMember_Works()
    {
        var sut = Build(TestTeamShape.MembersAsArray,
            Member(OwnerKey, AccessLevel.Owner),
            Member(UserKey, AccessLevel.User, MembershipState.Invited));

        await sut.RemoveMemberAsync(TeamKey, UserKey);

        Assert.Equal(1, sut.RemoveTeamMemberCallCount);
    }

    [Fact]
    public async Task OnAListShapedTeam_TheLastAdministratorCannotRemoveThemselves()
    {
        _currentUser.Key.Returns(AdminKey);
        var sut = Build(TestTeamShape.MembersAsList,
            Member(AdminKey, AccessLevel.Administrator),
            Member(UserKey, AccessLevel.User));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RemoveMemberAsync(TeamKey, AdminKey));

        Assert.Equal(0, sut.RemoveTeamMemberCallCount);
    }

    private TestTeamService OwnerlessTeamWhoseOtherAdministratorIs(DateTime? suspendedAt)
    {
        _currentUser.Key.Returns(AdminKey);
        return Build(TestTeamShape.MembersAsArray,
            Member(AdminKey, AccessLevel.Administrator),
            Member(OtherAdminKey, AccessLevel.Administrator, suspendedAt: suspendedAt),
            Member(UserKey, AccessLevel.User));
    }

    /// <summary>
    /// A suspended administrator holds no scopes, so they cannot be the one left holding <c>member:manage</c>.
    /// Decided 2026-09-26.
    /// </summary>
    [Fact]
    public async Task ASuspendedAdministratorIsNotCover_ForRemovingYourself()
    {
        var sut = OwnerlessTeamWhoseOtherAdministratorIs(suspendedAt: DateTime.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RemoveMemberAsync(TeamKey, AdminKey));
    }

    [Fact]
    public async Task ASuspendedAdministratorIsNotCover_ForLeaving()
    {
        var sut = OwnerlessTeamWhoseOtherAdministratorIs(suspendedAt: DateTime.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.LeaveTeamAsync(TeamKey));
    }

    [Fact]
    public async Task AnActiveAdministratorIsCover_ForLeaving()
    {
        var sut = OwnerlessTeamWhoseOtherAdministratorIs(suspendedAt: null);

        await sut.LeaveTeamAsync(TeamKey);

        Assert.Equal(1, sut.RemoveTeamMemberCallCount);
    }
}
