namespace Tharga.Team.Service.Tests;

/// <summary>
/// Destroying a team's data when the team is purged.
/// </summary>
/// <remarks>
/// <b>The failure-direction test is the one that cannot be verified by reading the code.</b> Participants and
/// the team-record delete cannot be made atomic, so the order was chosen: abort with the team still present
/// rather than delete the team and leave data nothing can find. That is a claim about behaviour under
/// failure, and only a test that makes a participant throw actually checks it.
/// </remarks>
public class TeamPurgeCascadeTests
{
    private const string TeamKey = "acme";

    [Fact]
    public async Task EveryParticipantRuns()
    {
        var keys = new CountingParticipant("API keys", 3);
        var icons = new CountingParticipant("icons", 1);
        var cases = new CountingParticipant("support cases", 7);

        await new TeamPurgeCascade([keys, icons, cases]).RunAsync(TeamKey);

        Assert.Equal(TeamKey, keys.PurgedTeam);
        Assert.Equal(TeamKey, icons.PurgedTeam);
        Assert.Equal(TeamKey, cases.PurgedTeam);
    }

    [Fact]
    public async Task AFailingParticipant_AbortsThePurge_NamingWhatFailed()
    {
        var cascade = new TeamPurgeCascade([new ThrowingParticipant("API keys")]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => cascade.RunAsync(TeamKey));

        Assert.Contains("API keys", ex.Message);
        Assert.Contains("has not been deleted", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    /// <summary>
    /// A purge that failed part-way is retried, so a participant that already ran must report zero rather
    /// than throwing. This asserts the cascade tolerates that, which is what makes the retry advice honest.
    /// </summary>
    [Fact]
    public async Task ParticipantsThatRemoveNothing_AreFine()
    {
        var cascade = new TeamPurgeCascade([new CountingParticipant("API keys", 0), new CountingParticipant("icons", 0)]);

        var exception = await Record.ExceptionAsync(() => cascade.RunAsync(TeamKey));

        Assert.Null(exception);
    }

    [Fact]
    public async Task NoParticipants_IsNotAnError()
    {
        var exception = await Record.ExceptionAsync(() => new TeamPurgeCascade([]).RunAsync(TeamKey));

        Assert.Null(exception);
    }

    private sealed class CountingParticipant(string name, int removes) : ITeamPurgeParticipant
    {
        public string Name => name;

        public string PurgedTeam { get; private set; }

        public Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default)
        {
            PurgedTeam = teamKey;
            return Task.FromResult(removes);
        }
    }

    private sealed class ThrowingParticipant(string name) : ITeamPurgeParticipant
    {
        public string Name => name;

        public Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the store was unreachable");
    }
}
