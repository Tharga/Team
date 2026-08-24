using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// What a support case writes to the audit trail, and what it deliberately does not.
/// </summary>
/// <remarks>
/// <b>The refusal test is the one that pins the decorator order.</b> Auditing wraps authorization, so a
/// call that was refused produces no entry — auditing an operation that never happened misrepresents it, and
/// the two decorators could be composed the other way round without anything failing to compile.
/// </remarks>
public class SupportCaseAuditTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";

    [Fact]
    public async Task RaisingReplyingAndClosing_AreThreeDistinctAuditedFacts()
    {
        var (service, sink) = Build(TeamA, Alice);

        var raised = await service.RaiseCaseAsync(TeamA, "Cannot sign in", "It says my key expired.");
        await service.ReplyToCaseAsync(TeamA, raised.Id, "Any news?");
        await service.CloseCaseAsync(TeamA, raised.Id);

        Assert.Equal(["raise", "reply", "close"], sink.Entries.Select(x => x.Action));
        Assert.All(sink.Entries, e => Assert.Equal("support", e.Feature));
        Assert.All(sink.Entries, e => Assert.Equal(TeamA, e.TeamKey));
        Assert.All(sink.Entries, e => Assert.True(e.Success));
    }

    [Fact]
    public async Task AnAuditedEntry_CarriesTheCaseId()
    {
        var (service, sink) = Build(TeamA, Alice);

        var raised = await service.RaiseCaseAsync(TeamA, "Subject", "Body");

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(raised.Id, entry.Metadata[SupportAuditMetadataKeys.CaseId]);
    }

    /// <summary>
    /// A support case is where somebody pastes a password. The audit entry travels further than the case
    /// does, so the body must not be in it.
    /// </summary>
    [Fact]
    public async Task TheMessageBody_IsNeverRecorded()
    {
        const string secret = "my password is hunter2";

        var (service, sink) = Build(TeamA, Alice);

        var raised = await service.RaiseCaseAsync(TeamA, "Subject", secret);
        await service.ReplyToCaseAsync(TeamA, raised.Id, secret);

        Assert.DoesNotContain(sink.Entries.SelectMany(e => e.Metadata ?? new Dictionary<string, string>()),
            pair => pair.Value != null && pair.Value.Contains("hunter2", StringComparison.Ordinal));
    }

    /// <summary>
    /// A refused attempt is recorded, not dropped — the same choice the toolkit makes for access-level and
    /// scope denials.
    /// </summary>
    /// <remarks>
    /// <b>This is what pins the decorator order.</b> Auditing wraps authorization; composed the other way
    /// round every refusal would vanish and nothing would fail to compile. A denied attempt to reach another
    /// team's support case is more worth knowing about than a permitted one.
    /// </remarks>
    [Fact]
    public async Task ARefusedOperation_IsAuditedAsAFailure()
    {
        // Alice is a member of team A, so naming team B is refused.
        var (service, sink) = Build(TeamA, Alice);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.RaiseCaseAsync("team-b", "Subject", "Body"));

        var entry = Assert.Single(sink.Entries);
        Assert.False(entry.Success);
        Assert.Equal("raise", entry.Action);
        Assert.Equal("team-b", entry.TeamKey);
        Assert.Contains("member of that team", entry.ErrorMessage);
    }

    private static (ISupportCaseService Service, CollectingAuditLogger Sink) Build(string memberOfTeam, string subject)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, subject),
            new(TeamClaimTypes.TeamKey, memberOfTeam)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        var sink = new CollectingAuditLogger();
        var composite = new CompositeAuditLogger([sink], Options.Create(new AuditOptions()));

        var service = new AuditingSupportCaseServiceDecorator(
            new AuthorizationSupportCaseServiceDecorator(
                new SupportCaseService(new InMemorySupportCaseStore(), authorizer, TimeProvider.System),
                authorizer),
            composite,
            new AuditEntryFactory(null));

        return (service, sink);
    }

    private sealed class CollectingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Log(AuditEntry entry) => Entries.Add(entry);

        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
