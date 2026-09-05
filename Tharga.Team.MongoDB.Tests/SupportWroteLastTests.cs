namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The half of the inactivity predicate that cannot be a database filter.
/// </summary>
/// <remarks>
/// <b>Tested against the real adapter, not a double.</b> The sweep's own tests run on an in-memory store that
/// mirrors this logic — which is useful, and would pass just as happily if the two disagreed. Since "is the
/// last element of an embedded array a person's entry" is decided here in the adapter, here is where it has
/// to be pinned.
/// <para>
/// <b>The system-entry case is the one that matters.</b> A reopen note is a system entry, so counting it as
/// "support answered" would arm the very clock that closes a case somebody has just reopened.
/// </para>
/// </remarks>
public class SupportWroteLastTests
{
    private const string Author = "alice";
    private const string Support = "support";

    private static SupportCaseEntity Case(params SupportMessageEntity[] messages) => new()
    {
        CaseId = "case-1",
        TeamKey = "acme",
        AuthorIdentity = Author,
        AuthorName = "Alice",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        Messages = messages
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

    [Fact]
    public void SupportAnsweringLast_Counts()
    {
        var entity = Case(
            Message(1, SupportMessageKind.User, Author),
            Message(2, SupportMessageKind.User, Support));

        Assert.True(MongoSupportCaseStore.SupportWroteLast(entity));
    }

    [Fact]
    public void TheAuthorWritingLast_DoesNotCount()
    {
        var entity = Case(
            Message(1, SupportMessageKind.User, Author),
            Message(2, SupportMessageKind.User, Support),
            Message(3, SupportMessageKind.User, Author));

        Assert.False(MongoSupportCaseStore.SupportWroteLast(entity));
    }

    /// <summary>The reopen case: the toolkit spoke last, and that is not support answering.</summary>
    [Fact]
    public void ASystemEntryLast_DoesNotCount()
    {
        var entity = Case(
            Message(1, SupportMessageKind.User, Author),
            Message(2, SupportMessageKind.User, Support),
            Message(3, SupportMessageKind.System, null));

        Assert.False(MongoSupportCaseStore.SupportWroteLast(entity));
    }

    /// <summary>
    /// Sequence decides which entry is newest, not array order — the array is whatever order the driver
    /// returned it in.
    /// </summary>
    [Fact]
    public void TheNewestEntry_IsFoundBySequence_NotByPosition()
    {
        var entity = Case(
            Message(3, SupportMessageKind.User, Author),
            Message(1, SupportMessageKind.User, Author),
            Message(2, SupportMessageKind.User, Support));

        Assert.False(MongoSupportCaseStore.SupportWroteLast(entity));
    }

    [Fact]
    public void ACaseWithNoEntriesAtAll_DoesNotCount()
    {
        Assert.False(MongoSupportCaseStore.SupportWroteLast(Case()));
    }

    /// <summary>
    /// A case only the author has written to is waiting on support. It must never be eligible, however old.
    /// </summary>
    [Fact]
    public void AnUnansweredCase_DoesNotCount()
    {
        Assert.False(MongoSupportCaseStore.SupportWroteLast(Case(Message(1, SupportMessageKind.User, Author))));
    }

    /// <summary>
    /// A case that arrived by mail: nobody involved has an account, so the author identity is null on the
    /// case and on everything the customer writes.
    /// </summary>
    /// <remarks>
    /// <b>This is why an inbound entry carries no identity.</b> The predicate is "the newest entry is not the
    /// author's", so if a customer mail arrived carrying its own address as the identity, that address would
    /// never equal the case's null author — and the customer writing in would count as support answering,
    /// closing their case seven days after they asked for help.
    /// </remarks>
    [Theory]
    [InlineData(null, false)]
    [InlineData(Support, true)]
    public void OnACaseFromMail_OnlyAnAnswerWithAnIdentityCounts(string lastAuthor, bool expected)
    {
        var fromMail = Case(
            Message(1, SupportMessageKind.User, null),
            Message(2, SupportMessageKind.User, lastAuthor)) with { AuthorIdentity = null };

        Assert.Equal(expected, MongoSupportCaseStore.SupportWroteLast(fromMail));
    }
}
