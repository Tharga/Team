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

    /// <summary>The price as it is when the answer is given goes on the record with the cost it worked out, so a later price change never rewrites it.</summary>
    [Fact]
    public async Task APricedAnswer_KeepsThePricesItWasGivenAt_AndWhatItCost()
    {
        var sink = new CollectingSink();
        var pricing = Substitute.For<ISupportAssistantPricing>();
        pricing.GetAsync(TeamA, Arg.Any<CancellationToken>()).Returns(new PlatformAiModel { Id = "m1", PriceInput = 3m, PriceCachedInput = 0.3m, PriceOutput = 15m, Currency = "USD" });
        var world = new World(pricing, Answering(SupportAnswer.FromBody(AnswerText) with { Usage = Usage }), sink);
        var raised = await world.Service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        await world.Service.RunAssistantAsync(TeamA, raised.Id);

        var record = Assert.Single(sink.Records);
        Assert.Equal((3m, 0.3m, 15m, "USD"), (record.PriceInput, record.PriceCachedInput, record.PriceOutput, record.Currency));
        // 210 fresh input at 3, 400 cached at 0.3, 220 output at 15, per million tokens.
        Assert.Equal((210 * 3m + 400 * 0.3m + 220 * 15m) / 1_000_000m, record.Cost);
    }

    [Fact]
    public async Task AnAnswerWithNoPrice_IsRecordedWithNoCost_NotACostOfZero()
    {
        var sink = new CollectingSink();
        var pricing = Substitute.For<ISupportAssistantPricing>();
        pricing.GetAsync(TeamA, Arg.Any<CancellationToken>()).Returns((PlatformAiModel)null);
        var world = new World(pricing, Answering(SupportAnswer.FromBody(AnswerText) with { Usage = Usage }), sink);
        var raised = await world.Service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        await world.Service.RunAssistantAsync(TeamA, raised.Id);

        var record = Assert.Single(sink.Records);
        Assert.Null(record.Cost);
        Assert.Null(record.Currency);
    }

    /// <summary>The answer is the customer's: a price that cannot be read costs the record its price, nothing more.</summary>
    [Fact]
    public async Task APriceThatCannotBeRead_StillLeavesTheAnswerAndItsRecord()
    {
        var sink = new CollectingSink();
        var pricing = Substitute.For<ISupportAssistantPricing>();
        pricing.GetAsync(TeamA, Arg.Any<CancellationToken>()).Returns<PlatformAiModel>(_ => throw new InvalidOperationException("The catalogue is down."));
        var world = new World(pricing, Answering(SupportAnswer.FromBody(AnswerText) with { Usage = Usage }), sink);
        var raised = await world.Service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        Assert.True(await world.Service.RunAssistantAsync(TeamA, raised.Id));

        Assert.Null(Assert.Single(sink.Records).Cost);
    }

    [Theory]
    [InlineData(1000, 0, 500, "3", "0.3", "15", "0.0105")]
    [InlineData(1000, 400, 0, "3", "0.3", "15", "0.00192")]
    [InlineData(1000, 400, 0, "3", null, "15", "0.003")]
    [InlineData(1000, 9999, 0, "3", "0.3", "15", "0.0003")]
    public void TheCost_IsPerMillionTokens_WithCachedInputAtItsOwnPrice(long input, long cached, long output, string priceInput, string priceCached, string priceOutput, string expected)
    {
        static decimal? Price(string text) => text == null ? null : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(Price(expected), SupportAssistantUsageRecord.CostOf(input, cached, output, Price(priceInput), Price(priceCached), Price(priceOutput)));
    }

    [Fact]
    public void WithNoPriceAtAll_ThereIsNoCost()
        => Assert.Null(SupportAssistantUsageRecord.CostOf(1000, 0, 500, null, null, null));

    [Theory]
    [InlineData(1000, 0, 400, 0, "3", "0.3", "3.75", "15", "0.0033")]
    [InlineData(1000, 200, 300, 100, "3", "0.3", "3.75", "15", "0.004185")]
    [InlineData(1000, 0, 400, 0, "3", "0.3", null, "15", "0.003")]
    [InlineData(1000, 800, 800, 0, "3", "0.3", "3.75", "15", "0.00099")]
    [InlineData(1000, 0, 9999, 0, "3", "0.3", "3.75", "15", "0.00375")]
    public void CacheWrites_ArePricedAtTheirOwnPrice_OrAsInputWhenNoneIsGiven(long input, long cached, long written, long output, string priceInput, string priceCached, string priceWrite, string priceOutput, string expected)
    {
        static decimal? Price(string text) => text == null ? null : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(Price(expected), SupportAssistantUsageRecord.CostOf(input, cached, written, output, Price(priceInput), Price(priceCached), Price(priceWrite), Price(priceOutput)));
    }

    [Fact]
    public void WithOnlyACacheWritePrice_ThereIsACost()
        => Assert.NotNull(SupportAssistantUsageRecord.CostOf(1000, 0, 400, 0, null, null, 3.75m, null));

    [Fact]
    public void TheOldOverload_PricesNoWrites()
        => Assert.Equal(SupportAssistantUsageRecord.CostOf(1000, 400, 0, 500, 3m, 0.3m, null, 15m), SupportAssistantUsageRecord.CostOf(1000, 400, 500, 3m, 0.3m, 15m));

    [Fact]
    public async Task APricedAnswer_CountsCacheWritesAtTheirPrice_AndKeepsThePrice()
    {
        var sink = new CollectingSink();
        var pricing = Substitute.For<ISupportAssistantPricing>();
        pricing.GetAsync(TeamA, Arg.Any<CancellationToken>()).Returns(new PlatformAiModel { Id = "m1", PriceInput = 3m, PriceCachedInput = 0.3m, PriceCacheWrite = 3.75m, PriceOutput = 15m, Currency = "USD" });
        var world = new World(pricing, Answering(SupportAnswer.FromBody(AnswerText) with { Usage = Usage with { CacheWriteTokens = 100 } }), sink);
        var raised = await world.Service.RaiseCaseAsync(TeamA, null, Question, SupportAssistance.Assistant);

        await world.Service.RunAssistantAsync(TeamA, raised.Id);

        var record = Assert.Single(sink.Records);
        Assert.Equal((100L, 3.75m), (record.CacheWriteTokens, record.PriceCacheWrite));
        // 110 fresh input at 3, 400 cached at 0.3, 100 written at 3.75, 220 output at 15, per million tokens.
        Assert.Equal((110 * 3m + 400 * 0.3m + 100 * 3.75m + 220 * 15m) / 1_000_000m, record.Cost);
    }

    private static ISupportResponder Answering(SupportAnswer answer)
    {
        var responder = Substitute.For<ISupportResponder>();
        responder.AnswerAsync(Arg.Any<SupportCase>(), Arg.Any<IReadOnlyList<SupportMessage>>(), Arg.Any<CancellationToken>()).Returns(answer);
        return responder;
    }

    private sealed class World
    {
        public World(ISupportResponder responder, params ISupportAssistantUsageSink[] sinks) : this(null, responder, sinks)
        {
        }

        public World(ISupportAssistantPricing pricing, ISupportResponder responder, params ISupportAssistantUsageSink[] sinks)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Alice), new Claim(ClaimTypes.Name, "Alice"), new Claim(TeamClaimTypes.TeamKey, TeamA)], "test"));
            var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

            Service = new AuditingSupportCaseServiceDecorator(
                new AuthorizationSupportCaseServiceDecorator(
                    new SupportCaseService(new InMemorySupportCaseStore(), authorizer, TimeProvider.System, responder: responder, usageSinks: sinks, logger: NullLogger<SupportCaseService>.Instance, pricing: pricing),
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
