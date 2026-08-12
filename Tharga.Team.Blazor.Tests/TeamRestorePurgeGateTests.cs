using Tharga.Team.Blazor.Features.User;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What the Teams tab offers on a soft-deleted team — see <see cref="UserAdminGate.CanRestoreTeam"/> and
/// <see cref="UserAdminGate.CanPurgeTeam"/>.
/// </summary>
/// <remarks>
/// The two are deliberately asymmetric. Restore rides on <c>teams:delete</c> because it undoes it and is
/// strictly less destructive. Purge has its own scope: it is the only irreversible team operation and the
/// only one needing the deployment's privilege to destroy stored data, which is the privilege boundary
/// Tharga/Team#224 asked for.
/// </remarks>
public class TeamRestorePurgeGateTests
{
    /// <summary>Restore is offered to whoever may delete, on a team that is actually deleted.</summary>
    [Fact]
    public void Restore_OfferedToADeleterOnADeletedTeam()
    {
        Assert.True(UserAdminGate.CanRestoreTeam(hasTeamsDeleteScope: true, isDeleted: true));
    }

    /// <summary>Nothing to restore on a live team, so the control is not drawn.</summary>
    [Fact]
    public void Restore_NotOfferedOnALiveTeam()
    {
        Assert.False(UserAdminGate.CanRestoreTeam(hasTeamsDeleteScope: true, isDeleted: false));
    }

    [Fact]
    public void Restore_NotOfferedWithoutTheScope()
    {
        Assert.False(UserAdminGate.CanRestoreTeam(hasTeamsDeleteScope: false, isDeleted: true));
    }

    /// <summary>
    /// <b>The boundary that matters.</b> Holding <c>teams:delete</c> must not reach purge — otherwise the
    /// separation is decorative and a deployment cannot withhold the destructive capability while still
    /// allowing deletion, which is the whole ask in #224.
    /// </summary>
    [Fact]
    public void Purge_IsNotReachableWithTheDeleteScope()
    {
        Assert.False(UserAdminGate.CanPurgeTeam(hasTeamsPurgeScope: false, isDeleted: true));
    }

    [Fact]
    public void Purge_OfferedWithItsOwnScopeOnADeletedTeam()
    {
        Assert.True(UserAdminGate.CanPurgeTeam(hasTeamsPurgeScope: true, isDeleted: true));
    }

    /// <summary>
    /// Purge is offered only after a soft delete, so destroying a team's data is a second, separate
    /// decision rather than one click away from a live team.
    /// </summary>
    [Fact]
    public void Purge_NotOfferedOnALiveTeam()
    {
        Assert.False(UserAdminGate.CanPurgeTeam(hasTeamsPurgeScope: true, isDeleted: false));
    }

    /// <summary>
    /// The self-check: every assertion above would hold if both predicates ignored their arguments and
    /// returned the same constant, so this proves each input changes the answer.
    /// </summary>
    [Fact]
    public void BothInputsDecide()
    {
        Assert.NotEqual(
            UserAdminGate.CanRestoreTeam(true, true),
            UserAdminGate.CanRestoreTeam(true, false));

        Assert.NotEqual(
            UserAdminGate.CanPurgeTeam(true, true),
            UserAdminGate.CanPurgeTeam(false, true));
    }
}
