using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The assistant answers each team with the model the host gives that team, and with none it says so rather
/// than failing.
/// </summary>
public class ChatSupportResponderTests
{
    private const string TeamA = "team-a";
    private const string TeamB = "team-b";
    private const string Question = "The export is empty.";

    [Fact]
    public async Task EachTeam_IsAnsweredByItsOwnModel()
    {
        var responder = Responder(new MapResolver(new() { [TeamA] = new FixedChatClient("From A."), [TeamB] = new FixedChatClient("From B.") }));

        Assert.Equal("From A.", (await responder.AnswerAsync(Case(TeamA), Transcript())).Body);
        Assert.Equal("From B.", (await responder.AnswerAsync(Case(TeamB), Transcript())).Body);
    }

    [Fact]
    public async Task ATeamWithAModel_IsReady()
    {
        var responder = Responder(new MapResolver(new() { [TeamA] = new FixedChatClient("From A.") }));

        Assert.Equal(SupportAssistantReadiness.Ready, await responder.GetReadinessAsync(TeamA));
    }

    [Fact]
    public async Task ATeamWithNoModel_IsNotConfigured_AndItsCaseIsDeclinedForAPerson()
    {
        var responder = Responder(new MapResolver(new() { [TeamA] = new FixedChatClient("From A.") }));

        Assert.Equal(SupportAssistantReadiness.NotConfigured, await responder.GetReadinessAsync(TeamB));

        var answer = await responder.AnswerAsync(Case(TeamB), Transcript());
        Assert.False(answer.Answered);
        Assert.Equal(ChatSupportResponder.NotConfiguredReason, answer.Reason);
    }

    /// <summary>Settings that cannot be read must not stop somebody raising a case, so readiness says not configured.</summary>
    [Fact]
    public async Task AFailingLookup_IsNotConfigured_RatherThanThrown()
    {
        var responder = Responder(new FailingResolver());

        Assert.Equal(SupportAssistantReadiness.NotConfigured, await responder.GetReadinessAsync(TeamA));
        Assert.False((await responder.AnswerAsync(Case(TeamA), Transcript())).Answered);
    }

    [Fact]
    public async Task TheDefault_HandsEveryTeamTheRegisteredModel()
    {
        var registered = new FixedChatClient("Shared.");
        var resolver = new RegisteredChatClientResolver(registered);

        Assert.Same(registered, await resolver.GetAsync(TeamA));
        Assert.Same(registered, await resolver.GetAsync(null));
    }

    [Fact]
    public async Task TheDefault_WithNothingRegistered_HasNoModelForAnyTeam()
    {
        Assert.Null(await new RegisteredChatClientResolver(null).GetAsync(TeamA));
    }

    private static ChatSupportResponder Responder(ISupportChatClientResolver resolver)
        => new(resolver, new NoTools(), new SupportAssistantOptions(), NullLogger<ChatSupportResponder>.Instance);

    private static readonly DateTime At = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static SupportCase Case(string teamKey) => new()
    {
        Id = $"case-{teamKey}",
        TeamKey = teamKey,
        AuthorIdentity = "alice-subject",
        AuthorName = "Alice",
        Subject = Question,
        Status = SupportCaseStatus.Open,
        CreatedAt = At,
        MessageCount = 1
    };

    private static IReadOnlyList<SupportMessage> Transcript() => [new() { Sequence = 1, Kind = SupportMessageKind.User, Body = Question, SentAt = At }];

    private sealed class MapResolver(Dictionary<string, IChatClient> clients) : ISupportChatClientResolver
    {
        public Task<IChatClient> GetAsync(string teamKey, CancellationToken cancellationToken = default)
            => Task.FromResult(teamKey != null && clients.TryGetValue(teamKey, out var chat) ? chat : null);
    }

    private sealed class FailingResolver : ISupportChatClientResolver
    {
        public Task<IChatClient> GetAsync(string teamKey, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The settings store is down.");
    }

    private sealed class NoTools : ISupportAssistantTools
    {
        public Task<IReadOnlyList<AITool>> ForCurrentCallerAsync(string teamKey, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AITool>>([]);
    }

    private sealed class FixedChatClient(string answer) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
