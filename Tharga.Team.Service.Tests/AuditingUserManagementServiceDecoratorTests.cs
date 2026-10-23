using Microsoft.AspNetCore.Http;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Audit coverage for <see cref="AuditingUserManagementServiceDecorator"/>: per-user verify logs outcome,
/// bulk verify logs one summary entry with the processed count, delete logs team count + directory
/// result (and logs failure entries when the inner call throws). The directory-only listing is a read
/// and is not audited.
/// </summary>
public class AuditingUserManagementServiceDecoratorTests
{
    private readonly IUserManagementService _inner = Substitute.For<IUserManagementService>();
    private readonly FakeAuditBackend _backend;
    private readonly AuditingUserManagementServiceDecorator _sut;

    public AuditingUserManagementServiceDecoratorTests()
    {
        var (logger, backend) = FakeAuditLoggerFactory.Create();
        _backend = backend;
        _sut = new AuditingUserManagementServiceDecorator(_inner, logger, Substitute.For<IHttpContextAccessor>());
    }

    [Fact]
    public async Task Verify_LogsOutcome()
    {
        _inner.VerifyUserAsync("u-1", Arg.Any<CancellationToken>())
            .Returns(new DirectoryVerificationResult(DirectoryUserStatus.Disabled, "oid-1"));

        await _sut.VerifyUserAsync("u-1");

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("user", entry.Feature);
        Assert.Equal("verify", entry.Action);
        Assert.True(entry.Success);
        Assert.Equal("u-1", entry.Metadata[AuditMetadataKeys.UserKey]);
        Assert.Equal(nameof(DirectoryUserStatus.Disabled), entry.Metadata[AuditMetadataKeys.DirectoryStatus]);
    }

    [Fact]
    public async Task Verify_InnerThrows_LogsFailureAndRethrows()
    {
        _inner.VerifyUserAsync("u-1", Arg.Any<CancellationToken>())
            .Returns<Task<DirectoryVerificationResult>>(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.VerifyUserAsync("u-1"));

        var entry = Assert.Single(_backend.Entries);
        Assert.False(entry.Success);
        Assert.Equal("boom", entry.ErrorMessage);
    }

    [Fact]
    public async Task VerifyAll_LogsSingleSummaryWithCount()
    {
        _inner.VerifyAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new UserVerificationResult("u-1", new DirectoryVerificationResult(DirectoryUserStatus.Found, "a")),
            new UserVerificationResult("u-2", new DirectoryVerificationResult(DirectoryUserStatus.NotFound))
        }.ToAsyncEnumerable());

        var results = await _sut.VerifyAllAsync().ToListAsync();

        Assert.Equal(2, results.Count);
        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("verify-all", entry.Action);
        Assert.True(entry.Success);
        Assert.Equal("2", entry.Metadata[AuditMetadataKeys.VerifiedCount]);
    }

    [Fact]
    public async Task SetUserEmail_LogsTheAddressReplacedAndTheNewOne()
    {
        _inner.SetUserEmailAsync("u-1", " new@example.com ", Arg.Any<CancellationToken>()).Returns(new UserEmailChangeResult("old@example.com"));

        await _sut.SetUserEmailAsync("u-1", " new@example.com ");

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("user", entry.Feature);
        Assert.Equal("set-user-email", entry.Action);
        Assert.True(entry.Success);
        Assert.Equal("u-1", entry.Metadata[AuditMetadataKeys.UserKey]);
        Assert.Equal("old@example.com", entry.Metadata[AuditMetadataKeys.UserEmailOld]);
        Assert.Equal("new@example.com", entry.Metadata[AuditMetadataKeys.UserEmailNew]);
    }

    [Fact]
    public async Task SetUserEmail_ForAUserWithNoAddress_LogsOnlyTheNewOne()
    {
        _inner.SetUserEmailAsync("u-1", "new@example.com", Arg.Any<CancellationToken>()).Returns(new UserEmailChangeResult());

        await _sut.SetUserEmailAsync("u-1", "new@example.com");

        var entry = Assert.Single(_backend.Entries);
        Assert.False(entry.Metadata.ContainsKey(AuditMetadataKeys.UserEmailOld));
        Assert.Equal("new@example.com", entry.Metadata[AuditMetadataKeys.UserEmailNew]);
    }

    [Fact]
    public async Task SetUserEmail_Refused_LogsFailureWithoutTheAddressAndRethrows()
    {
        _inner.SetUserEmailAsync("u-1", "not-an-address", Arg.Any<CancellationToken>())
            .Returns<Task<UserEmailChangeResult>>(_ => throw new ArgumentException("The email address is not a single, plain address."));

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.SetUserEmailAsync("u-1", "not-an-address"));

        var entry = Assert.Single(_backend.Entries);
        Assert.False(entry.Success);
        Assert.Equal("set-user-email", entry.Action);
        Assert.Equal("u-1", entry.Metadata[AuditMetadataKeys.UserKey]);
        Assert.False(entry.Metadata.ContainsKey(AuditMetadataKeys.UserEmailNew));
    }

    [Fact]
    public async Task Delete_LogsTeamCountAndDirectoryResult()
    {
        _inner.DeleteUserAsync("u-1", true, Arg.Any<CancellationToken>())
            .Returns(new UserDeleteResult(DirectoryDeleted: true, RemovedTeamCount: 2));

        await _sut.DeleteUserAsync("u-1", deleteFromDirectory: true);

        var entry = Assert.Single(_backend.Entries);
        Assert.Equal("delete", entry.Action);
        Assert.Equal("u-1", entry.Metadata[AuditMetadataKeys.UserKey]);
        Assert.Equal("2", entry.Metadata[AuditMetadataKeys.MemberTeamCount]);
        Assert.Equal(bool.TrueString, entry.Metadata[AuditMetadataKeys.DirectoryDeleted]);
        Assert.False(entry.Metadata.ContainsKey(AuditMetadataKeys.DirectoryError));
    }

    [Fact]
    public async Task Delete_DirectoryError_IsRecorded()
    {
        _inner.DeleteUserAsync("u-1", true, Arg.Any<CancellationToken>())
            .Returns(new UserDeleteResult(DirectoryDeleted: false, DirectoryError: "not linked", RemovedTeamCount: 0));

        await _sut.DeleteUserAsync("u-1", deleteFromDirectory: true);

        var entry = Assert.Single(_backend.Entries);
        Assert.True(entry.Success);
        Assert.Equal("not linked", entry.Metadata[AuditMetadataKeys.DirectoryError]);
    }

    [Fact]
    public async Task DirectoryOnly_IsNotAudited()
    {
        _inner.GetDirectoryOnlyUsersAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new DirectoryUser("oid-1", "N", "e@x.y", true) }.ToAsyncEnumerable());

        _ = await _sut.GetDirectoryOnlyUsersAsync().ToListAsync();

        Assert.Empty(_backend.Entries);
    }
}
