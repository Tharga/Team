using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NSubstitute;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Recording a case's channel binding works on cases stored before bindings were always initialised.
/// </summary>
public class SupportBindingWriteTests
{
    private readonly ISupportCaseRepositoryCollection _collection = Substitute.For<ISupportCaseRepositoryCollection>();
    private UpdateDefinition<SupportCaseEntity> _sentUpdate;

    private MongoSupportCaseStore Store(SupportCaseEntity stored)
    {
        _collection.GetOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>()).Returns(stored);
        _collection.UpdateOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>(), Arg.Do<UpdateDefinition<SupportCaseEntity>>(x => _sentUpdate = x));

        return new MongoSupportCaseStore(_collection);
    }

    private static SupportCaseEntity Case(SupportChannelBindingEntity[] bindings) => new()
    {
        CaseId = "case-1",
        TeamKey = "acme",
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Subject = "Subject",
        Status = SupportCaseStatus.Open,
        CreatedAt = DateTime.UtcNow,
        Messages = [],
        Bindings = bindings
    };

    private static readonly SupportChannelBinding Slack = new() { ChannelType = SupportChannelType.Slack, ExternalId = "1.0" };

    private string RenderedUpdate() =>
        _sentUpdate.Render(new RenderArgs<SupportCaseEntity>(
            BsonSerializer.LookupSerializer<SupportCaseEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    [Fact]
    public async Task ACaseStoredWithNoBindings_GetsItsFirstOneSet()
    {
        await Store(Case(null)).AddBindingAsync("acme", "case-1", Slack, TestContext.Current.CancellationToken);

        Assert.Contains("$set", RenderedUpdate());
        Assert.DoesNotContain("$push", RenderedUpdate());
    }

    [Fact]
    public async Task ACaseWithBindings_HasTheNewOneAppended()
    {
        await Store(Case([])).AddBindingAsync("acme", "case-1", Slack, TestContext.Current.CancellationToken);

        Assert.Contains("$push", RenderedUpdate());
    }
}
