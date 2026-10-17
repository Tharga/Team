using MongoDB.Bson;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The teams a mail's sender matched are stored with the case and read back, and a case without any stores
/// nothing for them.
/// </summary>
public class SupportCaseSuggestionMappingTests
{
    private readonly ISupportCaseRepositoryCollection _collection = Substitute.For<ISupportCaseRepositoryCollection>();

    private static SupportCase Case(SupportCaseTeam[] suggested = null, SupportTeamSuggestionReason? reason = null) => new()
    {
        Id = "case-1",
        TeamKey = null,
        AuthorIdentity = null,
        AuthorName = "anna@example.com",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc),
        MessageCount = 1,
        SuggestedTeams = suggested ?? [],
        SuggestionReason = reason
    };

    private static SupportMessage First => new()
    {
        Sequence = 1,
        Kind = SupportMessageKind.User,
        AuthorName = "anna@example.com",
        Body = "Nothing comes out.",
        SentAt = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc),
        Source = SupportChannelType.Email
    };

    private async Task<SupportCaseEntity> StoredAsync(SupportCase supportCase)
    {
        SupportCaseEntity added = null;
        await _collection.AddAsync(Arg.Do<SupportCaseEntity>(x => added = x));

        await new MongoSupportCaseStore(_collection).AddCaseAsync(supportCase, First, TestContext.Current.CancellationToken);

        return added;
    }

    [Fact]
    public async Task SuggestedTeamsAndTheReason_AreStored()
    {
        var stored = await StoredAsync(Case(
            [new SupportCaseTeam { Key = "acme", Name = "Acme" }, new SupportCaseTeam { Key = "beta", Name = "Beta" }],
            SupportTeamSuggestionReason.SeveralTeams));

        Assert.Equal(["acme", "beta"], stored.SuggestedTeams.Select(x => x.Key));
        Assert.Equal(["Acme", "Beta"], stored.SuggestedTeams.Select(x => x.Name));

        var document = stored.ToBsonDocument();
        Assert.Equal("SeveralTeams", document[nameof(SupportCaseEntity.SuggestionReason)].AsString);
        Assert.Equal("acme", document[nameof(SupportCaseEntity.SuggestedTeams)][0]["Key"].AsString);
    }

    [Fact]
    public async Task ACaseWithNoSuggestions_StoresNoneOfIt()
    {
        var document = (await StoredAsync(Case())).ToBsonDocument();

        Assert.False(document.Contains(nameof(SupportCaseEntity.SuggestedTeams)));
        Assert.False(document.Contains(nameof(SupportCaseEntity.SuggestionReason)));
    }

    /// <summary>A reason with nothing to explain is not kept, so a stored reason always has its teams.</summary>
    [Fact]
    public async Task AReasonWithoutTeams_IsNotStored()
    {
        var stored = await StoredAsync(Case([], SupportTeamSuggestionReason.SenderNotVerified));

        Assert.Null(stored.SuggestionReason);
    }

    [Fact]
    public async Task SuggestionsAreReadBack()
    {
        var stored = await StoredAsync(Case([new SupportCaseTeam { Key = "acme", Name = "Acme" }], SupportTeamSuggestionReason.SenderNotVerified));
        _collection.GetOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>()).Returns(stored);

        var read = await new MongoSupportCaseStore(_collection).GetCaseByIdAsync("case-1", TestContext.Current.CancellationToken);

        var suggestion = Assert.Single(read.SuggestedTeams);
        Assert.Equal("acme", suggestion.Key);
        Assert.Equal("Acme", suggestion.Name);
        Assert.Equal(SupportTeamSuggestionReason.SenderNotVerified, read.SuggestionReason);
    }

    [Fact]
    public async Task ACaseStoredBeforeSuggestionsExisted_ReadsAsHavingNone()
    {
        var stored = await StoredAsync(Case());
        _collection.GetOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>()).Returns(stored);

        var read = await new MongoSupportCaseStore(_collection).GetCaseByIdAsync("case-1", TestContext.Current.CancellationToken);

        Assert.Empty(read.SuggestedTeams);
        Assert.Null(read.SuggestionReason);
    }
}
