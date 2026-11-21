using MongoDB.Bson;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>The service calls kept with an answer, and an entry without any storing nothing for them.</summary>
public class SupportAssistantCallMappingTests
{
    private static SupportMessage Answer(params SupportAssistantCall[] calls) => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.Assistant,
        AuthorName = "Assistant",
        Body = "You have two cases.",
        SentAt = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc),
        AssistantCalls = calls
    };

    [Fact]
    public void TheCalls_RoundTrip_InOrder()
    {
        var stored = MongoSupportCaseStore.ToEntity(Answer(new() { Name = "get_my_cases" }, new() { Name = "search_my_cases", Arguments = "text: \"export\"" }));

        var calls = MongoSupportCaseStore.ToMessage(stored).AssistantCalls;

        Assert.Equal(["get_my_cases", "search_my_cases"], calls.Select(x => x.Name));
        Assert.Equal(["", "text: \"export\""], calls.Select(x => x.Arguments));
    }

    [Fact]
    public void ACallThatTookNothing_StoresNoArguments()
    {
        var stored = MongoSupportCaseStore.ToEntity(Answer(new SupportAssistantCall { Name = "get_my_cases" })).ToBsonDocument();

        Assert.False(stored[nameof(SupportMessageEntity.AssistantCalls)][0].AsBsonDocument.Contains(nameof(SupportAssistantCallEntity.Arguments)));
    }

    /// <summary>What a tool returned is kept as it was, and read back unchanged (Tharga/Team#545).</summary>
    [Fact]
    public void AResult_RoundTrips()
    {
        var stored = MongoSupportCaseStore.ToEntity(Answer(new SupportAssistantCall { Name = "get_my_cases", Result = "{\"cases\":[{\"id\":\"c1\"}]}" }));

        var calls = MongoSupportCaseStore.ToMessage(stored).AssistantCalls;

        Assert.Equal("{\"cases\":[{\"id\":\"c1\"}]}", Assert.Single(calls).Result);
    }

    /// <summary>With nothing kept, nothing is stored: the document is as it was before the field existed.</summary>
    [Fact]
    public void ACallWithNoResult_StoresNoResultField()
    {
        var stored = MongoSupportCaseStore.ToEntity(Answer(new SupportAssistantCall { Name = "get_my_cases" })).ToBsonDocument();

        Assert.False(stored[nameof(SupportMessageEntity.AssistantCalls)][0].AsBsonDocument.Contains(nameof(SupportAssistantCallEntity.Result)));
    }

    /// <summary>A message stored before the field existed reads back with an empty result, not a null one.</summary>
    [Fact]
    public void AMessageKeptBeforeResultsWereRecorded_ReadsWithAnEmptyResult()
    {
        var stored = MongoSupportCaseStore.ToEntity(Answer(new SupportAssistantCall { Name = "get_my_cases" })) with
        {
            AssistantCalls = [new SupportAssistantCallEntity { Name = "get_my_cases", Arguments = "text: \"export\"", Succeeded = true }]
        };

        var call = Assert.Single(MongoSupportCaseStore.ToMessage(stored).AssistantCalls);

        Assert.Equal(string.Empty, call.Result);
        Assert.Equal("text: \"export\"", call.Arguments);
    }

    [Fact]
    public void TheTokens_RoundTrip()
    {
        var stored = MongoSupportCaseStore.ToEntity(Answer() with { AssistantUsage = new SupportAssistantUsage { InputTokens = 610, OutputTokens = 220, TotalTokens = 830, CachedInputTokens = 400, Provider = "openai", Model = "gpt-4.1" } });

        Assert.Equal(new SupportAssistantUsage { InputTokens = 610, OutputTokens = 220, TotalTokens = 830, CachedInputTokens = 400, Provider = "openai", Model = "gpt-4.1" }, MongoSupportCaseStore.ToMessage(stored).AssistantUsage);
    }

    [Fact]
    public void HowLongTheAssistantTook_RoundTrips_ToTheMillisecond()
    {
        var stored = MongoSupportCaseStore.ToEntity(Answer() with { AssistantUsage = new SupportAssistantUsage { TotalTokens = 830, Elapsed = TimeSpan.FromMilliseconds(83_250) } });

        Assert.Equal(83_250, stored.AssistantUsage.ElapsedMs);
        Assert.Equal(TimeSpan.FromMilliseconds(83_250), MongoSupportCaseStore.ToMessage(stored).AssistantUsage.Elapsed);
    }

    [Fact]
    public void AnAnswerKeptBeforeTheTimeWasRecorded_ReadsWithNone_AndStoresNoField()
    {
        var stored = MongoSupportCaseStore.ToEntity(Answer() with { AssistantUsage = new SupportAssistantUsage { TotalTokens = 830 } });

        Assert.Null(MongoSupportCaseStore.ToMessage(stored).AssistantUsage.Elapsed);
        Assert.False(stored.AssistantUsage.ToBsonDocument().Contains(nameof(SupportAssistantUsageEntity.ElapsedMs)));
    }

    [Fact]
    public void AnEntryWithoutTokens_StoresNone()
    {
        var stored = MongoSupportCaseStore.ToEntity(Answer());

        Assert.False(stored.ToBsonDocument().Contains(nameof(SupportMessageEntity.AssistantUsage)));
        Assert.Null(MongoSupportCaseStore.ToMessage(stored).AssistantUsage);
    }

    [Fact]
    public void AnEntryWithoutCalls_StoresNoneAndReadsEmpty()
    {
        var stored = MongoSupportCaseStore.ToEntity(Answer());

        Assert.False(stored.ToBsonDocument().Contains(nameof(SupportMessageEntity.AssistantCalls)));
        Assert.Empty(MongoSupportCaseStore.ToMessage(stored).AssistantCalls);
    }
}
