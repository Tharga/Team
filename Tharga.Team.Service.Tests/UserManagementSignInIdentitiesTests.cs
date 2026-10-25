namespace Tharga.Team.Service.Tests;

/// <summary>
/// <see cref="UserManagementService.GetSignInIdentitiesAsync"/>: user keys resolve to directory ids, the
/// directory is asked once for the whole set, and the answer is mapped back to user keys. Unlinked and
/// unknown users are left out, and no identity directory means an empty answer rather than a failure.
/// </summary>
public class UserManagementSignInIdentitiesTests
{
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly ITeamService _teamService = Substitute.For<ITeamService>();
    private readonly IUserIdentityDirectory _identities = Substitute.For<IUserIdentityDirectory>();

    private static readonly DirectoryUserIdentity Email = new("emailAddress", "contoso", "a@b.c");

    private sealed record TestUser : IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string Name { get; init; }
        public string EMail { get; init; }
        public string DirectoryId { get; init; }
    }

    private UserManagementService Build() => new(_userService, _teamService, identityDirectory: _identities);

    private void GivenUser(string key, string directoryId)
        => _userService.GetUserByKeyAsync(key).Returns(new TestUser { Key = key, Identity = $"id-{key}", DirectoryId = directoryId });

    [Fact]
    public async Task OneBatchRead_MappedBackToUserKeys()
    {
        GivenUser("u-1", "oid-1");
        GivenUser("u-2", "oid-2");
        _identities.GetIdentitiesByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new DirectoryUserIdentities("oid-2", [Email]), new DirectoryUserIdentities("oid-1", [])]);

        var result = await Build().GetSignInIdentitiesAsync(["u-1", "u-2"]);

        await _identities.Received(1).GetIdentitiesByIdsAsync(
            Arg.Is<IEnumerable<string>>(x => x.OrderBy(y => y).SequenceEqual(new[] { "oid-1", "oid-2" })), Arg.Any<CancellationToken>());
        Assert.Equal([Email], result.Single(x => x.UserKey == "u-2").Identities);
        Assert.Empty(result.Single(x => x.UserKey == "u-1").Identities);
    }

    [Fact]
    public async Task UnlinkedAndUnknownUsers_AreNotAskedAbout()
    {
        GivenUser("u-1", "oid-1");
        GivenUser("u-unlinked", null);
        _userService.GetUserByKeyAsync("u-gone").Returns((IUser)null);
        _identities.GetIdentitiesByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new DirectoryUserIdentities("oid-1", [Email])]);

        var result = await Build().GetSignInIdentitiesAsync(["u-1", "u-unlinked", "u-gone"]);

        await _identities.Received(1).GetIdentitiesByIdsAsync(
            Arg.Is<IEnumerable<string>>(x => x.SequenceEqual(new[] { "oid-1" })), Arg.Any<CancellationToken>());
        Assert.Equal("u-1", Assert.Single(result).UserKey);
    }

    [Fact]
    public async Task NoLinkedUsers_MakesNoDirectoryCall()
    {
        GivenUser("u-unlinked", null);

        var result = await Build().GetSignInIdentitiesAsync(["u-unlinked"]);

        Assert.Empty(result);
        await _identities.DidNotReceiveWithAnyArgs().GetIdentitiesByIdsAsync(default, default);
    }

    [Fact]
    public async Task NoIdentityDirectory_ReturnsEmpty()
    {
        GivenUser("u-1", "oid-1");

        var result = await new UserManagementService(_userService, _teamService).GetSignInIdentitiesAsync(["u-1"]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DirectoryFailure_Propagates()
    {
        GivenUser("u-1", "oid-1");
        _identities.GetIdentitiesByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<DirectoryUserIdentities[]>>(_ => throw new HttpRequestException("Graph down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => Build().GetSignInIdentitiesAsync(["u-1"]));
    }

    [Fact]
    public async Task HostImplementation_DefaultMember_ReturnsEmpty()
    {
        IUserManagementService hostImplementation = new HostUserManagement();

        Assert.Empty(await hostImplementation.GetSignInIdentitiesAsync(["u-1"]));
    }

    /// <summary>A host's own implementation, written before this member existed.</summary>
    private sealed class HostUserManagement : IUserManagementService
    {
        public Task<DirectoryVerificationResult> VerifyUserAsync(string userKey, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<UserVerificationResult> VerifyAllAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<UserDeleteResult> DeleteUserAsync(string userKey, bool deleteFromDirectory = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SetUserDisabledAsync(string userKey, bool disabled, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<UserNameChangeResult> SetUserNameAsync(string userKey, string name, bool writeToDirectory, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ITeam>> GetOwnedTeamsAsync(string userKey, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<DirectoryUser> GetDirectoryOnlyUsersAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
