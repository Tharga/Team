using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// What the assistant was asked and had no way to do, kept on the case for the people who build it.
/// </summary>
public class SupportAssistantGapTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Question = "Is there a case about telling a joke?";
    private const string Gap = "search cases by subject";

    [Fact]
    public async Task TheAssistantIsAlwaysGiven_AWayToSayWhatItCannotDo()
    {
        var chat = new RecordingChatClient();

        await Responder(chat).AnswerAsync(Case(), Transcript());

        Assert.Contains(chat.ToolsOffered, x => x == ChatSupportResponder.MissingSkillTool);
    }

    /// <summary>Through the real tool loop: the model reports the gap, is told to say so, and then answers.</summary>
    [Fact]
    public async Task WhatTheModelReports_ComesBackWithTheAnswer()
    {
        var chat = new ReportingChatClient(Gap, "I cannot search your cases by subject, but a person can.").AsBuilder().UseFunctionInvocation().Build();

        var answer = await Responder(chat).AnswerAsync(Case(), Transcript(), new Reports());

        Assert.Equal([Gap], answer.Gaps);
        Assert.Equal("I cannot search your cases by subject, but a person can.", answer.Body);
    }

    [Fact]
    public async Task AGap_IsKeptOnTheCase_WithTheAnswer()
    {
        var store = new InMemorySupportCaseStore();
        var service = Service(store, new FixedResponder(SupportAnswer.FromBody("A person can help.") with { Gaps = [Gap] }));
        var raised = await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        Assert.True(await service.RunAssistantAsync(TeamA, raised.Id));

        Assert.Equal([Gap], (await service.GetCaseAsync(TeamA, raised.Id)).AssistantGaps);
    }

    /// <summary>A decline because the assistant could not do what was asked is the most telling gap of all.</summary>
    [Fact]
    public async Task AGap_IsKept_EvenWhenTheAssistantDeclined()
    {
        var store = new InMemorySupportCaseStore();
        var service = Service(store, new FixedResponder(SupportAnswer.Declined("Nothing to say.") with { Gaps = [Gap] }));
        var raised = await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        Assert.False(await service.RunAssistantAsync(TeamA, raised.Id));

        Assert.Equal([Gap], (await service.GetCaseAsync(TeamA, raised.Id)).AssistantGaps);
    }

    /// <summary>The model writes these, so they are bounded like anything else it writes.</summary>
    [Fact]
    public async Task Gaps_AreTrimmed_Shortened_AndKeptOnce()
    {
        var store = new InMemorySupportCaseStore();
        var tooLong = new string('x', SupportCaseLimits.MaxAssistantGapLength + 50);
        var service = Service(store, new FixedResponder(SupportAnswer.FromBody("No.") with { Gaps = ["  " + Gap + "  ", Gap.ToUpperInvariant(), " ", tooLong] }));
        var raised = await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        await service.RunAssistantAsync(TeamA, raised.Id);

        var kept = (await service.GetCaseAsync(TeamA, raised.Id)).AssistantGaps;
        Assert.Equal(2, kept.Length);
        Assert.Equal(Gap, kept[0]);
        Assert.Equal(SupportCaseLimits.MaxAssistantGapLength, kept[1].Length);
    }

    [Fact]
    public async Task ACaseTheAssistantCouldHelpWith_HasNoGaps()
    {
        var store = new InMemorySupportCaseStore();
        var service = Service(store, new FixedResponder(SupportAnswer.FromBody("Reindex the export.")));
        var raised = await service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        await service.RunAssistantAsync(TeamA, raised.Id);

        Assert.Empty((await service.GetCaseAsync(TeamA, raised.Id)).AssistantGaps);
    }

    private static ChatSupportResponder Responder(IChatClient chat)
        => new(new RegisteredChatClientResolver(chat), new NoTools(), new SupportAssistantOptions(), NullLogger<ChatSupportResponder>.Instance);

    private static ISupportCaseService Service(InMemorySupportCaseStore store, ISupportResponder responder)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Alice),
            new Claim(ClaimTypes.Name, Alice),
            new Claim(TeamClaimTypes.TeamKey, TeamA)
        ], "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        return new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(store, authorizer, TimeProvider.System, null, null, responder), authorizer);
    }

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

    private sealed class Reports : IProgress<SupportAssistantProgress>
    {
        public void Report(SupportAssistantProgress value)
        {
        }
    }

    private sealed class FixedResponder(SupportAnswer answer) : ISupportResponder
    {
        public Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, CancellationToken cancellationToken = default)
            => Task.FromResult(answer);
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public string[] ToolsOffered { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        {
            ToolsOffered = [.. (options?.Tools ?? []).Select(x => x.Name)];
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello.")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>Reports the gap until told it is noted, then answers.</summary>
    private sealed class ReportingChatClient(string gap, string answer) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            if (!messages.SelectMany(x => x.Contents).OfType<FunctionResultContent>().Any())
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-1", ChatSupportResponder.MissingSkillTool, new Dictionary<string, object> { ["what"] = gap })]);
                yield break;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, answer);
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

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
