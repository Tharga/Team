using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>Reading a case changes the reader's unread count, so a live indicator has to hear of it.</summary>
public class SupportReadIsAnnouncedTests
{
    private const string TeamA = "team-a";

    [Fact]
    public async Task MarkingACaseRead_AnnouncesARead()
    {
        var notifier = new SupportCaseNotifier();
        var changes = new List<SupportCaseUpdatedEventArgs>();
        var service = Service(notifier);
        var raised = await service.RaiseCaseAsync(TeamA, "Subject", "Body");

        notifier.CaseUpdated += (_, e) => changes.Add(e);
        await service.MarkReadAsync(TeamA, raised.Id);

        var change = Assert.Single(changes);
        Assert.Equal(SupportCaseChange.Read, change.Change);
        Assert.Equal(TeamA, change.TeamKey);
        Assert.Equal(raised.Id, change.CaseId);
    }

    [Fact]
    public async Task MarkingAMissingCaseRead_AnnouncesNothing()
    {
        var notifier = new SupportCaseNotifier();
        var changes = new List<SupportCaseUpdatedEventArgs>();
        notifier.CaseUpdated += (_, e) => changes.Add(e);

        await Service(notifier).MarkReadAsync(TeamA, "no-such-case");

        Assert.Empty(changes);
    }

    private static ISupportCaseService Service(ISupportCaseNotifier notifier)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "alice"),
            new(ClaimTypes.Name, "Alice"),
            new(TeamClaimTypes.TeamKey, TeamA)
        };

        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        return new SupportCaseService(new InMemorySupportCaseStore(), authorizer, TimeProvider.System, notifier: notifier);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
