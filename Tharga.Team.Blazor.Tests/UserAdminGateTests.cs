using Tharga.Team;
using Tharga.Team.Blazor.Features.User;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Gating for the user administration surface: actions require the <c>users:manage</c> scope, and
/// directory features (verify, badge column, directory-only tab, the Entra delete opt-in)
/// additionally require a registered directory service. Pure-function tests to match the other
/// gating tests in this project (no bUnit, so razor markup cannot be asserted directly).
/// </summary>
public class UserAdminGateTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void CanAdministerUsers_RequiresScope(bool hasScope, bool expected)
    {
        Assert.Equal(expected, UserAdminGate.CanAdministerUsers(hasScope));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ShowDirectoryFeatures_RequiresScopeAndRegisteredDirectory(bool hasScope, bool directoryRegistered, bool expected)
    {
        Assert.Equal(expected, UserAdminGate.ShowDirectoryFeatures(hasScope, directoryRegistered));
    }

    /// <summary>
    /// Presentational only. The same gate must never be reused to offer consent or custom-role editing —
    /// the system scope does not reach them and the service refuses.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void CanManageTeams_RequiresTheSystemScope(bool hasScope, bool expected)
    {
        Assert.Equal(expected, UserAdminGate.CanManageTeams(hasScope));
    }

    /// <summary>
    /// The scope alone decides it. <b>Deliberately not conditioned on the owner count any more</b> — the
    /// operation serves an ownerless team, a team with one owner and a team with several, so a gate keyed
    /// on ownerless-ness would hide it from the two cases it was widened for.
    /// </summary>
    [Fact]
    public void CanSetOwner_ScopedCaller_IsAllowed()
    {
        Assert.True(UserAdminGate.CanSetOwner(hasSetOwnerScope: true));
    }

    [Fact]
    public void CanSetOwner_WithoutTheScope_IsHidden()
    {
        Assert.False(UserAdminGate.CanSetOwner(hasSetOwnerScope: false));
    }

    [Fact]
    public void CanDeleteUser_AnotherUser_IsAllowed()
    {
        Assert.True(UserAdminGate.CanDeleteUser("user-1", "user-2"));
    }

    /// <summary>The case the guard exists for: the caller's own row is never deletable.</summary>
    [Fact]
    public void CanDeleteUser_OwnRow_IsRefused()
    {
        Assert.False(UserAdminGate.CanDeleteUser("user-1", "user-1"));
    }

    /// <summary>
    /// Stricter than <c>MemberHighlight.IsCurrentMember</c> on purpose. That drives a highlight, where a
    /// false positive is cosmetic; here a false negative deletes an account, so a key differing only in
    /// case must still read as "you".
    /// </summary>
    [Theory]
    [InlineData("USER-1", "user-1")]
    [InlineData("user-1", "User-1")]
    public void CanDeleteUser_OwnRowDifferingOnlyInCase_IsRefused(string rowUserKey, string currentUserKey)
    {
        Assert.False(UserAdminGate.CanDeleteUser(rowUserKey, currentUserKey));
    }

    /// <summary>
    /// Fails closed. A null current-user key means the caller could not be established — not a state in
    /// which to offer an irreversible action, so every row refuses rather than every row allowing.
    /// </summary>
    [Theory]
    [InlineData(null, "user-1")]
    [InlineData("user-1", null)]
    [InlineData("", "user-1")]
    [InlineData("user-1", "")]
    [InlineData(null, null)]
    public void CanDeleteUser_UnknownIdentity_IsRefused(string rowUserKey, string currentUserKey)
    {
        Assert.False(UserAdminGate.CanDeleteUser(rowUserKey, currentUserKey));
    }

    [Theory]
    [InlineData(DirectoryUserStatus.Found, "Found", "Success")]
    [InlineData(DirectoryUserStatus.NotFound, "Not found", "Danger")]
    [InlineData(DirectoryUserStatus.Disabled, "Disabled", "Warning")]
    [InlineData(DirectoryUserStatus.NotLinked, "Not linked", "Secondary")]
    public void DirectoryStatusBadge_MapsEveryStatus(DirectoryUserStatus status, string text, string style)
    {
        Assert.Equal(text, DirectoryStatusBadge.Text(status));
        Assert.Equal(style, DirectoryStatusBadge.Style(status));
    }

    /// <summary>
    /// Disabling excludes the self-row for the same reason deleting does: an administrator who disables
    /// themselves needs a second one to undo it. The service refuses it too — this only stops the row
    /// offering an action that would throw.
    /// </summary>
    [Fact]
    public void CanDisableUser_AnotherUser_IsAllowed()
    {
        Assert.True(UserAdminGate.CanDisableUser("user-1", "user-2"));
    }

    [Fact]
    public void CanDisableUser_OwnRow_IsRefused()
    {
        Assert.False(UserAdminGate.CanDisableUser("user-1", "user-1"));
    }

    /// <summary>A differently-cased key is the same account; the guard is not side-steppable by casing.</summary>
    [Theory]
    [InlineData("USER-1", "user-1")]
    [InlineData("user-1", "USER-1")]
    public void CanDisableUser_OwnRowDifferingOnlyInCase_IsRefused(string rowUserKey, string currentUserKey)
    {
        Assert.False(UserAdminGate.CanDisableUser(rowUserKey, currentUserKey));
    }

    /// <summary>
    /// An unknown identity is refused rather than allowed. Failing open here would offer the action on
    /// every row the moment the current user could not be resolved — including the operator's own.
    /// </summary>
    [Theory]
    [InlineData(null, "user-1")]
    [InlineData("user-1", null)]
    [InlineData("", "user-1")]
    [InlineData("user-1", "")]
    public void CanDisableUser_UnknownIdentity_IsRefused(string rowUserKey, string currentUserKey)
    {
        Assert.False(UserAdminGate.CanDisableUser(rowUserKey, currentUserKey));
    }

    /// <summary>
    /// The audit action is offered only to a caller who can actually read audit — hidden, not shown and
    /// then refused. The host flag alone is permission to <i>offer</i> the feature, not evidence this
    /// caller may use it.
    /// </summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void CanReadAudit_NeedsBothTheHostFlagAndTheGrant(bool showButton, bool hasGrant, bool expected)
    {
        Assert.Equal(expected, UserAdminGate.CanReadAudit(showButton, hasGrant));
    }
}
