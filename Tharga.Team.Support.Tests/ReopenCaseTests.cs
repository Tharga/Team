using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Opening a closed case again.
/// </summary>
/// <remarks>
/// <b>Reopening is what makes closing safe to do.</b> Without it, closing is a decision somebody has to be
/// certain about and the safe move is to leave every case open — which is how a case list stops being read.
/// </remarks>
public class ReopenCaseTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Bob = "bob-subject";

    [Fact]
    public async Task AClosedCase_CanBeReopenedByItsAuthor()
    {
        var (service, _) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await service.CloseCaseAsync(TeamA, raised.Id);

        await service.ReopenCaseAsync(TeamA, raised.Id);

        var reopened = await service.GetCaseAsync(TeamA, raised.Id);
        Assert.Equal(SupportCaseStatus.Open, reopened.Status);
    }

    /// <summary>
    /// The closure has to be cleared, not merely overridden by the status: anything reading
    /// <see cref="SupportCase.ClosedReason"/> would otherwise still see a reason on an open case.
    /// </summary>
    [Fact]
    public async Task Reopening_ClearsTheWholeClosure()
    {
        var (service, _) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await service.CloseCaseAsync(TeamA, raised.Id);

        await service.ReopenCaseAsync(TeamA, raised.Id);

        var reopened = await service.GetCaseAsync(TeamA, raised.Id);
        Assert.Null(reopened.ClosedAt);
        Assert.Null(reopened.ClosedBy);
        Assert.Null(reopened.ClosedReason);
    }

    [Fact]
    public async Task Reopening_KeepsTheHistory_AndRecordsItself()
    {
        var (service, _) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await service.ReplyToCaseAsync(TeamA, raised.Id, "Looking into it.");
        await service.CloseCaseAsync(TeamA, raised.Id);

        await service.ReopenCaseAsync(TeamA, raised.Id);

        var messages = await service.GetMessagesAsync(TeamA, raised.Id);

        Assert.Equal("The export is empty.", messages.Items[0].Body);
        Assert.Equal("Looking into it.", messages.Items[1].Body);
        Assert.Contains("closed", messages.Items[2].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reopened", messages.Items[3].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SupportMessageKind.System, messages.Items[3].Kind);
    }

    /// <summary>
    /// Two people looking at the same closed case both press reopen. The second must see an open case, not an
    /// error about it already being open.
    /// </summary>
    [Fact]
    public async Task ReopeningAnOpenCase_DoesNothingAndDoesNotThrow()
    {
        var (service, _) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");

        await service.ReopenCaseAsync(TeamA, raised.Id);
        await service.ReopenCaseAsync(TeamA, raised.Id);

        var current = await service.GetCaseAsync(TeamA, raised.Id);
        Assert.Equal(SupportCaseStatus.Open, current.Status);
        Assert.Equal(1, current.MessageCount);
    }

    [Fact]
    public async Task AnAutoClosedCase_CanBeReopened()
    {
        var store = new InMemorySupportCaseStore();
        var (service, _) = Build(store);
        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");

        await store.CloseCaseAsync(TeamA, raised.Id, DateTime.UtcNow, SupportCaseActors.AutoClose,
            new SupportMessage
            {
                Sequence = 0,
                Kind = SupportMessageKind.System,
                Body = "Closed automatically after inactivity.",
                SentAt = DateTime.UtcNow
            });

        var closed = await service.GetCaseAsync(TeamA, raised.Id);
        Assert.Equal(SupportCaseClosureReason.Inactivity, closed.ClosedReason);

        await service.ReopenCaseAsync(TeamA, raised.Id);

        var reopened = await service.GetCaseAsync(TeamA, raised.Id);
        Assert.Equal(SupportCaseStatus.Open, reopened.Status);
        Assert.Null(reopened.ClosedReason);
    }

    /// <summary>
    /// Somebody who could not answer a case has no business changing its state.
    /// </summary>
    [Fact]
    public async Task SomebodyElsesCase_CannotBeReopenedWithoutAScope()
    {
        var store = new InMemorySupportCaseStore();
        var (asAlice, _) = Build(store);
        var raised = await asAlice.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await asAlice.CloseCaseAsync(TeamA, raised.Id);

        var (asBob, _) = Build(store, Bob);

        var refused = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => asBob.ReopenCaseAsync(TeamA, raised.Id));

        Assert.Contains("reopen", refused.Message);
    }

    [Fact]
    public async Task AHolderOfSupportManage_CanReopenAnybodysCase()
    {
        var store = new InMemorySupportCaseStore();
        var (asAlice, _) = Build(store);
        var raised = await asAlice.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await asAlice.CloseCaseAsync(TeamA, raised.Id);

        var (asSupport, _) = Build(store, Bob, SupportScopes.Manage);

        await asSupport.ReopenCaseAsync(TeamA, raised.Id);

        var reopened = await asAlice.GetCaseAsync(TeamA, raised.Id);
        Assert.Equal(SupportCaseStatus.Open, reopened.Status);
    }

    [Fact]
    public async Task Reopening_NotifiesTheHost()
    {
        var notifier = Substitute.For<ISupportCaseNotifier>();
        var (service, _) = Build(notifier: notifier);
        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await service.CloseCaseAsync(TeamA, raised.Id);

        await service.ReopenCaseAsync(TeamA, raised.Id);

        notifier.Received(1).Notify(Arg.Is<SupportCaseUpdatedEventArgs>(x =>
            x.CaseId == raised.Id && x.Change == SupportCaseChange.Reopened));
    }

    /// <summary>
    /// A store written before reopening existed keeps compiling, and says plainly that it cannot do this
    /// rather than silently leaving the case closed while telling the caller it opened.
    /// </summary>
    [Fact]
    public async Task AStoreThatDoesNotSupportReopening_SaysSo()
    {
        var store = new StoreWithoutReopen(new InMemorySupportCaseStore());

        var refused = await Assert.ThrowsAsync<NotSupportedException>(
            () => ((ISupportCaseStore)store).ReopenCaseAsync(TeamA, "case-1", null));

        Assert.Contains(nameof(StoreWithoutReopen), refused.Message);
        Assert.Contains("ReopenCaseAsync", refused.Message);
    }

    private static (ISupportCaseService Service, InMemorySupportCaseStore Store) Build(
        InMemorySupportCaseStore store = null,
        string identity = Alice,
        string scope = null,
        ISupportCaseNotifier notifier = null)
    {
        store ??= new InMemorySupportCaseStore();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity),
            new(ClaimTypes.Name, identity),
            new(TeamClaimTypes.TeamKey, TeamA)
        };

        if (scope != null) claims.Add(new Claim(TeamClaimTypes.Scope, scope));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        var service = new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(store, authorizer, TimeProvider.System, null, notifier),
            authorizer);

        return (service, store);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }

}
