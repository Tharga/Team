using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Every time the assistant is asked, one usage record: who, where, which model, how much, how long, and never what
/// anybody wrote.
/// </summary>
public class SupportAssistantUsageRecordingTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Question = "The export is empty, my password is hunter2.";
    private const string AnswerText = "Check the date filter.";

    private static readonly SupportAssistantUsage Usage = new()
    {
        InputTokens = 610,
        CachedInputTokens = 400,
        OutputTokens = 220,
        TotalTokens = 830,
        Provider = "openai",
        Model = "gpt-4.1"
    };

    private static readonly SupportAssistantCall[] TwoCalls = [new() { Name = "search_my_cases" }, new() { Name = "get_case" }];

    [Fact]
    public async Task AnAnswer_IsRecorded_WithEverythingButWhatWasWritten()
    {
        var sink = new CollectingSink();
        var world = new World(Answering(SupportAnswer.FromBody(AnswerText) with { Usage = Usage, Calls = TwoCalls }), sink);
        var raised = await world.Service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        Assert.True(await world.Service.RunAssistantAsync(TeamA, raised.Id));

        var record = Assert.Single(sink.Records);
        Assert.Equal(TeamA, record.TeamKey);
        Assert.Equal(raised.Id, record.CaseId);
        Assert.Equal(Alice, record.AskedBy);
        Assert.Equal("openai", record.Provider);
        Assert.Equal("gpt-4.1", record.Model);
        Assert.Equal(610, record.InputTokens);
        Assert.Equal(400, record.CachedInputTokens);
        Assert.Equal(220, record.OutputTokens);
        Assert.Equal(830, record.TotalTokens);
        Assert.Equal(2, record.ToolCalls);
        Assert.True(record.Answered);
        Assert.True(record.Duration >= TimeSpan.Zero);
        Assert.NotEqual(default, record.At);

        var text = JsonSerializer.Serialize(record);
        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain(AnswerText, text);
    }

    /// <summary>A decline spent the tokens all the same, so it is what a bill is most likely to miss.</summary>
    [Fact]
    public async Task ADecline_IsRecordedToo()
    {
        var sink = new CollectingSink();
        var world = new World(Answering(SupportAnswer.Declined("The assistant returned nothing.") with { Usage = Usage }), sink);
        var raised = await world.Service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        Assert.False(await world.Service.RunAssistantAsync(TeamA, raised.Id));

        var record = Assert.Single(sink.Records);
        Assert.False(record.Answered);
        Assert.Equal(830, record.TotalTokens);
    }

    /// <summary>A host's own responder that reports nothing leaves nothing to count, rather than a row of zeros.</summary>
    [Fact]
    public async Task AnAnswerWithNoUsage_IsNotRecorded()
    {
        var sink = new CollectingSink();
        var world = new World(Answering(SupportAnswer.FromBody(AnswerText)), sink);
        var raised = await world.Service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        Assert.True(await world.Service.RunAssistantAsync(TeamA, raised.Id));

        Assert.Empty(sink.Records);
    }

    /// <summary>The answer is the customer's; a record the host failed to keep is the host's problem, and is logged.</summary>
    [Fact]
    public async Task ASinkThatThrows_IsSkipped_AndTheAnswerStillAppended_AndTheOtherSinksStillTold()
    {
        var after = new CollectingSink();
        var world = new World(Answering(SupportAnswer.FromBody(AnswerText) with { Usage = Usage }), new ThrowingSink(), after);
        var raised = await world.Service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        Assert.True(await world.Service.RunAssistantAsync(TeamA, raised.Id));

        Assert.Equal(AnswerText, (await world.Service.GetMessagesAsync(TeamA, raised.Id)).Items[^1].Body);
        Assert.Single(after.Records);
    }

    private static ISupportResponder Answering(SupportAnswer answer)
    {
        var responder = Substitute.For<ISupportResponder>();
        responder.AnswerAsync(Arg.Any<SupportCase>(), Arg.Any<IReadOnlyList<SupportMessage>>(), Arg.Any<CancellationToken>()).Returns(answer);
        return responder;
    }

    private sealed class World
    {
        public World(ISupportResponder responder, params ISupportAssistantUsageSink[] sinks)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Alice), new Claim(ClaimTypes.Name, "Alice"), new Claim(TeamClaimTypes.TeamKey, TeamA)], "test"));
            var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

            Service = new AuditingSupportCaseServiceDecorator(
                new AuthorizationSupportCaseServiceDecorator(
                    new SupportCaseService(new InMemorySupportCaseStore(), authorizer, TimeProvider.System, responder: responder, usageSinks: sinks, logger: NullLogger<SupportCaseService>.Instance),
                    authorizer),
                new CompositeAuditLogger(Array.Empty<IAuditLogger>(), Options.Create(new AuditOptions())),
                new AuditEntryFactory(null));
        }

        public ISupportCaseService Service { get; }
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

    private sealed class ThrowingSink : ISupportAssistantUsageSink
    {
        public Task RecordAsync(SupportAssistantUsageRecord record, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The usage database is down.");
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
