using System.Net;
using System.Security.Claims;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The assistant not answering for a reason that is not a failure of the request: the team has used up its allowance, or the provider has refused the
/// account. Neither spends tokens, neither hands a case to the assistant, and each says which it is.
/// </summary>
public class SupportAssistantRefusalTests
{
    private const string Team = "team-a";
    private const string Other = "team-b";
    private const string Alice = "alice-subject";

    private static readonly DateTime At = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

    // ---- telling a refused account from a failed request ----

    [Theory]
    [InlineData(401, "Unauthorized", true)]
    [InlineData(402, "Payment Required", true)]
    [InlineData(403, "Forbidden", true)]
    [InlineData(400, "Your credit balance is too low to access the Anthropic API.", true)]
    [InlineData(400, "messages: field required", false)]
    [InlineData(429, "You exceeded your current quota, please check your plan and billing details.", true)]
    [InlineData(429, "Rate limit reached for requests, try again in 20s.", false)]
    [InlineData(500, "Internal error while checking billing.", false)]
    [InlineData(529, "Overloaded", false)]
    [InlineData(404, "model not found", false)]
    public void AStatusAndAMessage_SayWhetherTheAccountWasRefused(int status, string message, bool refused)
    {
        Assert.Equal(refused, ProviderRefusal.IsAccountLevel(new AnthropicStyleException(status, message)));
        Assert.Equal(refused, ProviderRefusal.IsAccountLevel(new OpenAiStyleException(status, message)));
    }

    [Fact]
    public void WithNoStatus_TheProvidersWordsDecide()
    {
        Assert.True(ProviderRefusal.IsAccountLevel(new InvalidOperationException("Monthly usage limit reached.")));
        Assert.False(ProviderRefusal.IsAccountLevel(new InvalidOperationException("The settings store is down.")));
        Assert.False(ProviderRefusal.IsAccountLevel(new TimeoutException("The request timed out.")));
    }

    [Fact]
    public void TheStatusOfAnInnerException_Counts()
        => Assert.True(ProviderRefusal.IsAccountLevel(new InvalidOperationException("The chat client failed.", new AnthropicStyleException(401, "invalid x-api-key"))));

    [Fact]
    public void ARealHttpRequestException_IsReadToo()
    {
        Assert.True(ProviderRefusal.IsAccountLevel(new HttpRequestException("Response status code does not indicate success: 403 (Forbidden).", null, HttpStatusCode.Forbidden)));
        Assert.False(ProviderRefusal.IsAccountLevel(new HttpRequestException("Connection refused.", null, null)));
    }

    // ---- remembering it ----

    [Fact]
    public void ARefusedTeam_IsRemembered_UntilTheRetryTimeHasPassed()
    {
        var clock = new FixedClock(At);
        var refusals = new SupportProviderRefusals(clock, new SupportAssistantOptions { ProviderRefusalRetryAfter = TimeSpan.FromMinutes(10) });

        refusals.Refused(Team);

        Assert.True(refusals.IsRefused(Team));
        Assert.False(refusals.IsRefused(Other));
        clock.Now = At.AddMinutes(9);
        Assert.True(refusals.IsRefused(Team));
        clock.Now = At.AddMinutes(10);
        Assert.False(refusals.IsRefused(Team));
    }

    [Fact]
    public void AnAnswer_MendsIt()
    {
        var refusals = new SupportProviderRefusals(new FixedClock(At), new SupportAssistantOptions());
        refusals.Refused(Team);

        refusals.Answered(Team);

        Assert.False(refusals.IsRefused(Team));
    }

    [Fact]
    public void NoTeam_IsNeverRemembered()
    {
        var refusals = new SupportProviderRefusals(new FixedClock(At), new SupportAssistantOptions());

        refusals.Refused(null);

        Assert.False(refusals.IsRefused(null));
    }

    // ---- the responder ----

    [Fact]
    public async Task AnAccountRefusal_DeclinesWithTheReason_AndTheTeamIsNotReady()
    {
        var chat = new ScriptedChat(new AnthropicStyleException(400, "Your credit balance is too low to access the Anthropic API."));
        var responder = Responder(chat, out _);

        var answer = await responder.AnswerAsync(Case(Team), Transcript());

        Assert.False(answer.Answered);
        Assert.Equal(ChatSupportResponder.ProviderRefusedReason, answer.Reason);
        Assert.Equal(SupportAssistantReadiness.ProviderRefused, await responder.GetReadinessAsync(Team));
    }

    /// <summary>The refused account is not asked again for every message; that is the point of remembering it.</summary>
    [Fact]
    public async Task AfterARefusal_TheProviderIsNotAskedAgain_UntilTheRetryTime()
    {
        var chat = new ScriptedChat(new AnthropicStyleException(403, "Forbidden"));
        var responder = Responder(chat, out var clock);

        await responder.AnswerAsync(Case(Team), Transcript());
        await responder.AnswerAsync(Case(Team), Transcript());
        await responder.AnswerAsync(Case(Team), Transcript());

        Assert.Equal(1, chat.Calls);
        clock.Now = At.AddMinutes(11);
        await responder.AnswerAsync(Case(Team), Transcript());
        Assert.Equal(2, chat.Calls);
    }

    /// <summary>When the account is mended the next answer clears the refusal, and the team is ready again.</summary>
    [Fact]
    public async Task AnAnswerAfterTheRetryTime_MendsIt()
    {
        var chat = new ScriptedChat(new AnthropicStyleException(402, "Payment Required"), null);
        var responder = Responder(chat, out var clock);

        await responder.AnswerAsync(Case(Team), Transcript());
        clock.Now = At.AddMinutes(11);
        var answer = await responder.AnswerAsync(Case(Team), Transcript());

        Assert.Equal("Here you go.", answer.Body);
        Assert.Equal(SupportAssistantReadiness.Ready, await responder.GetReadinessAsync(Team));
    }

    /// <summary>One team's refusal is that team's: another team, with its own key, is still asked.</summary>
    [Fact]
    public async Task ARefusalForOneTeam_IsNotAnotherTeams()
    {
        var chat = new ScriptedChat(new AnthropicStyleException(401, "invalid x-api-key"), null);
        var responder = Responder(chat, out _);

        await responder.AnswerAsync(Case(Team), Transcript());

        Assert.Equal(SupportAssistantReadiness.Ready, await responder.GetReadinessAsync(Other));
        Assert.Equal("Here you go.", (await responder.AnswerAsync(Case(Other), Transcript())).Body);
    }

    /// <summary>A provider that is merely down, or a model that is overloaded, is worth another try at once.</summary>
    [Theory]
    [InlineData(500, "Internal server error")]
    [InlineData(529, "Overloaded")]
    [InlineData(429, "Rate limit reached for requests, try again in 20s.")]
    public async Task AFailedRequest_IsNotRemembered(int status, string message)
    {
        var chat = new ScriptedChat(new AnthropicStyleException(status, message), null);
        var responder = Responder(chat, out _);

        var first = await responder.AnswerAsync(Case(Team), Transcript());
        var second = await responder.AnswerAsync(Case(Team), Transcript());

        Assert.False(first.Answered);
        Assert.NotEqual(ChatSupportResponder.ProviderRefusedReason, first.Reason);
        Assert.Equal("Here you go.", second.Body);
        Assert.Equal(SupportAssistantReadiness.Ready, await responder.GetReadinessAsync(Team));
    }

    [Fact]
    public async Task WithNothingToRememberWith_AnAccountRefusalStillDeclines()
    {
        var chat = new ScriptedChat(new AnthropicStyleException(401, "Unauthorized"));
        var responder = new ChatSupportResponder(new Resolver(chat), new NoTools(), new SupportAssistantOptions(), NullLogger<ChatSupportResponder>.Instance);

        Assert.Equal(ChatSupportResponder.ProviderRefusedReason, (await responder.AnswerAsync(Case(Team), Transcript())).Reason);
    }

    // ---- the case service ----

    [Fact]
    public async Task ATeamThatUsedUpItsAllowance_IsNotReady()
    {
        var world = new World(Allowance(applies: true, exhausted: true));

        Assert.Equal(SupportAssistantReadiness.AllowanceUsed, await world.Service.GetAssistantReadinessAsync(Team));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task ATeamWithAllowanceLeft_OrNoLimit_IsReady(bool applies, bool exhausted)
    {
        var world = new World(Allowance(applies, exhausted));

        Assert.Equal(SupportAssistantReadiness.Ready, await world.Service.GetAssistantReadinessAsync(Team));
    }

    /// <summary>A counter that is broken must not take the assistant away from every team at once.</summary>
    [Fact]
    public async Task AnAllowanceThatCannotBeRead_IsTreatedAsNotUsedUp()
    {
        var source = Substitute.For<ISupportAssistantAllowanceSource>();
        source.GetAsync(Team, Arg.Any<CancellationToken>()).Returns<SupportAssistantAllowance>(_ => throw new InvalidOperationException("The usage database is down."));
        var world = new World(source);

        Assert.Equal(SupportAssistantReadiness.Ready, await world.Service.GetAssistantReadinessAsync(Team));
    }

    [Fact]
    public async Task AProviderRefusalFromTheResponder_IsPassedOn_AndNotMistakenForReady()
    {
        var world = new World(Allowance(applies: true, exhausted: false), readiness: SupportAssistantReadiness.ProviderRefused);

        Assert.Equal(SupportAssistantReadiness.ProviderRefused, await world.Service.GetAssistantReadinessAsync(Team));
    }

    /// <summary>The allowance is checked only when the assistant would otherwise be ready: a team with no model is not told it is out of money.</summary>
    [Fact]
    public async Task ATeamNotConfigured_IsNotConfigured_WhateverItsAllowance()
    {
        var world = new World(Allowance(applies: true, exhausted: true), readiness: SupportAssistantReadiness.NotConfigured);

        Assert.Equal(SupportAssistantReadiness.NotConfigured, await world.Service.GetAssistantReadinessAsync(Team));
    }

    [Fact]
    public async Task ACaseRaisedWhileTheAllowanceIsUsedUp_IsNotHandedToTheAssistant()
    {
        var world = new World(Allowance(applies: true, exhausted: true));

        var raised = await world.Service.RaiseCaseAsync(Team, null, "The export is empty.", SupportAssistance.Assistant);

        Assert.Equal(SupportAssistantState.None, raised.AssistantState);
    }

    [Fact]
    public async Task ACaseRaisedWithAllowanceLeft_IsHandedToTheAssistant()
    {
        var world = new World(Allowance(applies: true, exhausted: false));

        var raised = await world.Service.RaiseCaseAsync(Team, null, "The export is empty.", SupportAssistance.Assistant);

        Assert.Equal(SupportAssistantState.Active, raised.AssistantState);
    }

    /// <summary>The allowance runs out while a conversation is open: the next message spends nothing and leaves no usage record of a question never sent.</summary>
    [Fact]
    public async Task AMessageAfterTheAllowanceRanOut_NeverReachesTheModel()
    {
        var source = Allowance(applies: true, exhausted: false);
        var world = new World(source);
        var raised = await world.Service.RaiseCaseAsync(Team, null, "The export is empty.", SupportAssistance.Assistant);
        source.GetAsync(Team, Arg.Any<CancellationToken>()).Returns(new SupportAssistantAllowance { TeamKey = Team, Applies = true, Exhausted = true });

        var answered = await world.Service.RunAssistantAsync(Team, raised.Id);

        Assert.False(answered);
        await world.Responder.DidNotReceiveWithAnyArgs().AnswerAsync(default, default, default(CancellationToken));
        Assert.Empty(world.Sink.Records);
    }

    [Fact]
    public async Task WithAllowanceLeft_TheAssistantAnswersAndItIsRecorded()
    {
        var world = new World(Allowance(applies: true, exhausted: false));
        var raised = await world.Service.RaiseCaseAsync(Team, null, "The export is empty.", SupportAssistance.Assistant);

        Assert.True(await world.Service.RunAssistantAsync(Team, raised.Id));
        Assert.Single(world.Sink.Records);
    }

    [Fact]
    public void TheZeroValue_IsStillNotRegistered_AndTheNewStatesAreNotReady()
    {
        Assert.Equal(SupportAssistantReadiness.NotRegistered, default);
        Assert.NotEqual(SupportAssistantReadiness.Ready, SupportAssistantReadiness.AllowanceUsed);
        Assert.NotEqual(SupportAssistantReadiness.Ready, SupportAssistantReadiness.ProviderRefused);
    }

    private static ISupportAssistantAllowanceSource Allowance(bool applies, bool exhausted)
    {
        var source = Substitute.For<ISupportAssistantAllowanceSource>();
        source.GetAsync(Team, Arg.Any<CancellationToken>()).Returns(new SupportAssistantAllowance { TeamKey = Team, Applies = applies, Exhausted = exhausted });
        return source;
    }

    private static ChatSupportResponder Responder(ScriptedChat chat, out FixedClock clock)
    {
        clock = new FixedClock(At);
        var options = new SupportAssistantOptions { ProviderRefusalRetryAfter = TimeSpan.FromMinutes(10) };
        return new ChatSupportResponder(new Resolver(chat), new NoTools(), options, NullLogger<ChatSupportResponder>.Instance, new SupportProviderRefusals(clock, options));
    }

    private static SupportCase Case(string teamKey) => new()
    {
        Id = $"case-{teamKey}",
        TeamKey = teamKey,
        AuthorIdentity = Alice,
        AuthorName = "Alice",
        Subject = "The export is empty.",
        Status = SupportCaseStatus.Open,
        CreatedAt = At,
        MessageCount = 1
    };

    private static IReadOnlyList<SupportMessage> Transcript() => [new() { Sequence = 1, Kind = SupportMessageKind.User, Body = "The export is empty.", SentAt = At }];

    private sealed class World
    {
        public World(ISupportAssistantAllowanceSource allowance, SupportAssistantReadiness readiness = SupportAssistantReadiness.Ready)
        {
            Responder = Substitute.For<ISupportResponder>();
            Responder.GetReadinessAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(readiness);
            Responder.AnswerAsync(Arg.Any<SupportCase>(), Arg.Any<IReadOnlyList<SupportMessage>>(), Arg.Any<CancellationToken>())
                .Returns(SupportAnswer.FromBody("Here you go.") with { Usage = new SupportAssistantUsage { InputTokens = 10, OutputTokens = 5, TotalTokens = 15 } });
            Sink = new CollectingSink();

            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Alice), new Claim(ClaimTypes.Name, "Alice"), new Claim(TeamClaimTypes.TeamKey, Team)], "test"));
            var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

            Service = new AuditingSupportCaseServiceDecorator(
                new AuthorizationSupportCaseServiceDecorator(
                    new SupportCaseService(new InMemorySupportCaseStore(), authorizer, TimeProvider.System, responder: Responder, usageSinks: [Sink], logger: NullLogger<SupportCaseService>.Instance, allowance: allowance),
                    authorizer),
                new CompositeAuditLogger(Array.Empty<IAuditLogger>(), Options.Create(new AuditOptions())),
                new AuditEntryFactory(null));
        }

        public ISupportCaseService Service { get; }

        public ISupportResponder Responder { get; }

        public CollectingSink Sink { get; }
    }

    private sealed class CollectingSink : ISupportAssistantUsageSink
    {
        public List<SupportAssistantUsageRecord> Records { get; } = [];

        public Task RecordAsync(SupportAssistantUsageRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private sealed class Resolver(IChatClient chat) : ISupportChatClientResolver
    {
        public Task<IChatClient> GetAsync(string teamKey, CancellationToken cancellationToken = default) => Task.FromResult(chat);
    }

    private sealed class NoTools : ISupportAssistantTools
    {
        public Task<IReadOnlyList<AITool>> ForCurrentCallerAsync(string teamKey, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AITool>>([]);
    }

    /// <summary>Fails or answers in turn; a null entry is an answer.</summary>
    private sealed class ScriptedChat(params Exception[] script) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        {
            var step = script[Math.Min(Calls++, script.Length - 1)];
            return step == null ? Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Here you go."))) : throw step;
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>The shape of Anthropic's exceptions: an <see cref="HttpStatusCode"/> called <c>StatusCode</c>.</summary>
    private sealed class AnthropicStyleException(int status, string message) : Exception(message)
    {
        public HttpStatusCode StatusCode => (HttpStatusCode)status;
    }

    /// <summary>The shape of OpenAI's exceptions, which Mistral, Ollama and llama.cpp are reached through: an integer called <c>Status</c>.</summary>
    private sealed class OpenAiStyleException(int status, string message) : Exception(message)
    {
        public int Status => status;
    }
}
