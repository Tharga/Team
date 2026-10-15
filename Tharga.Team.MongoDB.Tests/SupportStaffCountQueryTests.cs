using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NSubstitute;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The filters the two support-staff counts send: the same "awaiting support" rule as the per-team count,
/// over the teamed and the unassigned populations respectively.
/// </summary>
public class SupportStaffCountQueryTests
{
    private readonly ISupportCaseRepositoryCollection _collection = Substitute.For<ISupportCaseRepositoryCollection>();
    private FilterDefinition<SupportCaseEntity> _sentFilter;

    private MongoSupportCaseStore Store()
    {
        _collection.CountAsync(Arg.Do<FilterDefinition<SupportCaseEntity>>(x => _sentFilter = x)).Returns(3L);

        return new MongoSupportCaseStore(_collection);
    }

    private string RenderedFilter() =>
        _sentFilter.Render(new RenderArgs<SupportCaseEntity>(
            BsonSerializer.LookupSerializer<SupportCaseEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    [Fact]
    public async Task AcrossTeams_CountsOpenCasesTheAuthorWroteLast_ThatHaveATeam()
    {
        var count = await Store().GetAwaitingSupportCountAcrossTeamsAsync(TestContext.Current.CancellationToken);

        var filter = RenderedFilter();

        Assert.Equal(3, count);
        Assert.Contains("\"TeamKey\" : { \"$exists\" : true }", filter);
        Assert.Contains("\"Status\" : \"Open\"", filter);
        Assert.Contains("\"LastMessageFromAuthor\" : true", filter);
    }

    [Fact]
    public async Task Unassigned_CountsOpenCasesTheAuthorWroteLast_ThatHaveNoTeam()
    {
        var count = await Store().GetAwaitingUnassignedCountAsync(TestContext.Current.CancellationToken);

        var filter = RenderedFilter();

        Assert.Equal(3, count);
        Assert.Contains("\"TeamKey\" : { \"$exists\" : false }", filter);
        Assert.Contains("\"Status\" : \"Open\"", filter);
        Assert.Contains("\"LastMessageFromAuthor\" : true", filter);
    }
}
