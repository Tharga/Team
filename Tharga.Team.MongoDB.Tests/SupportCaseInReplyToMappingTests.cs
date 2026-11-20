using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The mail in the mail log a case's first mail answered is stored with the case and read back, and absent from every case that answered none
/// (Tharga/Team#529).
/// </summary>
public class SupportCaseInReplyToMappingTests
{
    private static readonly DateTime Raised = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly ISupportCaseRepositoryCollection _collection = Substitute.For<ISupportCaseRepositoryCollection>();

    private static SupportCase Case(string inReplyToMailId) => new()
    {
        Id = "case-1",
        AuthorIdentity = null,
        AuthorName = "user@customer.example",
        Subject = "Cannot sign in",
        Status = SupportCaseStatus.Open,
        CreatedAt = Raised,
        MessageCount = 1,
        InReplyToMailId = inReplyToMailId
    };

    private static SupportMessage First => new()
    {
        Sequence = 1,
        Kind = SupportMessageKind.User,
        AuthorName = "user@customer.example",
        Body = "I cannot sign in.",
        SentAt = Raised
    };

    private async Task<SupportCaseEntity> StoredAsync(SupportCase supportCase)
    {
        SupportCaseEntity added = null;
        await _collection.AddAsync(Arg.Do<SupportCaseEntity>(x => added = x));

        await new MongoSupportCaseStore(_collection).AddCaseAsync(supportCase, First, TestContext.Current.CancellationToken);

        return added;
    }

    [Fact]
    public async Task TheMailAnswered_IsStoredWithTheCase()
    {
        var stored = await StoredAsync(Case("mail-1"));

        Assert.Equal("mail-1", stored.InReplyToMailId);
    }

    [Fact]
    public async Task ACaseThatAnsweredNoMail_StoresNone_AndTheFieldIsAbsentFromTheDocument()
    {
        var stored = await StoredAsync(Case(null));

        Assert.Null(stored.InReplyToMailId);
        Assert.False(stored.ToBsonDocument().Contains(nameof(SupportCaseEntity.InReplyToMailId)));
    }

    [Fact]
    public async Task TheMailAnswered_ComesBackOnTheCaseReadFromTheStore()
    {
        var stored = await StoredAsync(Case("mail-1"));
        _collection.GetOneAsync(Arg.Any<global::MongoDB.Driver.FilterDefinition<SupportCaseEntity>>()).Returns(stored);

        var read = await new MongoSupportCaseStore(_collection).GetCaseAsync("team-a", "case-1", TestContext.Current.CancellationToken);

        Assert.Equal("mail-1", read?.InReplyToMailId);
    }
}
