using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// When someone stops being a member of a team, their private API keys there are deleted, or disabled when
/// the host asks for that (Tharga/Team#323). Covers the participant, the cascade that runs it, and each of
/// the three removal paths through the authorization decorator.
/// </summary>
public class ApiKeyOwnerRemovalTests
{
    private const string Team = "acme";
    private const string Owner = "user-1";

    private readonly IApiKeyAdministrationService _apiKeys = Substitute.For<IApiKeyAdministrationService>();

    private static IApiKey Key(string id, string owner = null, DateTime? disabledAt = null)
    {
        var key = Substitute.For<IApiKey>();
        key.Key.Returns(id);
        key.OwnerMemberKey.Returns(owner);
        key.DisabledAt.Returns(disabledAt);
        return key;
    }

    private static async IAsyncEnumerable<IApiKey> Keys(params IApiKey[] items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }

    private void GivenKeys(params IApiKey[] keys) => _apiKeys.GetKeysAsync(Team).Returns(_ => Keys(keys));

    private ApiKeyOwnerRemovalParticipant Participant(PrivateKeyOwnerRemovedAction action = PrivateKeyOwnerRemovedAction.Delete)
        => new(_apiKeys, Options.Create(new ApiKeyOptions { OwnerRemovedAction = action }));

    // ---- The participant ----

    [Fact]
    public async Task Delete_RemovesOnlyTheOwnersPrivateKeys()
    {
        GivenKeys(Key("team-wide"), Key("mine", Owner), Key("someone-elses", "user-2"));

        var affected = await Participant().MemberRemovedAsync(Team, Owner);

        Assert.Equal(1, affected);
        await _apiKeys.Received(1).DeleteKeyAsync(Team, "mine");
        await _apiKeys.DidNotReceive().DeleteKeyAsync(Team, "team-wide");
        await _apiKeys.DidNotReceive().DeleteKeyAsync(Team, "someone-elses");
    }

    [Fact]
    public async Task Disable_DisablesInsteadOfDeleting_SkippingOnesAlreadyDisabled()
    {
        GivenKeys(Key("mine", Owner), Key("mine-disabled", Owner, DateTime.UtcNow));

        var affected = await Participant(PrivateKeyOwnerRemovedAction.Disable).MemberRemovedAsync(Team, Owner);

        Assert.Equal(1, affected);
        await _apiKeys.Received(1).SetKeyDisabledAsync(Team, "mine", true, Arg.Any<string>());
        await _apiKeys.DidNotReceive().SetKeyDisabledAsync(Team, "mine-disabled", Arg.Any<bool>(), Arg.Any<string>());
        await _apiKeys.DidNotReceiveWithAnyArgs().DeleteKeyAsync(default, default);
    }

    [Fact]
    public void DefaultAction_IsDelete()
    {
        Assert.Equal(PrivateKeyOwnerRemovedAction.Delete, new ApiKeyOptions().OwnerRemovedAction);
    }

    [Fact]
    public async Task NoApiKeyService_DoesNothing()
    {
        Assert.Equal(0, await new ApiKeyOwnerRemovalParticipant().MemberRemovedAsync(Team, Owner));
    }

    // ---- The cascade ----

    [Fact]
    public async Task Cascade_AFailingParticipant_DoesNotThrow_AndOthersStillRun()
    {
        var failing = Substitute.For<ITeamMemberRemovalParticipant>();
        failing.MemberRemovedAsync(Team, Owner, Arg.Any<CancellationToken>()).Returns<Task<int>>(_ => throw new TimeoutException());
        var other = Substitute.For<ITeamMemberRemovalParticipant>();

        await new TeamMemberRemovalCascade([failing, other]).RunAsync(Team, Owner);

        await other.Received(1).MemberRemovedAsync(Team, Owner, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cascade_GetTeamKeys_CoversEveryAccessLevel_AndToleratesAStoreThatCannotAnswer()
    {
        var teams = Substitute.For<ITeamService>();
        IReadOnlyList<ITeam> userTeams = [TeamNamed("a"), TeamNamed("b")];
        teams.GetTeamsForUserWithAccessLevelAsync(Owner, Arg.Any<AccessLevel>()).Returns(Task.FromResult<IReadOnlyList<ITeam>>([]));
        teams.GetTeamsForUserWithAccessLevelAsync(Owner, AccessLevel.User).Returns(Task.FromResult(userTeams));
        teams.GetTeamsForUserWithAccessLevelAsync(Owner, AccessLevel.Viewer).Returns<Task<IReadOnlyList<ITeam>>>(_ => throw new NotSupportedException());

        var keys = await new TeamMemberRemovalCascade([]).GetTeamKeysAsync(teams, Owner);

        Assert.Equal(["a", "b"], keys);
        foreach (var level in Enum.GetValues<AccessLevel>())
            await teams.Received(1).GetTeamsForUserWithAccessLevelAsync(Owner, level);
    }

    private static ITeam TeamNamed(string key)
    {
        var team = Substitute.For<ITeam>();
        team.Key.Returns(key);
        return team;
    }

    // ---- Every removal path runs it, through the decorator ----

    private readonly ITeamService _inner = Substitute.For<ITeamService>();
    private readonly ITeamMemberRemovalParticipant _participant = Substitute.For<ITeamMemberRemovalParticipant>();
    private readonly IUserService _users = Substitute.For<IUserService>();

    private AuthorizationTeamServiceDecorator Decorator(params Claim[] claims)
    {
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
        var cascade = new TeamMemberRemovalCascade([_participant], _users);
        return new AuthorizationTeamServiceDecorator(_inner, new TeamAuthorizer(accessor), new TeamLifecycleOptions(), memberRemovalCascade: cascade);
    }

    private static Claim[] MemberManageOn(string team) =>
    [
        new(TeamClaimTypes.TeamKey, team),
        new(TeamClaimTypes.Scope, TeamScopes.MemberManage)
    ];

    [Fact]
    public async Task RemoveMember_CleansUpAfterTheRemoval()
    {
        await Decorator(MemberManageOn(Team)).RemoveMemberAsync(Team, Owner);

        Received.InOrder(() =>
        {
            _inner.RemoveMemberAsync(Team, Owner);
            _participant.MemberRemovedAsync(Team, Owner, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RemoveMember_ThatFails_CleansUpNothing()
    {
        _inner.RemoveMemberAsync(Team, Owner).Returns<Task>(_ => throw new InvalidOperationException("The owner cannot leave."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Decorator(MemberManageOn(Team)).RemoveMemberAsync(Team, Owner));

        await _participant.DidNotReceiveWithAnyArgs().MemberRemovedAsync(default, default, default);
    }

    [Fact]
    public async Task RemoveMember_Unauthorized_CleansUpNothing()
    {
        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => Decorator().RemoveMemberAsync(Team, Owner));

        await _participant.DidNotReceiveWithAnyArgs().MemberRemovedAsync(default, default, default);
    }

    [Fact]
    public async Task LeaveTeam_CleansUpAfterTheCurrentUser()
    {
        var user = Substitute.For<IUser>();
        user.Key.Returns(Owner);
        _users.GetCurrentUserAsync().Returns(user);

        await Decorator().LeaveTeamAsync(Team);

        Received.InOrder(() =>
        {
            _inner.LeaveTeamAsync(Team);
            _participant.MemberRemovedAsync(Team, Owner, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RemoveUserFromAllTeams_CleansUpEveryTeamTheyWereIn_ReadBeforeRemoving()
    {
        IReadOnlyList<ITeam> userTeams = [TeamNamed("a"), TeamNamed("b")];
        _inner.GetTeamsForUserWithAccessLevelAsync(Owner, Arg.Any<AccessLevel>()).Returns(Task.FromResult<IReadOnlyList<ITeam>>([]));
        _inner.GetTeamsForUserWithAccessLevelAsync(Owner, AccessLevel.User).Returns(Task.FromResult(userTeams));
        _inner.RemoveUserFromAllTeamsAsync(Owner).Returns(2);

        var removed = await Decorator(new Claim(TeamClaimTypes.SystemScope, SystemUserScopes.Manage)).RemoveUserFromAllTeamsAsync(Owner);

        Assert.Equal(2, removed);
        Received.InOrder(() =>
        {
            _inner.GetTeamsForUserWithAccessLevelAsync(Owner, AccessLevel.User);
            _inner.RemoveUserFromAllTeamsAsync(Owner);
            _participant.MemberRemovedAsync("a", Owner, Arg.Any<CancellationToken>());
            _participant.MemberRemovedAsync("b", Owner, Arg.Any<CancellationToken>());
        });
    }
}
