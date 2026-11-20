using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The person the assistant answered rates the conversation as a whole: kept on the case, audited, announced, and handed to
/// the host's sinks with the conversation but never what its tools were asked or returned.
/// </summary>
public class SupportConversationRatingTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Question = "How long are grades kept?";
    private const string Comment = "It never looked at our retention rules.";
    private const string SecretArgument = "personnummer 19800101-1234";

    [Fact]
    public async Task TheAuthor_RatesTheConversation_AndTheCaseKeepsIt()
    {
        var world = new World();
        var raised = await world.Answered();
        var alice = world.As(Alice);

        await alice.RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Down, Comment);

        var rating = (await alice.GetCaseAsync(TeamA, raised.Id)).ConversationRating;
        Assert.Equal(SupportAnswerRating.Down, rating.Rating);
        Assert.Equal(Comment, rating.Comment);
        Assert.Equal(Alice, rating.RatedBy);
        Assert.Equal(World.Now, rating.RatedAt);
    }

    [Fact]
    public async Task RatingAgain_ReplacesTheEarlierRating()
    {
        var world = new World();
        var raised = await world.Answered();
        var alice = world.As(Alice);

        await alice.RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Down, Comment);
        await alice.RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Up);

        var rating = (await alice.GetCaseAsync(TeamA, raised.Id)).ConversationRating;
        Assert.Equal(SupportAnswerRating.Up, rating.Rating);
        Assert.Null(rating.Comment);
    }

    [Fact]
    public async Task None_TakesTheRatingBack()
    {
        var world = new World();
        var raised = await world.Answered();
        var alice = world.As(Alice);
        await alice.RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Down, Comment);

        await alice.RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.None);

        Assert.Null((await alice.GetCaseAsync(TeamA, raised.Id)).ConversationRating);
    }

    [Theory]
    [InlineData("   ", null)]
    [InlineData("  Too slow.  ", "Too slow.")]
    public async Task AComment_IsTrimmed_AndABlankOneIsNoComment(string given, string kept)
    {
        var world = new World();
        var raised = await world.Answered();

        await world.As(Alice).RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Down, given);

        Assert.Equal(kept, (await world.As(Alice).GetCaseAsync(TeamA, raised.Id)).ConversationRating.Comment);
    }

    [Fact]
    public async Task ACommentLongerThanAMessage_IsRefused()
    {
        var world = new World();
        var raised = await world.Answered();

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.As(Alice).RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Down, new string('x', SupportCaseLimits.MaxMessageLength + 1)));
    }

    [Fact]
    public async Task ARatingThatIsNoVerdict_IsRefused()
    {
        var world = new World();
        var raised = await world.Answered();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => world.As(Alice).RateConversationAsync(TeamA, raised.Id, (SupportAnswerRating)7));
    }

    [Fact]
    public async Task SomebodyElse_EvenSupport_CannotRateTheConversation()
    {
        var world = new World();
        var raised = await world.Answered();
        var staff = world.As("staff-subject", team: null, systemScopes: [SystemSupportScopes.AllManage]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => staff.RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Down));
    }

    [Fact]
    public async Task AConversationTheAssistantNeverAnswered_CannotBeRated()
    {
        var world = new World();
        var raised = await world.As(Alice).RaiseCaseAsync(TeamA, null, Question);

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.As(Alice).RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Down));
    }

    [Fact]
    public async Task ARating_IsAudited_WithHowItWent_ButNeverTheComment()
    {
        var world = new World();
        var raised = await world.Answered();

        await world.As(Alice).RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Down, Comment);

        var entry = world.Audit.Entries.Single(x => x.Action == "rate-conversation");
        Assert.Equal("Down", entry.Metadata[SupportAuditMetadataKeys.Rating]);
        Assert.Equal(raised.Id, entry.Metadata[SupportAuditMetadataKeys.CaseId]);
        Assert.DoesNotContain(entry.Metadata.Values, x => x?.Contains("retention") == true);
    }

    [Fact]
    public async Task ARefusedRating_IsAuditedAsFailed()
    {
        var world = new World();
        var raised = await world.Answered();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => world.As("bob-subject").RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Down));

        Assert.False(world.Audit.Entries.Single(x => x.Action == "rate-conversation").Success);
    }

    [Fact]
    public async Task ARating_IsAnnounced_AsAChangeToTheCase()
    {
        var world = new World();
        var raised = await world.Answered();
        SupportCaseUpdatedEventArgs seen = null;
        world.Notifier.CaseUpdated += (_, e) => seen = e;

        await world.As(Alice).RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Up);

        Assert.Equal(SupportCaseChange.ConversationRated, seen.Change);
        Assert.Equal(raised.Id, seen.CaseId);
    }

    [Fact]
    public async Task ThumbsDown_ReachesEverySink_WithTheConversation()
    {
        var first = new CollectingSink();
        var second = new CollectingSink();
        var world = new World([first, second]);
        var raised = await world.Answered();

        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("sv-SE");
        try
        {
            await world.As(Alice).RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Down, Comment);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        var feedback = Assert.Single(first.Received);
        Assert.Single(second.Received);
        Assert.Equal(raised.Id, feedback.CaseId);
        Assert.Equal(TeamA, feedback.TeamKey);
        Assert.Equal(SupportAnswerRating.Down, feedback.Rating);
        Assert.Equal(Comment, feedback.Comment);
        Assert.Equal(World.Now, feedback.RatedAt);
        Assert.Equal("sv-SE", feedback.Culture);
        Assert.Equal([1, 2], feedback.Messages.Select(x => x.Sequence));
        Assert.Equal(Question, feedback.Messages[0].Body);
        Assert.Equal(SupportMessageKind.Assistant, feedback.Messages[1].Kind);
        Assert.Equal([new SupportConversationFeedbackCall { Name = "list_retention_rules", Succeeded = true }, new SupportConversationFeedbackCall { Name = "read_manual", Succeeded = false }], feedback.Messages[1].Calls);
    }

    [Fact]
    public async Task WhatTheToolsWereAsked_NeverReachesASink()
    {
        var sink = new CollectingSink();
        var world = new World([sink]);
        var raised = await world.Answered();

        await world.As(Alice).RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Down);

        Assert.DoesNotContain(SecretArgument, SupportConversationFeedbackJson.Serialize(Assert.Single(sink.Received)));
    }

    [Fact]
    public async Task ThumbsUp_ReachesNoSink_ByDefault()
    {
        var sink = new CollectingSink();
        var world = new World([sink]);
        var raised = await world.Answered();

        await world.As(Alice).RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Up);

        Assert.Empty(sink.Received);
    }

    [Fact]
    public async Task TheHost_CanLetThumbsUpThrough()
    {
        var sink = new CollectingSink();
        var world = new World([sink], [SupportAnswerRating.Up, SupportAnswerRating.Down]);
        var raised = await world.Answered();

        await world.As(Alice).RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Up);

        Assert.Equal(SupportAnswerRating.Up, Assert.Single(sink.Received).Rating);
    }

    [Fact]
    public async Task TakingTheRatingBack_ReachesNoSink()
    {
        var sink = new CollectingSink();
        var world = new World([sink], [SupportAnswerRating.Up, SupportAnswerRating.Down, SupportAnswerRating.None]);
        var raised = await world.Answered();

        await world.As(Alice).RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.None);

        Assert.Empty(sink.Received);
    }

    [Fact]
    public async Task ARefusedRating_ReachesNoSink()
    {
        var sink = new CollectingSink();
        var world = new World([sink]);
        var raised = await world.Answered();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => world.As("bob-subject").RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Down));

        Assert.Empty(sink.Received);
    }

    [Fact]
    public async Task ASinkThatThrows_NeverFailsTheRating_AndTheOthersAreStillTold()
    {
        var after = new CollectingSink();
        var world = new World([new ThrowingSink(), after]);
        var raised = await world.Answered();

        await world.As(Alice).RateConversationAsync(TeamA, raised.Id, SupportAnswerRating.Down, Comment);

        Assert.Single(after.Received);
        Assert.NotNull((await world.As(Alice).GetCaseAsync(TeamA, raised.Id)).ConversationRating);
    }

    private sealed class World(ISupportConversationFeedbackSink[] sinks = null, SupportAnswerRating[] feedbackRatings = null)
    {
        public static readonly DateTime Now = new(2026, 11, 20, 14, 30, 0, DateTimeKind.Utc);

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
            var service = new SupportCaseService(_store, authorizer, new FixedTime(Now), null, Notifier, new LookingUpResponder(), feedbackSinks: sinks, feedbackRatings: feedbackRatings);

            return new AuditingSupportCaseServiceDecorator(
                new AuthorizationSupportCaseServiceDecorator(service, authorizer),
                new CompositeAuditLogger([Audit], Options.Create(new AuditOptions())),
                new AuditEntryFactory(null));
        }
    }

    private sealed class LookingUpResponder : ISupportResponder
    {
        public Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, CancellationToken cancellationToken = default)
            => Task.FromResult(SupportAnswer.FromBody("Grades are kept for ten years.") with
            {
                Calls =
                [
                    new SupportAssistantCall { Name = "list_retention_rules", Arguments = $"text: \"{SecretArgument}\"", Succeeded = true },
                    new SupportAssistantCall { Name = "read_manual", Succeeded = false }
                ]
            });
    }

    private sealed class CollectingSink : ISupportConversationFeedbackSink
    {
        public List<SupportConversationFeedback> Received { get; } = [];

        public Task HandleAsync(SupportConversationFeedback feedback, CancellationToken cancellationToken = default)
        {
            Received.Add(feedback);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSink : ISupportConversationFeedbackSink
    {
        public Task HandleAsync(SupportConversationFeedback feedback, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The mail server is down.");
    }

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
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
