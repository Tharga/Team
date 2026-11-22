using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// What a host can hook on when the assistant refuses a team, and the gate that lets it refuse for reasons of its own: told once per spell, never failing the
/// refusal, and holding nothing back when it breaks.
/// </summary>
public class SupportAssistantRefusalEventTests
{
    private const string Team = "team-a";
    private const string Alice = "alice-subject";

    private static readonly DateTime EndOfPeriod = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly ISupportResponder _responder = Substitute.For<ISupportResponder>();
    private readonly ISupportAssistantAllowanceSource _allowance = Substitute.For<ISupportAssistantAllowanceSource>();
    private readonly ISupportAssistantGate _gate = Substitute.For<ISupportAssistantGate>();
    private readonly CollectingSink _sink = new();

    private SupportCaseService Service(bool gate = false, bool allowance = true, params ISupportAssistantRefusalSink[] more)
    {
        _responder.GetReadinessAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SupportAssistantReadiness.Ready);
        _responder.AnswerAsync(Arg.Any<SupportCase>(), Arg.Any<IReadOnlyList<SupportMessage>>(), Arg.Any<CancellationToken>()).Returns(SupportAnswer.FromBody("Here you go."));

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Alice), new Claim(ClaimTypes.Name, "Alice"), new Claim(TeamClaimTypes.TeamKey, Team)], "test"));

        return new SupportCaseService(new InMemorySupportCaseStore(), new TeamAuthorizer(new FixedPrincipalAccessor(principal)), TimeProvider.System, responder: _responder,
            logger: NullLogger<SupportCaseService>.Instance, allowance: allowance ? _allowance : null, gate: gate ? _gate : null,
            refusalSinks: [.. more, _sink], announcements: new SupportRefusalAnnouncements());
    }

    private void AllowanceIs(bool exhausted)
        => _allowance.GetAsync(Team, Arg.Any<CancellationToken>()).Returns(new SupportAssistantAllowance { TeamKey = Team, Applies = true, Exhausted = exhausted, PeriodEndsAt = EndOfPeriod, Limit = 100, Used = exhausted ? 100 : 10 });

    // Raised while the assistant is ready, so it is the later question that is refused.
    private static async Task<string> OpenAsync(SupportCaseService service)
        => (await service.RaiseCaseAsync(Team, null, "The export is empty.", SupportAssistance.Assistant)).Id;

    [Fact]
    public async Task AQuestionRefusedForAUsedUpAllowance_IsToldToTheSink_WithTheAllowance()
    {
        AllowanceIs(exhausted: false);
        var service = Service();
        var caseId = await OpenAsync(service);
        AllowanceIs(exhausted: true);

        Assert.False(await service.RunAssistantAsync(Team, caseId));

        var refusal = Assert.Single(_sink.Refusals);
        Assert.Equal(Team, refusal.TeamKey);
        Assert.Equal(caseId, refusal.CaseId);
        Assert.Equal(SupportAssistantReadiness.AllowanceUsed, refusal.Reason);
        Assert.Equal(EndOfPeriod, refusal.Allowance.PeriodEndsAt);
    }

    /// <summary>A team that keeps asking is one piece of news, not one per question.</summary>
    [Fact]
    public async Task ATeamThatKeepsAsking_IsToldOnce()
    {
        AllowanceIs(exhausted: false);
        var service = Service();
        var caseId = await OpenAsync(service);
        AllowanceIs(exhausted: true);

        await service.RunAssistantAsync(Team, caseId);
        await service.RunAssistantAsync(Team, caseId);
        await service.RunAssistantAsync(Team, caseId);

        Assert.Single(_sink.Refusals);
    }

    [Fact]
    public async Task OnceTheAssistantHasAnsweredAgain_TheNextRefusalIsNewsAgain()
    {
        AllowanceIs(exhausted: false);
        var service = Service();
        var caseId = await OpenAsync(service);

        AllowanceIs(exhausted: true);
        await service.RunAssistantAsync(Team, caseId);
        AllowanceIs(exhausted: false);
        Assert.True(await service.RunAssistantAsync(Team, caseId));
        AllowanceIs(exhausted: true);
        await service.RunAssistantAsync(Team, caseId);

        Assert.Equal(2, _sink.Refusals.Count);
    }

    [Fact]
    public async Task AProviderRefusal_IsToldToo()
    {
        AllowanceIs(exhausted: false);
        var service = Service();
        var caseId = await OpenAsync(service);
        _responder.AnswerAsync(Arg.Any<SupportCase>(), Arg.Any<IReadOnlyList<SupportMessage>>(), Arg.Any<CancellationToken>()).Returns(SupportAnswer.Declined(ChatSupportResponder.ProviderRefusedReason));
        _responder.GetReadinessAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SupportAssistantReadiness.ProviderRefused);

        Assert.False(await service.RunAssistantAsync(Team, caseId));

        Assert.Equal(SupportAssistantReadiness.ProviderRefused, Assert.Single(_sink.Refusals).Reason);
    }

    /// <summary>A decline for another reason, such as the model taking too long, is not an account refusal and is nobody's news.</summary>
    [Fact]
    public async Task AnOrdinaryDecline_IsNotAnnounced()
    {
        AllowanceIs(exhausted: false);
        var service = Service();
        var caseId = await OpenAsync(service);
        _responder.AnswerAsync(Arg.Any<SupportCase>(), Arg.Any<IReadOnlyList<SupportMessage>>(), Arg.Any<CancellationToken>()).Returns(SupportAnswer.Declined("The assistant took too long to answer."));

        Assert.False(await service.RunAssistantAsync(Team, caseId));

        Assert.Empty(_sink.Refusals);
    }

    [Fact]
    public async Task ASinkThatThrows_NeverChangesTheRefusal_AndTheOthersAreStillTold()
    {
        AllowanceIs(exhausted: false);
        var service = Service(more: new ThrowingSink());
        var caseId = await OpenAsync(service);
        AllowanceIs(exhausted: true);

        Assert.False(await service.RunAssistantAsync(Team, caseId));

        Assert.Single(_sink.Refusals);
    }

    // ---- the host's gate ----

    [Fact]
    public async Task AGateThatHoldsTheAssistant_MakesTeamsReadinessHeld()
    {
        _gate.CheckAsync(Team, Arg.Any<CancellationToken>()).Returns(new SupportAssistantHold { Message = "Your plan does not include the assistant." });
        var service = Service(gate: true);

        Assert.Equal(SupportAssistantReadiness.Held, await service.GetAssistantReadinessAsync(Team));
    }

    [Fact]
    public async Task AGateThatHoldsNothing_LeavesTheAssistantReady()
    {
        AllowanceIs(exhausted: false);
        _gate.CheckAsync(Team, Arg.Any<CancellationToken>()).Returns((SupportAssistantHold)null);
        var service = Service(gate: true);

        Assert.Equal(SupportAssistantReadiness.Ready, await service.GetAssistantReadinessAsync(Team));
    }

    /// <summary>The host's rule comes before the allowance: it is the first thing a team is told.</summary>
    [Fact]
    public async Task AHold_IsReportedBeforeAUsedUpAllowance()
    {
        AllowanceIs(exhausted: true);
        _gate.CheckAsync(Team, Arg.Any<CancellationToken>()).Returns(new SupportAssistantHold { Message = "No." });
        var service = Service(gate: true);

        Assert.Equal(SupportAssistantReadiness.Held, await service.GetAssistantReadinessAsync(Team));
    }

    [Fact]
    public async Task AQuestionToAHeldAssistant_NeverReachesTheModel_AndTheSinkGetsTheHostsMessage()
    {
        AllowanceIs(exhausted: false);
        _gate.CheckAsync(Team, Arg.Any<CancellationToken>()).Returns((SupportAssistantHold)null);
        var service = Service(gate: true);
        var caseId = await OpenAsync(service);
        _gate.CheckAsync(Team, Arg.Any<CancellationToken>()).Returns(new SupportAssistantHold { Message = "Your plan does not include the assistant." });

        Assert.False(await service.RunAssistantAsync(Team, caseId));

        await _responder.DidNotReceiveWithAnyArgs().AnswerAsync(default, default, default(CancellationToken));
        var refusal = Assert.Single(_sink.Refusals);
        Assert.Equal(SupportAssistantReadiness.Held, refusal.Reason);
        Assert.Equal("Your plan does not include the assistant.", refusal.Message);
    }

    /// <summary>The host's rule is not worth everybody's support: a gate that breaks holds nothing back.</summary>
    [Fact]
    public async Task AGateThatThrows_HoldsNothingBack()
    {
        AllowanceIs(exhausted: false);
        _gate.CheckAsync(Team, Arg.Any<CancellationToken>()).Returns<SupportAssistantHold>(_ => throw new InvalidOperationException("The plans service is down."));
        var service = Service(gate: true);

        Assert.Equal(SupportAssistantReadiness.Ready, await service.GetAssistantReadinessAsync(Team));
    }

    [Fact]
    public void HeldIsNotReady_AndTheZeroValueIsStillNotRegistered()
    {
        Assert.NotEqual(SupportAssistantReadiness.Ready, SupportAssistantReadiness.Held);
        Assert.Equal(SupportAssistantReadiness.NotRegistered, default);
    }

    [Fact]
    public void TheAnnouncementMemory_TellsEachSpellOnce_PerTeam()
    {
        var memory = new SupportRefusalAnnouncements();

        Assert.True(memory.IsNew("a", "x"));
        Assert.False(memory.IsNew("a", "x"));
        Assert.True(memory.IsNew("b", "x"));
        Assert.True(memory.IsNew("a", "y"));
        memory.Clear("a");
        Assert.True(memory.IsNew("a", "y"));
    }

    private sealed class CollectingSink : ISupportAssistantRefusalSink
    {
        public List<SupportAssistantRefusal> Refusals { get; } = [];

        public Task HandleAsync(SupportAssistantRefusal refusal, CancellationToken cancellationToken = default)
        {
            Refusals.Add(refusal);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSink : ISupportAssistantRefusalSink
    {
        public Task HandleAsync(SupportAssistantRefusal refusal, CancellationToken cancellationToken = default) => throw new InvalidOperationException("The message service is down.");
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
