using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// <see cref="ITeamService.GetTeamKeyByInviteKeyAsync"/> survives the decorator chain a host actually gets.
/// </summary>
/// <remarks>
/// From Tharga/Team#272. The member is a default interface member returning <c>null</c>, so a decorator that
/// does not declare it compiles, registers and answers every short invitation link with "unknown code" —
/// whatever the store would have said. <see cref="InviteCodeResolutionTests"/> did not catch it because it
/// drives <see cref="TeamManagementService{TMember}"/> over a stub that declares the member; only the
/// decorated chain runs the default body.
/// </remarks>
public class InviteKeyForwardingTests
{
    private const string TeamKey = "team-1";
    private const string InviteKey = "84Fb6G_8BbXE";
    private const string EMail = "invitee@example.com";

    private static TestTeamService BuildStore()
    {
        var userService = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns("user-1");
        userService.GetCurrentUserAsync().Returns(user);

        var store = new TestTeamService(userService);
        store.AddTeam(TeamKey, "Test Team",
            new TestMember { Key = "user-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member },
            new TestMember
            {
                Key = "user-2",
                AccessLevel = AccessLevel.User,
                State = MembershipState.Invited,
                Invitation = new Invitation { EMail = EMail, InviteKey = InviteKey, InviteTime = DateTime.UtcNow }
            });

        return store;
    }

    private static AuthorizationTeamServiceDecorator Authorized(ITeamService inner)
    {
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(new ClaimsPrincipal(new ClaimsIdentity())));
        return new AuthorizationTeamServiceDecorator(inner, new TeamAuthorizer(accessor), new TeamLifecycleOptions());
    }

    private static (AuditingTeamServiceDecorator Sut, FakeAuditBackend Backend) Audited(ITeamService inner)
    {
        var (logger, backend) = FakeAuditLoggerFactory.Create();
        return (new AuditingTeamServiceDecorator(inner, logger, Substitute.For<IHttpContextAccessor>()), backend);
    }

    /// <summary>
    /// Called through the interface on purpose: that is how the host calls it, and it is the only way the
    /// default body can run.
    /// </summary>
    [Fact]
    public async Task AuthorizationDecorator_ForwardsToTheInnerService()
    {
        ITeamService sut = Authorized(BuildStore());

        Assert.Equal(TeamKey, await sut.GetTeamKeyByInviteKeyAsync(InviteKey));
    }

    /// <summary>No scope check: the invite code is the check, as for the other invitation reads.</summary>
    [Fact]
    public async Task AuthorizationDecorator_ForwardsForAnAnonymousCaller()
    {
        ITeamService sut = Authorized(BuildStore());

        Assert.Equal(TeamKey, await sut.GetTeamKeyByInviteKeyAsync(InviteKey));
    }

    [Fact]
    public async Task AuditingDecorator_ForwardsToTheInnerService()
    {
        var (decorator, _) = Audited(BuildStore());
        ITeamService sut = decorator;

        Assert.Equal(TeamKey, await sut.GetTeamKeyByInviteKeyAsync(InviteKey));
    }

    /// <summary>
    /// A lookup is a read, and reads are opt-in since #262. Auditing it would also let anyone holding a link
    /// write rows from an unauthenticated, pre-membership call.
    /// </summary>
    [Fact]
    public async Task AuditingDecorator_WritesNoEntry()
    {
        var (decorator, backend) = Audited(BuildStore());
        ITeamService sut = decorator;

        await sut.GetTeamKeyByInviteKeyAsync(InviteKey);

        Assert.Empty(backend.Entries);
    }

    [Fact]
    public async Task AnUnknownCode_StillResolvesToNull()
    {
        ITeamService sut = Authorized(BuildStore());

        Assert.Null(await sut.GetTeamKeyByInviteKeyAsync("not-a-code"));
    }

    /// <summary>
    /// The regression proper: a short link resolved over the full chain, in the order
    /// <c>ThargaBlazorRegistration</c> applies it — authorization outermost, audit beneath it, store innermost.
    /// </summary>
    [Fact]
    public async Task AShortLink_ResolvesThroughTheFullDecoratorChain()
    {
        var store = BuildStore();
        var (audited, _) = Audited(store);
        ITeamService chain = Authorized(audited);

        var userService = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns("user-2");
        userService.GetCurrentUserAsync().Returns(user);

        var management = new TeamManagementService<TestMember>(chain, userService, null);

        var invitation = await management.GetInvitationAsync(InviteKey);

        Assert.NotNull(invitation);
        Assert.Equal(TeamKey, invitation.TeamKey);
        Assert.Equal(EMail, invitation.EMail);
        Assert.Equal(InviteKey, invitation.InviteKey);
    }
}
