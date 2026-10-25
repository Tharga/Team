using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The person the assistant answered says whether an answer helped: kept on the answer, counted on the case, audited.
/// </summary>
public class SupportAnswerRatingTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Question = "The export is empty.";
    private const int Answer = 2;

    [Fact]
    public async Task TheAuthor_RatesAnAnswer_AndTheCaseCountsIt()
    {
        var world = new World();
        var raised = await world.Answered();
        var alice = world.As(Alice);

        await alice.RateAnswerAsync(TeamA, raised.Id, Answer, SupportAnswerRating.Up);
        Assert.Equal(SupportAnswerRating.Up, (await alice.GetMessagesAsync(TeamA, raised.Id)).Items.Single(x => x.Sequence == Answer).Rating);
        Assert.Equal((1, 0), Counts(await alice.GetCaseAsync(TeamA, raised.Id)));

        await alice.RateAnswerAsync(TeamA, raised.Id, Answer, SupportAnswerRating.Down);
        Assert.Equal((0, 1), Counts(await alice.GetCaseAsync(TeamA, raised.Id)));

        await alice.RateAnswerAsync(TeamA, raised.Id, Answer, SupportAnswerRating.None);
        Assert.Equal((0, 0), Counts(await alice.GetCaseAsync(TeamA, raised.Id)));
    }

    [Fact]
    public async Task SomebodyElse_EvenSupport_CannotRateTheAnswer()
    {
        var world = new World();
        var raised = await world.Answered();
        var staff = world.As("staff-subject", team: null, systemScopes: [SystemSupportScopes.AllManage]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staff.RateAnswerAsync(TeamA, raised.Id, Answer, SupportAnswerRating.Down));
    }

    [Fact]
    public async Task OnlyTheAssistantsAnswers_AreRated()
    {
        var world = new World();
        var raised = await world.Answered();

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.As(Alice).RateAnswerAsync(TeamA, raised.Id, 1, SupportAnswerRating.Up));
    }

    [Fact]
    public async Task ARating_IsAudited_WithHowItWent()
    {
        var world = new World();
        var raised = await world.Answered();

        await world.As(Alice).RateAnswerAsync(TeamA, raised.Id, Answer, SupportAnswerRating.Down);

        var entry = world.Audit.Entries.Single(x => x.Action == "rate-answer");
        Assert.Equal("Down", entry.Metadata[SupportAuditMetadataKeys.Rating]);
        Assert.Equal(raised.Id, entry.Metadata[SupportAuditMetadataKeys.CaseId]);
    }

    [Fact]
    public async Task ARating_IsAnnounced_AsAChangeToTheCase()
    {
        var world = new World();
        var raised = await world.Answered();
        SupportCaseUpdatedEventArgs seen = null;
        world.Notifier.CaseUpdated += (_, e) => seen = e;

        await world.As(Alice).RateAnswerAsync(TeamA, raised.Id, Answer, SupportAnswerRating.Up);

        Assert.Equal(SupportCaseChange.Rated, seen.Change);
    }

    private static (int Up, int Down) Counts(SupportCase supportCase) => (supportCase.AnswersRatedUp, supportCase.AnswersRatedDown);

    private sealed class World
    {
        private readonly InMemorySupportCaseStore _store = new();

        public SupportCaseNotifier Notifier { get; } = new();

        public CollectingAuditLogger Audit { get; } = new();

        public async Task<SupportCase> Answered()
        {
            var alice = As(Alice);
            var raised = await alice.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);
            await alice.RunAssistantAsync(TeamA, raised.Id);
            return raised;
        }

        public ISupportCaseService As(string subject, string team = TeamA, string[] systemScopes = null)
        {
            List<Claim> claims = [new(ClaimTypes.NameIdentifier, subject), new(ClaimTypes.Name, subject)];
            if (team != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, team));
            claims.AddRange((systemScopes ?? []).Select(x => new Claim(TeamClaimTypes.SystemScope, x)));
            var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

            return new AuditingSupportCaseServiceDecorator(
                new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(_store, authorizer, TimeProvider.System, null, Notifier, new AnsweringResponder()), authorizer),
                new CompositeAuditLogger([Audit], Options.Create(new AuditOptions())),
                new AuditEntryFactory(null));
        }
    }

    private sealed class AnsweringResponder : ISupportResponder
    {
        public Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, CancellationToken cancellationToken = default)
            => Task.FromResult(SupportAnswer.FromBody("Reindex the export."));
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
