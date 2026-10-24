namespace Tharga.Team.Support.Tests;

/// <summary>
/// Whether an assistant can answer for a team, and why not when it cannot.
/// </summary>
public class SupportAssistantReadinessTests
{
    private const string TeamA = "team-a";

    /// <summary>A responder written before readiness existed answers for every team, as it always did.</summary>
    [Fact]
    public async Task AResponderThatDoesNotSay_IsReadyForEveryTeam()
    {
        ISupportResponder responder = new SilentResponder();

        Assert.Equal(SupportAssistantReadiness.Ready, await responder.GetReadinessAsync(TeamA));
        Assert.Equal(SupportAssistantReadiness.Ready, await responder.GetReadinessAsync(null));
    }

    /// <summary>A readiness nobody set must not read as ready, or a case waits on an assistant that only declines.</summary>
    [Fact]
    public void TheUnsetValue_IsNotReady()
    {
        Assert.Equal(SupportAssistantReadiness.NotRegistered, default(SupportAssistantReadiness));
    }

    private sealed class SilentResponder : ISupportResponder
    {
        public Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, CancellationToken cancellationToken = default)
            => Task.FromResult(SupportAnswer.Declined("Nothing to say."));
    }
}
