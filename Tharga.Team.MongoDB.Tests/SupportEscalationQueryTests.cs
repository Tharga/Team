using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NSubstitute;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The two cross-team escalation queries on the real adapter: the filter each sends, and what the adapter
/// does with what comes back.
/// </summary>
/// <remarks>
/// <b>Ordering is the assertion that matters most.</b> Neither query changes the state it reads, so a case
/// already escalated keeps matching; newest first is what stops a limited sweep returning the same handled
/// cases on every pass.
/// </remarks>
public class SupportEscalationQueryTests
{
    private const string Author = "alice";
    private const string Support = "support";
    private static readonly DateTime Cutoff = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISupportCaseRepositoryCollection _collection = Substitute.For<ISupportCaseRepositoryCollection>();
    private FilterDefinition<SupportCaseEntity> _sentFilter;

    private MongoSupportCaseStore Store(params SupportCaseEntity[] returned)
    {
        _collection.GetAsync(Arg.Do<FilterDefinition<SupportCaseEntity>>(x => _sentFilter = x))
            .Returns(returned.ToAsyncEnumerable());

        return new MongoSupportCaseStore(_collection);
    }

    private static SupportCaseEntity Case(string id, int minutesBeforeCutoff, string teamKey, params SupportMessageEntity[] messages) => new()
    {
        CaseId = id,
        TeamKey = teamKey,
        AuthorIdentity = Author,
        AuthorName = "Alice",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = Cutoff.AddDays(-1),
        Messages = messages,
        LastMessageAt = Cutoff.AddMinutes(-minutesBeforeCutoff)
    };

    private static SupportMessageEntity Message(int sequence, SupportMessageKind kind, string author) => new()
    {
        Sequence = sequence,
        Kind = kind,
        AuthorIdentity = author,
        AuthorName = author,
        Body = "Anything.",
        SentAt = Cutoff.AddHours(-2).AddMinutes(sequence)
    };

    private BsonDocument RenderedFilter() =>
        _sentFilter.Render(new RenderArgs<SupportCaseEntity>(
            BsonSerializer.LookupSerializer<SupportCaseEntity>(), BsonSerializer.SerializerRegistry));

    [Fact]
    public async Task AwaitingSupport_AsksForOpenCasesTheAuthorWroteLast_BeforeTheCutoff()
    {
        await Store().GetCasesAwaitingSupportSinceAsync(Cutoff, 10, TestContext.Current.CancellationToken);

        var filter = RenderedFilter().ToString();

        Assert.Contains("\"Status\" : \"Open\"", filter);
        Assert.Contains("\"LastMessageFromAuthor\" : true", filter);
        Assert.Contains("\"LastMessageAt\" : { \"$lt\"", filter);
        Assert.DoesNotContain("TeamKey", filter);
    }

    [Fact]
    public async Task UnreadAnswer_AsksForOpenCasesSomeoneElseWroteLast_BeforeTheCutoff()
    {
        await Store().GetCasesWithUnreadAnswerSinceAsync(Cutoff, 10, TestContext.Current.CancellationToken);

        var filter = RenderedFilter().ToString();

        Assert.Contains("\"Status\" : \"Open\"", filter);
        Assert.Contains("\"LastMessageFromAuthor\" : false", filter);
        Assert.Contains("\"LastMessageAt\" : { \"$lt\"", filter);
        Assert.DoesNotContain("TeamKey", filter);
    }

    [Fact]
    public async Task AwaitingSupport_ReturnsNewestFirst_AndHonoursTheLimit()
    {
        var store = Store(
            Case("oldest", 60, "acme", Message(1, SupportMessageKind.User, Author)),
            Case("newest", 1, "globex", Message(1, SupportMessageKind.User, Author)),
            Case("middle", 30, null, Message(1, SupportMessageKind.User, Author)));

        var due = await store.GetCasesAwaitingSupportSinceAsync(Cutoff, 2, TestContext.Current.CancellationToken);

        Assert.Equal(["newest", "middle"], due.Select(x => x.Id));
    }

    [Fact]
    public async Task UnreadAnswer_KeepsOnlyCasesWithAnUnreadAnswer_NewestFirst()
    {
        var store = Store(
            Case("answered-old", 50, "acme",
                Message(1, SupportMessageKind.User, Author),
                Message(2, SupportMessageKind.User, Support)),
            Case("system-last", 5, "acme",
                Message(1, SupportMessageKind.User, Author),
                Message(2, SupportMessageKind.User, Support),
                Message(3, SupportMessageKind.System, null)),
            Case("assistant-new", 2, null,
                Message(1, SupportMessageKind.User, Author),
                Message(2, SupportMessageKind.Assistant, "assistant")));

        var due = await store.GetCasesWithUnreadAnswerSinceAsync(Cutoff, 10, TestContext.Current.CancellationToken);

        Assert.Equal(["assistant-new", "answered-old"], due.Select(x => x.Id));
    }

    [Fact]
    public async Task EachReturnedCase_CarriesTheMessageCountHostsDeduplicateOn()
    {
        var store = Store(Case("c", 5, "acme",
            Message(1, SupportMessageKind.User, Author),
            Message(2, SupportMessageKind.User, Support)));

        var due = await store.GetCasesWithUnreadAnswerSinceAsync(Cutoff, 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, Assert.Single(due).MessageCount);
    }
}
