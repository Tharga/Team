using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Moving a user's name between this application and the directory, in either direction, after a
/// verification has shown that they differ.
/// </summary>
public class DirectoryNameSyncTests
{
    private sealed record FakeUser(string Key, string Name, string DirectoryId, string EMail = null) : IUser
    {
        public string Identity => "id-" + Key;
        public string Icon => null;
        public DateTime? LastSeen => null;
    }

    private sealed class FakeUserService(IUser user) : IUserService
    {
        public List<(string Key, string Name)> NameWrites { get; } = [];
        public List<(string Key, string DirectoryId)> Relinks { get; } = [];
        public Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal = null) => Task.FromResult(user);
        public IAsyncEnumerable<IUser> GetAsync() => new[] { user }.ToAsyncEnumerable();
        public Task<IUser> GetUserByKeyAsync(string userKey) => Task.FromResult(user.Key == userKey ? user : null);
        public Task SeedUserNameAsync(string userKey, string name) => Task.CompletedTask;
        public Task SetUserNameAsync(string userKey, string name) { NameWrites.Add((userKey, name)); return Task.CompletedTask; }
        public Task SetUserDirectoryIdAsync(string userKey, string directoryId) { Relinks.Add((userKey, directoryId)); return Task.CompletedTask; }
    }

    private sealed class FakeDirectory(DirectoryVerificationResult verification) : IUserDirectoryService
    {
        public List<(string DirectoryId, string Name)> NameWrites { get; } = [];

        public Task<DirectoryVerificationResult> VerifyUserAsync(IUser user, CancellationToken cancellationToken = default)
            => Task.FromResult(verification);

        public Task SetUserNameAsync(string directoryId, string name, CancellationToken cancellationToken = default)
        {
            NameWrites.Add((directoryId, name));
            return Task.CompletedTask;
        }

        public Task DeleteUserAsync(string directoryId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IAsyncEnumerable<DirectoryUser> GetUsersAsync(CancellationToken cancellationToken = default) => AsyncEnumerable.Empty<DirectoryUser>();
    }

    private static (UserManagementService Sut, FakeUserService Users, FakeDirectory Directory) Build(
        IUser user, DirectoryVerificationResult verification = null, bool writeNameToDirectory = true)
    {
        var users = new FakeUserService(user);
        var directory = new FakeDirectory(verification ?? new DirectoryVerificationResult(DirectoryUserStatus.Found, user.DirectoryId, "Directory Name"));
        return (new UserManagementService(users, teamService: null, directory, writeNameToDirectory), users, directory);
    }

    [Fact]
    public async Task UseDirectoryName_SetsTheLocalNameFromTheDirectory_AndWritesNothingBack()
    {
        var (sut, users, directory) = Build(new FakeUser("u-1", "unknown", "oid-1"),
            new DirectoryVerificationResult(DirectoryUserStatus.Found, "oid-1", " Daniel Bohlin "));

        var name = await sut.UseDirectoryNameAsync("u-1");

        Assert.Equal("Daniel Bohlin", name);
        Assert.Equal([("u-1", "Daniel Bohlin")], users.NameWrites);
        Assert.Empty(directory.NameWrites);
    }

    /// <summary>Taking "unknown" from the directory would overwrite a real name with none.</summary>
    [Theory]
    [InlineData("unknown")]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData(null)]
    public async Task UseDirectoryName_RefusesAPlaceholder(string directoryName)
    {
        var (sut, users, _) = Build(new FakeUser("u-1", "Daniel", "oid-1"),
            new DirectoryVerificationResult(DirectoryUserStatus.Found, "oid-1", directoryName));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.UseDirectoryNameAsync("u-1"));
        Assert.Empty(users.NameWrites);
    }

    [Theory]
    [InlineData(DirectoryUserStatus.NotFound)]
    [InlineData(DirectoryUserStatus.NotLinked)]
    public async Task UseDirectoryName_RefusesAUserTheDirectoryDoesNotHave(DirectoryUserStatus status)
    {
        var (sut, users, _) = Build(new FakeUser("u-1", "Daniel", "oid-1"),
            new DirectoryVerificationResult(status, null, "Somebody"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.UseDirectoryNameAsync("u-1"));
        Assert.Empty(users.NameWrites);
    }

    /// <summary>Resolving by email links the user, exactly as a verification does.</summary>
    [Fact]
    public async Task UseDirectoryName_RelinksAUserResolvedByEmail()
    {
        var (sut, users, _) = Build(new FakeUser("u-1", null, null, "a@b.c"),
            new DirectoryVerificationResult(DirectoryUserStatus.Found, "oid-9", "Alice"));

        await sut.UseDirectoryNameAsync("u-1");

        Assert.Equal([("u-1", "oid-9")], users.Relinks);
    }

    /// <summary>A host's own implementation written before these operations existed still compiles, and says what it lacks.</summary>
    [Fact]
    public async Task HostImplementation_WithoutNameSync_SaysWhatItDoesNotImplement()
    {
        IUserManagementService sut = new HostUserManagementWithoutNameSync();

        var e = await Assert.ThrowsAsync<NotSupportedException>(() => sut.UseDirectoryNameAsync("u-1"));

        Assert.Contains(nameof(IUserManagementService.UseDirectoryNameAsync), e.Message);
        Assert.Contains(nameof(HostUserManagementWithoutNameSync), e.Message);
    }

    private sealed class HostUserManagementWithoutNameSync : IUserManagementService
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
