using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Looking up the team behind a short invitation code reaches the store through each team-service decorator.
/// </summary>
/// <remarks>
/// <c>GetTeamKeyByInviteKeyAsync</c> has a default body returning null, so a decorator that does not implement
/// it answers "no team" on the store's behalf — which is how every short link came to resolve to nothing
/// (Tharga/Team#272).
/// </remarks>
public class InviteTokenDecoratorForwardingTests
{
    private const string Token = "84Fb6G_8BbXE";
    private const string TeamKey = "team-1";

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public readonly List<AuditEntry> Entries = [];
        public void Log(AuditEntry entry) => Entries.Add(entry);
        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private static ITeamService Inner()
    {
        var inner = Substitute.For<ITeamService>();
        inner.GetTeamKeyByInviteKeyAsync(Token).Returns(TeamKey);
        return inner;
    }

    /// <summary>
    /// Asked by someone holding nothing, because that is who follows an invitation link: the code is the check,
    /// not a scope.
    /// </summary>
    [Fact]
    public async Task TheAuthorizationDecorator_AsksTheStore_ForACallerHoldingNoScope()
    {
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(new ClaimsPrincipal(new ClaimsIdentity())));
        ITeamService sut = new AuthorizationTeamServiceDecorator(Inner(), new TeamAuthorizer(accessor), new TeamLifecycleOptions());

        Assert.Equal(TeamKey, await sut.GetTeamKeyByInviteKeyAsync(Token));
    }

    [Fact]
    public async Task TheAuditingDecorator_AsksTheStore()
    {
        ITeamService sut = new AuditingTeamServiceDecorator(Inner(), new CompositeAuditLogger([new RecordingAuditLogger()], Options.Create(new AuditOptions())), new HttpContextAccessor());

        Assert.Equal(TeamKey, await sut.GetTeamKeyByInviteKeyAsync(Token));
    }

    /// <summary>
    /// A read, and the code in it is a bearer credential — so it is not written to a log that more people can
    /// read than may accept the invitation. Repeated failures are audited by the throttle, without the code.
    /// </summary>
    [Fact]
    public async Task TheAuditingDecorator_RecordsNothing()
    {
        var recorder = new RecordingAuditLogger();
        ITeamService sut = new AuditingTeamServiceDecorator(Inner(), new CompositeAuditLogger([recorder], Options.Create(new AuditOptions())), new HttpContextAccessor());

        await sut.GetTeamKeyByInviteKeyAsync(Token);

        Assert.Empty(recorder.Entries);
    }
}
