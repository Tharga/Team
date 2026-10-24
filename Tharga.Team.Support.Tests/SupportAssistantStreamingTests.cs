using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Watching the assistant's answer being written: what the caller is shown on the way, and what is kept.
/// </summary>
/// <remarks>
/// <b>A report is a preview; the answer returned is what is kept.</b> An answer that breaks off halfway is declined,
/// and nothing of what was shown on the way reaches the case.
/// </remarks>
public class SupportAssistantStreamingTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Bob = "bob-subject";
    private const string Question = "The export is empty.";

    [Fact]
    public async Task WithSomebodyWatching_TheAnswerIsReportedAsItIsWritten_AndKeptWhole()
    {
        var chat = new StreamingChatClient("Check ", "the date ", "range.");
        var seen = new Reports();

        var answer = await Responder(chat).AnswerAsync(Case(), Transcript(), seen);

        Assert.Equal(["Check ", "Check the date ", "Check the date range."], seen.Items);
        Assert.Equal("Check the date range.", answer.Body);
        Assert.False(chat.AskedInOnePiece);
    }

    [Fact]
    public async Task WithNobodyWatching_TheModelIsAskedInOnePiece()
    {
        var chat = new StreamingChatClient("Check ", "the date.");

        var answer = await Responder(chat).AnswerAsync(Case(), Transcript());

        Assert.True(chat.AskedInOnePiece);
        Assert.Equal("Check the date.", answer.Body);
    }

    /// <summary>The reports are for whoever is watching; one that fails to show them must not cost the answer.</summary>
    [Fact]
    public async Task AFailingReader_DoesNotLoseTheAnswer()
    {
        var answer = await Responder(new StreamingChatClient("Check ", "the date.")).AnswerAsync(Case(), Transcript(), new FailingReports());

        Assert.Equal("Check the date.", answer.Body);
    }

    [Fact]
    public async Task AStreamThatSaysNothing_IsDeclined()
    {
        var answer = await Responder(new StreamingChatClient("  ")).AnswerAsync(Case(), Transcript(), new Reports());

        Assert.False(answer.Answered);
    }

    [Fact]
    public async Task RunningTheAssistant_WithSomebodyWatching_ReportsTheAnswer_AndAppendsIt()
    {
        var world = new World(Responder(new StreamingChatClient("Check ", "the date.")));
        var raised = await world.As(Alice).RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);
        var seen = new Reports();

        Assert.True(await world.As(Alice).RunAssistantAsync(TeamA, raised.Id, seen));

        Assert.Equal("Check the date.", seen.Items[^1]);
        var last = (await world.As(Alice).GetMessagesAsync(TeamA, raised.Id)).Items[^1];
        Assert.Equal(SupportMessageKind.Assistant, last.Kind);
        Assert.Equal("Check the date.", last.Body);
    }

    [Fact]
    public async Task AnAnswerThatBreaksOff_IsDeclined_AndNothingShownIsKept()
    {
        var world = new World(Responder(new StreamingChatClient("Check ", "the ") { BreaksOff = true }));
        var raised = await world.As(Alice).RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);
        var seen = new Reports();

        Assert.False(await world.As(Alice).RunAssistantAsync(TeamA, raised.Id, seen));

        Assert.NotEmpty(seen.Items);
        Assert.Single((await world.As(Alice).GetMessagesAsync(TeamA, raised.Id)).Items);
    }

    /// <summary>A responder written before streaming answers in one piece, and the caller is shown nothing on the way.</summary>
    [Fact]
    public async Task AResponderThatCannotStream_AnswersInOnePiece()
    {
        var world = new World(new OnePieceResponder("Reindex the export."));
        var raised = await world.As(Alice).RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);
        var seen = new Reports();

        Assert.True(await world.As(Alice).RunAssistantAsync(TeamA, raised.Id, seen));

        Assert.Empty(seen.Items);
        Assert.Equal("Reindex the export.", (await world.As(Alice).GetMessagesAsync(TeamA, raised.Id)).Items[^1].Body);
    }

    /// <summary>A test double does not run the streaming default; a host's mocked assistant must still answer when watched.</summary>
    [Fact]
    public async Task AResponderDouble_StillAnswers_WhenWatched()
    {
        var responder = Substitute.For<ISupportResponder>();
        responder.AnswerAsync(Arg.Any<SupportCase>(), Arg.Any<IReadOnlyList<SupportMessage>>(), Arg.Any<CancellationToken>())
            .Returns(SupportAnswer.FromBody("Reindex the export."));
        var world = new World(responder);
        var raised = await world.As(Alice).RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        Assert.True(await world.As(Alice).RunAssistantAsync(TeamA, raised.Id, new Reports()));
    }

    [Fact]
    public async Task SomebodyWhoCannotWriteInTheCase_CannotWatchTheAssistantAnswerIt()
    {
        var world = new World(Responder(new StreamingChatClient("Check ", "the date.")));
        var raised = await world.As(Alice).RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);
        var seen = new Reports();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => world.As(Bob).RunAssistantAsync(TeamA, raised.Id, seen));

        Assert.Empty(seen.Items);
    }

    [Fact]
    public async Task AWatchedAnswer_IsAuditedAsAnyAnswer()
    {
        var world = new World(Responder(new StreamingChatClient("Check ", "the date.")));
        var raised = await world.As(Alice).RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        await world.As(Alice).RunAssistantAsync(TeamA, raised.Id, new Reports());

        Assert.Equal(["raise", "assistant-answer"], world.Audit.Entries.Select(x => x.Action));
    }

    private static ChatSupportResponder Responder(IChatClient chat)
        => new(new RegisteredChatClientResolver(chat), new NoTools(), new SupportAssistantOptions(), NullLogger<ChatSupportResponder>.Instance);

    private static readonly DateTime At = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static SupportCase Case() => new()
    {
        Id = "case-1",
        TeamKey = TeamA,
        AuthorIdentity = Alice,
        AuthorName = "Alice",
        Subject = Question,
        Status = SupportCaseStatus.Open,
        CreatedAt = At,
        MessageCount = 1
    };

    private static IReadOnlyList<SupportMessage> Transcript() => [new() { Sequence = 1, Kind = SupportMessageKind.User, Body = Question, SentAt = At }];

    private sealed class World(ISupportResponder responder)
    {
        private readonly InMemorySupportCaseStore _store = new();

        public CollectingAuditLogger Audit { get; } = new();

        public ISupportCaseService As(string subject)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, subject),
                new Claim(ClaimTypes.Name, subject),
                new Claim(TeamClaimTypes.TeamKey, TeamA)
            ], "test"));
            var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

            return new AuditingSupportCaseServiceDecorator(
                new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(_store, authorizer, TimeProvider.System, null, null, responder), authorizer),
                new CompositeAuditLogger([Audit], Options.Create(new AuditOptions())),
                new AuditEntryFactory(null));
        }
    }

    /// <summary>Kept as they come, on the reporting thread, rather than posted on as <see cref="Progress{T}"/> would.</summary>
    private sealed class Reports : IProgress<string>
    {
        public List<string> Items { get; } = [];

        public void Report(string value) => Items.Add(value);
    }

    private sealed class FailingReports : IProgress<string>
    {
        public void Report(string value) => throw new InvalidOperationException("The page went away.");
    }

    private sealed class OnePieceResponder(string answer) : ISupportResponder
    {
        public Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, CancellationToken cancellationToken = default)
            => Task.FromResult(SupportAnswer.FromBody(answer));
    }

    private sealed class StreamingChatClient(params string[] pieces) : IChatClient
    {
        public bool BreaksOff { get; init; }

        public bool AskedInOnePiece { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        {
            AskedInOnePiece = true;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(pieces))));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var piece in pieces)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, piece);
            }

            if (BreaksOff) throw new HttpRequestException("The connection was reset.");
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class NoTools : ISupportAssistantTools
    {
        public Task<IReadOnlyList<AITool>> ForCurrentCallerAsync(string teamKey, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AITool>>([]);
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
