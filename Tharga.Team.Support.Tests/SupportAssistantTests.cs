using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Who answers a case, and what that does to the support queue.
/// </summary>
/// <remarks>
/// <b>The queue behaviour is the part worth guarding.</b> An assistant answer takes a case out of the support
/// queue and a customer reply puts it back, which is the rule read state already uses: a position, not a
/// flag. Wrong in one direction and every answered case sits in the queue until people stop reading it;
/// wrong in the other and a customer saying "that did not help" is never seen.
/// </remarks>
public class SupportAssistantTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Answer = "Reindex the export and try again.";
    private const string Question = "The export is empty.";

    [Fact]
    public async Task WithNoResponderRegistered_RunningTheAssistant_DoesNothing()
    {
        var (service, store) = Build(responder: null, withResponder: false);
        var raised = await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        Assert.False(await service.RunAssistantAsync(TeamA, raised.Id));
        Assert.Single((await store.GetMessagesAsync(TeamA, raised.Id, null, 50)).Items);
    }

    /// <summary>Asking for an assistant a host never configured still raises the case.</summary>
    [Fact]
    public async Task AskingForAnAssistantWithNoneRegistered_StillRaisesTheCase()
    {
        var (service, _) = Build(responder: null, withResponder: false);

        var raised = await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        Assert.NotNull(raised);
        Assert.Equal(SupportAssistantState.None, raised.AssistantState);
    }

    [Fact]
    public async Task RaisingWithAnAssistant_MarksTheCaseActive()
    {
        var (service, _) = Build();

        var raised = await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        Assert.Equal(SupportAssistantState.Active, raised.AssistantState);
    }

    [Fact]
    public async Task RaisingWithoutOne_NeverGetsAnAssistant()
    {
        var (service, store) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, Question);

        Assert.Equal(SupportAssistantState.None, raised.AssistantState);
        Assert.False(await service.RunAssistantAsync(TeamA, raised.Id));
        Assert.Single((await store.GetMessagesAsync(TeamA, raised.Id, null, 50)).Items);
    }

    [Fact]
    public async Task TheAnswer_IsRecordedAsAnAssistantEntry()
    {
        var (service, store) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        Assert.True(await service.RunAssistantAsync(TeamA, raised.Id));

        var last = (await store.GetMessagesAsync(TeamA, raised.Id, null, 50)).Items[^1];
        Assert.Equal(SupportMessageKind.Assistant, last.Kind);
        Assert.Equal(Answer, last.Body);
        Assert.Null(last.AuthorIdentity);
    }

    [Fact]
    public async Task AnAssistantAnswer_TakesTheCaseOutOfTheSupportQueue()
    {
        var (service, store) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        Assert.Equal(1, await store.GetAwaitingSupportCountAsync(TeamA));

        await service.RunAssistantAsync(TeamA, raised.Id);

        Assert.Equal(0, await store.GetAwaitingSupportCountAsync(TeamA));
    }

    /// <summary>The half that makes the other half safe.</summary>
    [Fact]
    public async Task ACustomerReplyAfterAnAnswer_PutsTheCaseBackInTheQueue()
    {
        var (service, store) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);
        await service.RunAssistantAsync(TeamA, raised.Id);

        await service.ReplyToCaseAsync(TeamA, raised.Id, "That did not help.");

        Assert.Equal(1, await store.GetAwaitingSupportCountAsync(TeamA));
    }

    [Fact]
    public async Task AskingForAPerson_StopsTheAssistantAnswering()
    {
        var (service, store) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        await service.RequestHumanAsync(TeamA, raised.Id);

        Assert.Equal(SupportAssistantState.HandedOff, (await store.GetCaseAsync(TeamA, raised.Id)).AssistantState);
        Assert.False(await service.RunAssistantAsync(TeamA, raised.Id));
    }

    /// <summary>So a component can offer the action without first working out whether it applies.</summary>
    [Fact]
    public async Task AskingForAPerson_OnACaseThatNeverHadAnAssistant_IsNotAnError()
    {
        var (service, store) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, Question);

        await service.RequestHumanAsync(TeamA, raised.Id);

        Assert.Equal(SupportAssistantState.None, (await store.GetCaseAsync(TeamA, raised.Id)).AssistantState);
    }

    [Fact]
    public async Task ADecliningAssistant_AppendsNothingAndLeavesTheCaseWaiting()
    {
        var (service, store) = Build(new FakeResponder(null));
        var raised = await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        Assert.False(await service.RunAssistantAsync(TeamA, raised.Id));
        Assert.Single((await store.GetMessagesAsync(TeamA, raised.Id, null, 50)).Items);
        Assert.Equal(1, await store.GetAwaitingSupportCountAsync(TeamA));
    }

    /// <summary>
    /// The responder is handed the case and its transcript, and holds no service to reach any further with.
    /// </summary>
    [Fact]
    public async Task TheResponder_IsGivenOnlyTheCaseItIsAnswering()
    {
        var responder = new FakeResponder(Answer);
        var (service, _) = Build(responder);
        var raised = await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        await service.RunAssistantAsync(TeamA, raised.Id);

        Assert.Equal(raised.Id, responder.SeenCase.Id);
        Assert.Equal(TeamA, responder.SeenCase.TeamKey);
        Assert.Equal([Question], responder.SeenTranscript.Select(x => x.Body));
    }

    private sealed class FakeResponder(string answer) : ISupportResponder
    {
        public SupportCase SeenCase { get; private set; }
        public IReadOnlyList<SupportMessage> SeenTranscript { get; private set; }

        public Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, CancellationToken cancellationToken = default)
        {
            SeenCase = supportCase;
            SeenTranscript = transcript;

            return Task.FromResult(answer == null
                ? SupportAnswer.Declined("Nothing to say.")
                : SupportAnswer.FromBody(answer));
        }
    }

    private static (ISupportCaseService Service, InMemorySupportCaseStore Store) Build(
        ISupportResponder responder = null,
        bool withResponder = true)
    {
        if (withResponder) responder ??= new FakeResponder(Answer);

        var store = new InMemorySupportCaseStore();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Alice),
            new Claim(ClaimTypes.Name, Alice),
            new Claim(TeamClaimTypes.TeamKey, TeamA)
        ], "test"));

        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        var service = new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(store, authorizer, TimeProvider.System, null, null, responder),
            authorizer);

        return (service, store);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
