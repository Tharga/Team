using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The decorator that slows repeated failed invite-code resolves and makes the attempt visible
/// (Tharga/Team#256).
/// </summary>
/// <remarks>
/// Every case here runs with <c>MaxThrottleDelay</c> at zero, so the suite asserts the decision without
/// waiting for it. The delay lengths themselves are covered by <see cref="InvitationThrottleTests"/>.
/// </remarks>
public class ThrottledTeamInvitationServiceTests
{
    private const string Code = "84Fb6G_8BbXE";

    /// <summary>Behaviour on the success path is untouched — including that it is not counted.</summary>
    [Fact]
    public async Task AResolvedInvitation_IsReturnedAndNotCounted()
    {
        var (sut, backend) = Build(resolves: true, threshold: 1);

        for (var i = 0; i < 10; i++) Assert.NotNull(await sut.GetInvitationAsync(Code));

        Assert.Empty(backend.Entries);
    }

    /// <summary>
    /// A failure still answers null, indistinguishably from a malformed or unknown code. Throttling must
    /// not become a second oracle that tells a guesser they are being counted.
    /// </summary>
    [Fact]
    public async Task AFailedResolve_StillReturnsNull()
    {
        var (sut, _) = Build(resolves: false, threshold: 1);

        for (var i = 0; i < 5; i++) Assert.Null(await sut.GetInvitationAsync(Code));
    }

    /// <summary>
    /// <see cref="AuditEventType.RateLimit"/> was declared, filtered and rendered by the log view, and
    /// raised by nothing at all until this.
    /// </summary>
    [Fact]
    public async Task CrossingTheThreshold_RaisesARateLimitEntry()
    {
        var (sut, backend) = Build(resolves: false, threshold: 2);

        await sut.GetInvitationAsync(Code);
        await sut.GetInvitationAsync(Code);
        Assert.Empty(backend.Entries);

        await sut.GetInvitationAsync(Code);

        var entry = Assert.Single(backend.Entries);
        Assert.Equal(AuditEventType.RateLimit, entry.EventType);
        Assert.Equal("invitation", entry.Feature);
        Assert.False(entry.Success);
    }

    /// <summary>
    /// One entry for the run, not one per failure. Auditing every attempt past the threshold would bury the
    /// signal in exactly the noise it exists to report.
    /// </summary>
    [Fact]
    public async Task AnEnumerationRun_ProducesOneEntry_NotThousands()
    {
        var (sut, backend) = Build(resolves: false, threshold: 2);

        for (var i = 0; i < 200; i++) await sut.GetInvitationAsync(Code);

        Assert.Single(backend.Entries);
    }

    /// <summary>
    /// A failed code is one somebody guessed. Writing it to the audit log would put candidate codes in
    /// front of everyone who can read the log.
    /// </summary>
    [Fact]
    public async Task TheAuditEntry_DoesNotCarryTheAttemptedCode()
    {
        var (sut, backend) = Build(resolves: false, threshold: 1);

        await sut.GetInvitationAsync(Code);
        await sut.GetInvitationAsync(Code);

        var entry = Assert.Single(backend.Entries);
        Assert.DoesNotContain(Code, entry.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(Code, entry.MethodName ?? string.Empty, StringComparison.Ordinal);
        Assert.Null(entry.Metadata);
    }

    [Fact]
    public async Task WithThrottlingOff_NothingIsAudited()
    {
        var (sut, backend) = Build(resolves: false, threshold: 0);

        for (var i = 0; i < 50; i++) await sut.GetInvitationAsync(Code);

        Assert.Empty(backend.Entries);
    }

    private static (ITeamInvitationService Service, FakeAuditBackend Backend) Build(bool resolves, int threshold)
    {
        var inner = Substitute.For<ITeamInvitationService>();
        inner.GetInvitationAsync(Arg.Any<string>()).Returns(resolves
            ? new TeamInvitation("t-1", "Team One", "a@example.com", false)
            : null);

        var (logger, backend) = FakeAuditLoggerFactory.Create();
        var options = Microsoft.Extensions.Options.Options.Create(new InvitationOptions
        {
            ThrottleFailureThreshold = threshold,
            ThrottleWindow = TimeSpan.FromMinutes(5),
            MaxThrottleDelay = TimeSpan.Zero
        });

        var service = new ThrottledTeamInvitationService(
            inner, new InvitationThrottle(TimeProvider.System), options, null, logger);

        return (service, backend);
    }
}
