using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Writing a corrected display name back to the directory. The host allows it, the caller asks for it on each
/// rename, and it is never coupled to the local write — a directory outage must not stop a name being corrected here.
/// </summary>
public class UserNameDirectoryWriteBackTests
{
    private sealed record FakeUser(string Key, string Identity, string DirectoryId) : IUser
    {
        public string Name => null;
        public string EMail => null;
        public string Icon => null;
        public DateTime? LastSeen => null;
    }

    private sealed class FakeUserService(IUser user) : IUserService
    {
        public List<(string Key, string Name)> Writes { get; } = [];
        public Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal = null) => Task.FromResult(user);
        public IAsyncEnumerable<IUser> GetAsync() => new[] { user }.ToAsyncEnumerable();
        public Task<IUser> GetUserByKeyAsync(string userKey) => Task.FromResult(user.Key == userKey ? user : null);
        public Task SeedUserNameAsync(string userKey, string name) => Task.CompletedTask;
        public Task SetUserNameAsync(string userKey, string name) { Writes.Add((userKey, name)); return Task.CompletedTask; }
    }

    private sealed class FakeDirectory : IUserDirectoryService
    {
        public List<(string DirectoryId, string Name)> Writes { get; } = [];
        public Exception ThrowOnWrite { get; set; }

        public Task SetUserNameAsync(string directoryId, string name, CancellationToken cancellationToken = default)
        {
            if (ThrowOnWrite != null) throw ThrowOnWrite;
            Writes.Add((directoryId, name));
            return Task.CompletedTask;
        }

        public Task<DirectoryVerificationResult> VerifyUserAsync(IUser user, CancellationToken cancellationToken = default)
            => Task.FromResult(new DirectoryVerificationResult(DirectoryUserStatus.Found));
        public Task DeleteUserAsync(string directoryId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IAsyncEnumerable<DirectoryUser> GetUsersAsync(CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<DirectoryUser>();
    }

    private static (UserManagementService Sut, FakeUserService Users, FakeDirectory Directory) Build(
        bool writeNameToDirectory, string directoryId = "oid-1")
    {
        var users = new FakeUserService(new FakeUser("user-1", "id-1", directoryId));
        var directory = new FakeDirectory();
        // No team service: renaming never touches one, and a stub for a 20-member interface would say
        // less about this behaviour than the null does.
        var sut = new UserManagementService(users, teamService: null, directory, writeNameToDirectory);
        return (sut, users, directory);
    }

    /// <summary>Default off: the directory is never touched unless the host allows it and the caller asks for it.</summary>
    [Fact]
    public async Task NotAsked_WritesLocallyAndNeverCallsTheDirectory()
    {
        var (sut, users, directory) = Build(writeNameToDirectory: false);

        var result = await sut.SetUserNameAsync("user-1", "Real Name", writeToDirectory: false);

        Assert.Equal([("user-1", "Real Name")], users.Writes);
        Assert.Empty(directory.Writes);
        Assert.False(result.DirectoryUpdated);
        Assert.Null(result.DirectoryError);
    }

    /// <summary>The option only allows writing. A rename the caller did not ask to write stays local.</summary>
    [Fact]
    public async Task OptionOn_NotAsked_WritesOnlyLocally()
    {
        var (sut, users, directory) = Build(writeNameToDirectory: true);

        var result = await sut.SetUserNameAsync("user-1", "Real Name", writeToDirectory: false);

        Assert.Equal([("user-1", "Real Name")], users.Writes);
        Assert.Empty(directory.Writes);
        Assert.False(result.DirectoryUpdated);
    }

    [Fact]
    public async Task OptionOn_Asked_WritesBothAndReportsSuccess()
    {
        var (sut, users, directory) = Build(writeNameToDirectory: true);

        var result = await sut.SetUserNameAsync("user-1", "Real Name", writeToDirectory: true);

        Assert.Equal([("user-1", "Real Name")], users.Writes);
        Assert.Equal([("oid-1", "Real Name")], directory.Writes);
        Assert.True(result.DirectoryUpdated);
    }

    /// <summary>Asking for a write the host has not allowed is refused before anything changes, here or there.</summary>
    [Fact]
    public async Task OptionOff_Asked_IsRefusedBeforeAnyWrite()
    {
        var (sut, users, directory) = Build(writeNameToDirectory: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SetUserNameAsync("user-1", "Real Name", writeToDirectory: true));

        Assert.Empty(users.Writes);
        Assert.Empty(directory.Writes);
    }

    [Fact]
    public async Task NoDirectory_Asked_IsRefusedBeforeAnyWrite()
    {
        var users = new FakeUserService(new FakeUser("user-1", "id-1", "oid-1"));
        var sut = new UserManagementService(users, teamService: null, directoryService: null, writeNameToDirectory: true);

        await Assert.ThrowsAsync<NotSupportedException>(() => sut.SetUserNameAsync("user-1", "Real Name", writeToDirectory: true));

        Assert.Empty(users.Writes);
    }

    /// <summary>An unlinked user has no directory account to write to, so asking to write there cannot be honoured.</summary>
    [Fact]
    public async Task UnlinkedUser_Asked_IsRefusedBeforeAnyWrite()
    {
        var (sut, users, directory) = Build(writeNameToDirectory: true, directoryId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SetUserNameAsync("user-1", "Real Name", writeToDirectory: true));

        Assert.Empty(users.Writes);
        Assert.Empty(directory.Writes);
    }

    /// <summary>Writing a placeholder would replace whatever the directory holds with nothing better.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public async Task PlaceholderName_Asked_IsRefusedBeforeAnyWrite(string name)
    {
        var (sut, users, directory) = Build(writeNameToDirectory: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SetUserNameAsync("user-1", name, writeToDirectory: true));

        Assert.Empty(users.Writes);
        Assert.Empty(directory.Writes);
    }

    /// <summary>
    /// The case the whole shape exists for: they fail independently. Coupling them would let a Graph
    /// outage block renaming a user in this application.
    /// </summary>
    [Fact]
    public async Task DirectoryFailure_DoesNotRollBackTheLocalWrite()
    {
        var (sut, users, directory) = Build(writeNameToDirectory: true);
        directory.ThrowOnWrite = new InvalidOperationException("Graph is unavailable");

        var result = await sut.SetUserNameAsync("user-1", "Real Name", writeToDirectory: true);

        Assert.Equal([("user-1", "Real Name")], users.Writes);
        Assert.False(result.DirectoryUpdated);
        Assert.Contains("Graph is unavailable", result.DirectoryError);
    }

    /// <summary>A directory implementation that cannot write says so rather than silently accepting.</summary>
    [Fact]
    public async Task DirectoryWithoutAWritePath_Throws_AndIsReportedNotSwallowed()
    {
        var users = new FakeUserService(new FakeUser("user-1", "id-1", "oid-1"));
        var sut = new UserManagementService(users, teamService: null, new ReadOnlyDirectory(), writeNameToDirectory: true);

        var result = await sut.SetUserNameAsync("user-1", "Real Name", writeToDirectory: true);

        Assert.Single(users.Writes);
        Assert.Contains("does not implement", result.DirectoryError);
    }

    private sealed class ReadOnlyDirectory : IUserDirectoryService
    {
        public Task<DirectoryVerificationResult> VerifyUserAsync(IUser user, CancellationToken cancellationToken = default)
            => Task.FromResult(new DirectoryVerificationResult(DirectoryUserStatus.Found));
        public Task DeleteUserAsync(string directoryId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IAsyncEnumerable<DirectoryUser> GetUsersAsync(CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<DirectoryUser>();
    }
}
