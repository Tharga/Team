using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NSubstitute;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The filter that narrows a team's cases to what its support readers may see under per-case visibility.
/// </summary>
public class SupportCaseVisibilityQueryTests
{
    private const string Team = "acme";
    private const string Reader = "bob";

    private readonly ISupportCaseRepositoryCollection _collection = Substitute.For<ISupportCaseRepositoryCollection>();
    private FilterDefinition<SupportCaseEntity> _sentFilter;

    private MongoSupportCaseStore Store()
    {
        _collection.GetAsync(Arg.Do<FilterDefinition<SupportCaseEntity>>(x => _sentFilter = x))
            .Returns(AsyncEnumerable.Empty<SupportCaseEntity>());
        _collection.CountAsync(Arg.Do<FilterDefinition<SupportCaseEntity>>(x => _sentFilter = x)).Returns(0L);

        return new MongoSupportCaseStore(_collection);
    }

    private string RenderedFilter() =>
        _sentFilter.Render(new RenderArgs<SupportCaseEntity>(
            BsonSerializer.LookupSerializer<SupportCaseEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    [Fact]
    public async Task TheListing_AsksForTheTeamsSharedCases_OrTheReadersOwn()
    {
        await Store().GetCasesVisibleToTeamAsync(Team, Reader, null, 20, TestContext.Current.CancellationToken);

        var filter = RenderedFilter();

        Assert.Contains("\"TeamKey\" : \"acme\"", filter);
        Assert.Contains("\"Visibility\" : \"Team\"", filter);
        Assert.Contains("\"AuthorIdentity\" : \"bob\"", filter);
        Assert.Contains("$or", filter);
    }

    [Fact]
    public async Task WithNoReaderIdentity_OnlySharedCasesMatch()
    {
        await Store().GetCasesVisibleToTeamAsync(Team, null, null, 20, TestContext.Current.CancellationToken);

        var filter = RenderedFilter();

        Assert.Contains("\"Visibility\" : \"Team\"", filter);
        Assert.DoesNotContain("AuthorIdentity", filter);
    }

    [Fact]
    public async Task TheCount_IsTheListingsCasesThatAreAwaitingSupport()
    {
        await Store().GetAwaitingSupportCountVisibleToTeamAsync(Team, Reader, TestContext.Current.CancellationToken);

        var filter = RenderedFilter();

        Assert.Contains("\"Visibility\" : \"Team\"", filter);
        Assert.Contains("\"Status\" : \"Open\"", filter);
        Assert.Contains("\"LastMessageFromAuthor\" : true", filter);
    }
}
