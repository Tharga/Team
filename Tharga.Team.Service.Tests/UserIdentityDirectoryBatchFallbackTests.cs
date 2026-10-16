namespace Tharga.Team.Service.Tests;

/// <summary>
/// The default <see cref="IUserIdentityDirectory.GetIdentitiesByIdsAsync"/> — what every directory that
/// implements only the single read gets. A hand-written fake rather than a substitute, because a proxy
/// would replace the default member under test.
/// </summary>
public class UserIdentityDirectoryBatchFallbackTests
{
    private sealed class SingleReadDirectory : IUserIdentityDirectory
    {
        private readonly Dictionary<string, DirectoryUserIdentity[]> _users;
        public List<string> Reads { get; } = [];

        public SingleReadDirectory(Dictionary<string, DirectoryUserIdentity[]> users) => _users = users;

        public Task<IReadOnlyList<DirectoryUserIdentity>> GetIdentitiesAsync(string directoryId, CancellationToken cancellationToken = default)
        {
            Reads.Add(directoryId);
            return _users.TryGetValue(directoryId, out var identities)
                ? Task.FromResult<IReadOnlyList<DirectoryUserIdentity>>(identities)
                : throw new InvalidOperationException($"Directory user '{directoryId}' was not found.");
        }
    }

    private static readonly DirectoryUserIdentity Email = new("emailAddress", "contoso", "a@b.c");
    private static readonly DirectoryUserIdentity Federated = new("federated", "https://idp.example/", "sub-1");

    [Fact]
    public async Task ReadsEachIdOnce_InOrder()
    {
        var directory = new SingleReadDirectory(new() { ["a"] = [Email], ["b"] = [Federated, Email] });
        IUserIdentityDirectory sut = directory;

        var result = await sut.GetIdentitiesByIdsAsync(["a", "b", "a"]);

        Assert.Equal(["a", "b"], directory.Reads);
        Assert.Equal(["a", "b"], result.Select(x => x.DirectoryId));
        Assert.Equal([Federated, Email], result[1].Identities);
    }

    [Fact]
    public async Task UnknownId_IsLeftOut_OthersStillReturned()
    {
        IUserIdentityDirectory sut = new SingleReadDirectory(new() { ["a"] = [Email] });

        var result = await sut.GetIdentitiesByIdsAsync(["gone", "a"]);

        Assert.Equal("a", Assert.Single(result).DirectoryId);
    }

    [Fact]
    public async Task NullAndEmptyIds_AreIgnoredWithoutARead()
    {
        var directory = new SingleReadDirectory(new());
        IUserIdentityDirectory sut = directory;

        var result = await sut.GetIdentitiesByIdsAsync([null, ""]);

        Assert.Empty(result);
        Assert.Empty(directory.Reads);
    }

    [Fact]
    public async Task OtherFailures_AreNotSwallowed()
    {
        IUserIdentityDirectory sut = new ThrowingDirectory();

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetIdentitiesByIdsAsync(["a"]));
    }

    private sealed class ThrowingDirectory : IUserIdentityDirectory
    {
        public Task<IReadOnlyList<DirectoryUserIdentity>> GetIdentitiesAsync(string directoryId, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Directory unavailable");
    }
}
