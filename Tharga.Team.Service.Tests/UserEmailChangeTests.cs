using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// An administrator setting a user's email address: one plain address, trimmed, written locally and nowhere else,
/// with the address it replaced reported so the audit log can say what it was changed from.
/// </summary>
public class UserEmailChangeTests
{
    private sealed record FakeUser(string Key, string Identity, string EMail) : IUser;

    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly IUserDirectoryService _directory = Substitute.For<IUserDirectoryService>();
    private readonly UserManagementService _sut;

    public UserEmailChangeTests()
    {
        _userService.GetUserByKeyAsync(Arg.Any<string>()).Returns((IUser)null);
        _userService.GetUserByKeyAsync("u1").Returns(new FakeUser("u1", "alice", "alice@example.com"));
        _userService.GetUserByKeyAsync("u2").Returns(new FakeUser("u2", "bo", null));

        _sut = new UserManagementService(_userService, Substitute.For<ITeamService>(), _directory, writeNameToDirectory: true);
    }

    [Fact]
    public async Task TheAddress_IsWrittenTrimmed_AndTheOneItReplacedIsReported()
    {
        var result = await _sut.SetUserEmailAsync("u1", "  Alice.New@example.com ");

        await _userService.Received(1).SetUserEmailAsync("u1", "Alice.New@example.com");
        Assert.Equal("alice@example.com", result.Previous);
    }

    [Fact]
    public async Task AUserWithNoAddress_IsReportedAsReplacingNothing()
    {
        var result = await _sut.SetUserEmailAsync("u2", "bo@example.com");

        await _userService.Received(1).SetUserEmailAsync("u2", "bo@example.com");
        Assert.Null(result.Previous);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("alice")]
    [InlineData("alice@")]
    [InlineData("@example.com")]
    [InlineData("alice@@example.com")]
    [InlineData("Alice <alice@example.com>")]
    [InlineData("alice@example.com (work)")]
    [InlineData("alice@example.com, bo@example.com")]
    [InlineData("alice@example.com;bo@example.com")]
    public async Task WhatIsNotOnePlainAddress_IsRefused_BeforeTheStoreIsTouched(string email)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.SetUserEmailAsync("u1", email));

        await _userService.DidNotReceiveWithAnyArgs().SetUserEmailAsync(default, default);
        await _userService.DidNotReceiveWithAnyArgs().GetUserByKeyAsync(default);
    }

    [Fact]
    public async Task AUserWhoDoesNotExist_IsRefused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SetUserEmailAsync("nobody", "x@example.com"));

        await _userService.DidNotReceiveWithAnyArgs().SetUserEmailAsync(default, default);
    }

    [Fact]
    public async Task TheAddress_StaysLocal_EvenWhereNamesAreWrittenToTheDirectory()
    {
        await _sut.SetUserEmailAsync("u1", "alice.new@example.com");

        Assert.Empty(_directory.ReceivedCalls());
    }

    [Fact]
    public async Task TheAddress_IsNotCheckedAgainstOtherUsers()
    {
        await _sut.SetUserEmailAsync("u1", "shared@example.com");

        _ = _userService.DidNotReceive().GetAsync();
    }

    [Fact]
    public async Task AStoreThatDoesNotImplementIt_SaysSo_InsteadOfReportingASuccess()
    {
        var store = new StoreWithoutEmail();
        var sut = new UserManagementService(store, Substitute.For<ITeamService>());

        var e = await Assert.ThrowsAsync<NotSupportedException>(() => sut.SetUserEmailAsync("u1", "x@example.com"));

        Assert.Contains(nameof(IUserService.SetUserEmailAsync), e.Message);
        Assert.Contains(nameof(StoreWithoutEmail), e.Message);
    }

    private sealed class StoreWithoutEmail : IUserService
    {
        public Task<IUser> GetCurrentUserAsync(System.Security.Claims.ClaimsPrincipal claimsPrincipal = null) => Task.FromResult<IUser>(null);
        public IAsyncEnumerable<IUser> GetAsync() => AsyncEnumerable.Empty<IUser>();
        public Task SeedUserNameAsync(string userKey, string name) => Task.CompletedTask;
        public Task SetUserNameAsync(string userKey, string name) => Task.CompletedTask;
        public Task<IUser> GetUserByKeyAsync(string userKey) => Task.FromResult<IUser>(new FakeUser(userKey, userKey, "old@example.com"));
    }
}
