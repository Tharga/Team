using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Deleting a user cleans up their personal system keys, per <see cref="ApiKeyOptions.OwnerRemovedAction"/>
/// (Tharga/Team#314). Hygiene: authentication already refuses the keys once the owner is gone.
/// </summary>
public class PersonalSystemKeyUserDeletionTests
{
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IApiKeyAdministrationService _apiKeys = Substitute.For<IApiKeyAdministrationService>();

    public PersonalSystemKeyUserDeletionTests()
    {
        var user = Substitute.For<IUser>();
        user.Key.Returns("u1");
        _userService.GetUserByKeyAsync(Arg.Any<string>()).Returns((IUser)null);
        _userService.GetUserByKeyAsync("u1").Returns(user);

        _apiKeys.GetSystemKeysAsync().Returns(_ => ToAsyncEnumerable(
            Key("mine", "u1"), Key("mine-disabled", "u1", disabled: true), Key("theirs", "u2"), Key("shared", null)));
    }

    private static IApiKey Key(string id, string owner, bool disabled = false)
    {
        var k = Substitute.For<IApiKey>();
        k.Key.Returns(id);
        k.TeamKey.Returns((string)null);
        k.OwnerMemberKey.Returns(owner);
        k.DisabledAt.Returns(disabled ? DateTime.UtcNow : null);
        return k;
    }

    private static async IAsyncEnumerable<IApiKey> ToAsyncEnumerable(params IApiKey[] items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    private UserManagementService Build(PrivateKeyOwnerRemovedAction action)
        => new(_userService, Substitute.For<ITeamService>(), apiKeys: _apiKeys, ownerRemovedAction: action);

    [Fact]
    public async Task Delete_RemovesOnlyTheUsersPersonalKeys()
    {
        await Build(PrivateKeyOwnerRemovedAction.Delete).DeleteUserAsync("u1");

        await _apiKeys.Received(1).DeleteSystemKeyAsync("mine");
        await _apiKeys.Received(1).DeleteSystemKeyAsync("mine-disabled");
        await _apiKeys.DidNotReceive().DeleteSystemKeyAsync("theirs");
        await _apiKeys.DidNotReceive().DeleteSystemKeyAsync("shared");
        await _userService.Received(1).DeleteUserAsync("u1");
    }

    [Fact]
    public async Task Disable_KeepsTheKeysButStopsThem()
    {
        await Build(PrivateKeyOwnerRemovedAction.Disable).DeleteUserAsync("u1");

        await _apiKeys.Received(1).SetSystemKeyDisabledAsync("mine", true, Arg.Any<string>());
        await _apiKeys.DidNotReceive().SetSystemKeyDisabledAsync("mine-disabled", Arg.Any<bool>(), Arg.Any<string>());
        await _apiKeys.DidNotReceive().SetSystemKeyDisabledAsync("theirs", Arg.Any<bool>(), Arg.Any<string>());
        await _apiKeys.DidNotReceiveWithAnyArgs().DeleteSystemKeyAsync(default);
    }

    [Fact]
    public async Task WithoutApiKeys_DeletionStillWorks()
    {
        var sut = new UserManagementService(_userService, Substitute.For<ITeamService>());

        await sut.DeleteUserAsync("u1");

        await _userService.Received(1).DeleteUserAsync("u1");
    }
}
