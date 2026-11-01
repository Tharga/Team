using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Whether the assistant asks the model to reason: only when the host says so, because a model that does not reason
/// can refuse the request.
/// </summary>
public class SupportAssistantReasoningTests
{
    [Fact]
    public async Task ByDefault_TheModelIsNotAskedToReason()
    {
        var chat = new CapturingChatClient();

        await Answer(chat, new SupportAssistantOptions(), streamed: true);
        await Answer(chat, new SupportAssistantOptions(), streamed: false);

        Assert.All(chat.Options, x => Assert.Null(x.Reasoning));
        Assert.Equal(2, chat.Options.Count);
    }

    [Theory]
    [InlineData(ReasoningEffort.Low, true)]
    [InlineData(ReasoningEffort.Medium, true)]
    [InlineData(ReasoningEffort.High, false)]
    public async Task WhenTheHostAsksForIt_TheModelIsAskedToReason_AndToSendItsReasoningBack(ReasoningEffort effort, bool streamed)
    {
        var chat = new CapturingChatClient();

        await Answer(chat, new SupportAssistantOptions { Reasoning = effort }, streamed);

        var reasoning = Assert.Single(chat.Options).Reasoning;
        Assert.NotNull(reasoning);
        Assert.Equal(effort, reasoning.Effort);
        Assert.Equal(ReasoningOutput.Full, reasoning.Output);
    }

    private static Task Answer(IChatClient chat, SupportAssistantOptions options, bool streamed)
    {
        var responder = new ChatSupportResponder(new RegisteredChatClientResolver(chat), new NoTools(), options, NullLogger<ChatSupportResponder>.Instance);
        var supportCase = new SupportCase
        {
            Id = "case-1",
            TeamKey = "team-a",
            AuthorIdentity = "alice",
            AuthorName = "Alice",
            Subject = "Why?",
            Status = SupportCaseStatus.Open,
            CreatedAt = DateTime.UtcNow,
            MessageCount = 1
        };
        IReadOnlyList<SupportMessage> transcript = [new() { Sequence = 1, Kind = SupportMessageKind.User, Body = "Why?", SentAt = DateTime.UtcNow }];

        return streamed
            ? responder.AnswerAsync(supportCase, transcript, new Progress<SupportAssistantProgress>(_ => { }))
            : responder.AnswerAsync(supportCase, transcript);
    }

    private sealed class NoTools : ISupportAssistantTools
    {
        public Task<IReadOnlyList<AITool>> ForCurrentCallerAsync(string teamKey, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AITool>>([]);
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public List<ChatOptions> Options { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Because.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Because.");
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
