using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The rules behind <see cref="SystemTeamScopes.SetOwner"/>. They are the entire safety argument for an
/// operation that hands out <c>Owner</c> and takes it away with no sitting owner's consent, so they are
/// pinned here rather than left inside a service method.
/// </summary>
/// <remarks>
/// The invariant under test throughout: <b>a team ends up with exactly one owner</b>. These rules cannot
/// enforce that on their own — the service applies them in the right order — but they are what makes it
/// expressible, and every roster below is one of the ways a team arrives not satisfying it.
/// </remarks>
public class TeamOwnershipTests
{
    private sealed record Member(string Key, AccessLevel AccessLevel) : ITeamMember
    {
        public string Name => null;
        public string[] TenantRoles => null;
        public string[] ScopeOverrides => null;
        public MembershipState? State => MembershipState.Member;
        public Invitation Invitation => null;
        public DateTime? LastSeen => null;
    }

    private static Member[] Healthy =>
    [
        new("owner-1", AccessLevel.Owner),
        new("admin-1", AccessLevel.Administrator)
    ];

    private static Member[] Ownerless =>
    [
        new("admin-1", AccessLevel.Administrator),
        new("user-1", AccessLevel.User)
    ];

    /// <summary>What a legacy sync delivers: a source model that permitted several owners.</summary>
    private static Member[] MultiOwner =>
    [
        new("owner-1", AccessLevel.Owner),
        new("owner-2", AccessLevel.Owner),
        new("owner-3", AccessLevel.Owner),
        new("admin-1", AccessLevel.Administrator)
    ];

    [Fact]
    public void IsOwnerless_TeamWithAnOwner_IsFalse()
    {
        Assert.False(TeamOwnership.IsOwnerless(Healthy));
    }

    [Fact]
    public void IsOwnerless_TeamWithoutAnOwner_IsTrue()
    {
        Assert.True(TeamOwnership.IsOwnerless(Ownerless));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsOwnerless_NoRoster_IsTrue(bool useNull)
    {
        Assert.True(TeamOwnership.IsOwnerless(useNull ? null : []));
    }

    // CanSetOwner -- one condition only, and the condition that is absent is the point.

    [Fact]
    public void CanSetOwner_ExistingMemberOfAnOwnerlessTeam_IsAllowed()
    {
        Assert.True(TeamOwnership.CanSetOwner(Ownerless, "admin-1"));
    }

    /// <summary>
    /// The case the old <c>CanAssign</c> refused. Deposing a sitting owner is now the operation's whole
    /// purpose, so a healthy team must not disqualify the candidate.
    /// </summary>
    [Fact]
    public void CanSetOwner_TeamThatAlreadyHasAnOwner_IsAllowed()
    {
        Assert.True(TeamOwnership.CanSetOwner(Healthy, "admin-1"));
    }

    [Fact]
    public void CanSetOwner_TeamWithSeveralOwners_IsAllowed()
    {
        Assert.True(TeamOwnership.CanSetOwner(MultiOwner, "owner-2"));
    }

    /// <summary>
    /// The one condition that is load-bearing: the caller holds a system scope precisely because they are
    /// not a member, so without this they could install anyone -- including themselves.
    /// </summary>
    [Fact]
    public void CanSetOwner_SomeoneWhoIsNotAMember_IsRefused()
    {
        Assert.False(TeamOwnership.CanSetOwner(Ownerless, "stranger"));
    }

    [Fact]
    public void CanSetOwner_EmptyTeam_IsRefused()
    {
        Assert.False(TeamOwnership.CanSetOwner([], "admin-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CanSetOwner_NoCandidate_IsRefused(string candidate)
    {
        Assert.False(TeamOwnership.CanSetOwner(Ownerless, candidate));
    }

    /// <summary>A null entry in the roster must not be mistaken for a member or crash the check.</summary>
    [Fact]
    public void CanSetOwner_RosterWithNulls_IsTolerated()
    {
        ITeamMember[] roster = [null, new Member("admin-1", AccessLevel.Administrator), null];

        Assert.True(TeamOwnership.CanSetOwner(roster, "admin-1"));
        Assert.False(TeamOwnership.CanSetOwner(roster, "stranger"));
    }

    // IsSoleOwner -- what makes a repeated sync idempotent.

    [Fact]
    public void IsSoleOwner_TheOnlyOwner_IsTrue()
    {
        Assert.True(TeamOwnership.IsSoleOwner(Healthy, "owner-1"));
    }

    /// <summary>
    /// One of several owners is <b>not</b> the sole owner, so the operation must not read this as "already
    /// correct" and skip the reduction it exists to perform.
    /// </summary>
    [Fact]
    public void IsSoleOwner_OneOfSeveralOwners_IsFalse()
    {
        Assert.False(TeamOwnership.IsSoleOwner(MultiOwner, "owner-2"));
    }

    [Fact]
    public void IsSoleOwner_AMemberWhoIsNotTheOwner_IsFalse()
    {
        Assert.False(TeamOwnership.IsSoleOwner(Healthy, "admin-1"));
    }

    [Fact]
    public void IsSoleOwner_OwnerlessTeam_IsFalse()
    {
        Assert.False(TeamOwnership.IsSoleOwner(Ownerless, "admin-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsSoleOwner_NoCandidate_IsFalse(string candidate)
    {
        Assert.False(TeamOwnership.IsSoleOwner(Healthy, candidate));
    }

    // OwnersToDemote -- who loses the role.

    [Fact]
    public void OwnersToDemote_TeamWithSeveralOwners_ReturnsEveryOwnerButTheIncomingOne()
    {
        var demoted = TeamOwnership.OwnersToDemote(MultiOwner, "owner-2");

        Assert.Equal(["owner-1", "owner-3"], demoted.Select(x => x.Key));
    }

    /// <summary>
    /// Promoting somebody who is not currently an owner displaces the sitting one -- the operator-driven
    /// transfer.
    /// </summary>
    [Fact]
    public void OwnersToDemote_PromotingANonOwner_DisplacesTheSittingOwner()
    {
        var demoted = TeamOwnership.OwnersToDemote(Healthy, "admin-1");

        Assert.Equal(["owner-1"], demoted.Select(x => x.Key));
    }

    /// <summary>
    /// An ownerless team demotes nobody. This empty result is <b>not</b> the same as "nothing happened" --
    /// see <see cref="SetOwnerResult"/>, which is why the service reports the two separately.
    /// </summary>
    [Fact]
    public void OwnersToDemote_OwnerlessTeam_IsEmpty()
    {
        Assert.Empty(TeamOwnership.OwnersToDemote(Ownerless, "admin-1"));
    }

    [Fact]
    public void OwnersToDemote_TheIncomingOwnerIsNeverInTheList()
    {
        Assert.DoesNotContain(TeamOwnership.OwnersToDemote(MultiOwner, "owner-1"), x => x.Key == "owner-1");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OwnersToDemote_NoRoster_IsEmpty(bool useNull)
    {
        Assert.Empty(TeamOwnership.OwnersToDemote(useNull ? null : [], "admin-1"));
    }

    [Fact]
    public void OwnersToDemote_RosterWithNulls_IsTolerated()
    {
        ITeamMember[] roster = [null, new Member("owner-1", AccessLevel.Owner), null];

        Assert.Equal(["owner-1"], TeamOwnership.OwnersToDemote(roster, "admin-1").Select(x => x.Key));
    }
}
