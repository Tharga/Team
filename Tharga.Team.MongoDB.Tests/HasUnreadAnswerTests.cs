namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The half of the unread-answer escalation query that cannot be a database filter.
/// </summary>
/// <remarks>
/// Pinned against the real adapter for the same reason as <see cref="SupportWroteLastTests"/>: the in-memory
/// store mirrors this logic and would pass just as happily if the two disagreed.
/// </remarks>
public class HasUnreadAnswerTests
{
    private const string Author = "alice";
    private const string Support = "support";

    private static SupportCaseEntity Case(SupportCaseReadEntity[] reads, params SupportMessageEntity[] messages) => new()
    {
        CaseId = "case-1",
        TeamKey = "acme",
        AuthorIdentity = Author,
        AuthorName = "Alice",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        Messages = messages,
        Reads = reads
    };

    private static SupportMessageEntity Message(int sequence, SupportMessageKind kind, string author) => new()
    {
        Sequence = sequence,
        Kind = kind,
        AuthorIdentity = author,
        AuthorName = author,
        Body = "Anything.",
        SentAt = new DateTime(2026, 9, 1, 8, sequence, 0, DateTimeKind.Utc)
    };

    private static SupportCaseReadEntity ReadUpTo(string identity, int sequence) => new()
    {
        Identity = identity,
        LastReadSequence = sequence,
        ReadAt = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void ASupportAnswerNeverRead_Counts()
    {
        var entity = Case(null,
            Message(1, SupportMessageKind.User, Author),
            Message(2, SupportMessageKind.User, Support));

        Assert.True(MongoSupportCaseStore.HasUnreadAnswer(entity));
    }

    [Fact]
    public void AnAssistantAnswer_Counts()
    {
        var entity = Case([ReadUpTo(Author, 1)],
            Message(1, SupportMessageKind.User, Author),
            Message(2, SupportMessageKind.Assistant, "assistant"));

        Assert.True(MongoSupportCaseStore.HasUnreadAnswer(entity));
    }

    [Fact]
    public void AnAnswerTheAuthorHasRead_DoesNotCount()
    {
        var entity = Case([ReadUpTo(Author, 2)],
            Message(1, SupportMessageKind.User, Author),
            Message(2, SupportMessageKind.User, Support));

        Assert.False(MongoSupportCaseStore.HasUnreadAnswer(entity));
    }

    [Fact]
    public void SupportHavingReadIt_DoesNotMakeItRead()
    {
        var entity = Case([ReadUpTo(Support, 2), ReadUpTo(Author, 1)],
            Message(1, SupportMessageKind.User, Author),
            Message(2, SupportMessageKind.User, Support));

        Assert.True(MongoSupportCaseStore.HasUnreadAnswer(entity));
    }

    [Fact]
    public void ASystemEntryLast_DoesNotCount_EvenOverAnUnreadAnswer()
    {
        var entity = Case(null,
            Message(1, SupportMessageKind.User, Author),
            Message(2, SupportMessageKind.User, Support),
            Message(3, SupportMessageKind.System, null));

        Assert.False(MongoSupportCaseStore.HasUnreadAnswer(entity));
    }

    [Fact]
    public void TheAuthorWritingLast_DoesNotCount()
    {
        var entity = Case(null,
            Message(1, SupportMessageKind.User, Author),
            Message(2, SupportMessageKind.User, Support),
            Message(3, SupportMessageKind.User, Author));

        Assert.False(MongoSupportCaseStore.HasUnreadAnswer(entity));
    }

    [Fact]
    public void AnEmptyTranscript_DoesNotCount()
    {
        Assert.False(MongoSupportCaseStore.HasUnreadAnswer(Case(null)));
    }
}
