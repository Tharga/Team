using System.Reflection;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The short tracking token a case's reply address carries (Tharga/Team#465): stored once per case, unique across cases,
/// and found again from the address.
/// </summary>
public class SupportTrackingTokenStoreTests
{
    private readonly ISupportCaseRepositoryCollection _collection = Substitute.For<ISupportCaseRepositoryCollection>();

    private MongoSupportCaseStore Store() => new(_collection);

    private static SupportCaseEntity Entity(string token = null) => new()
    {
        CaseId = "case-1",
        TeamKey = "acme",
        AuthorIdentity = "sub-1",
        AuthorName = "A User",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc),
        Messages = [],
        TrackingToken = token
    };

    private void CaseIs(SupportCaseEntity entity, params SupportCaseEntity[] then)
        => _collection.GetOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>(), Arg.Any<OneOption<SupportCaseEntity>>(), Arg.Any<CancellationToken>()).Returns(entity, then);

    private void UpdateReturns(EntityChangeResult<SupportCaseEntity> result)
        => _collection.UpdateOneAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>(), Arg.Any<UpdateDefinition<SupportCaseEntity>>(), Arg.Any<OneOption<SupportCaseEntity>>(), Arg.Any<IClientSessionHandle>()).Returns(result);

    private static EntityChangeResult<T> Changed<T>(T before)
        => (EntityChangeResult<T>)Activator.CreateInstance(typeof(EntityChangeResult<T>), BindingFlags.NonPublic | BindingFlags.Instance, null, [before, before], null);

    [Fact]
    public async Task ACaseThatHasATokenKeepsIt_AndNothingIsWritten()
    {
        CaseIs(Entity("tracking-aaaaaaaaaaaa"));

        var held = await Store().TrySetTrackingTokenAsync("acme", "case-1", "tracking-bbbbbbbbbbbb", TestContext.Current.CancellationToken);

        Assert.Equal("tracking-aaaaaaaaaaaa", held);
        await _collection.DidNotReceiveWithAnyArgs().UpdateOneAsync(default(FilterDefinition<SupportCaseEntity>), default, default, default);
    }

    [Fact]
    public async Task ATokenAnotherCaseHolds_IsRefused_AndNothingIsWritten()
    {
        CaseIs(Entity());
        _collection.CountAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>()).Returns(1L);

        Assert.Null(await Store().TrySetTrackingTokenAsync("acme", "case-1", "tracking-bbbbbbbbbbbb", TestContext.Current.CancellationToken));
        await _collection.DidNotReceiveWithAnyArgs().UpdateOneAsync(default(FilterDefinition<SupportCaseEntity>), default, default, default);
    }

    [Fact]
    public async Task AFreeToken_IsSet_OnlyWhileTheCaseHasNone()
    {
        FilterDefinition<SupportCaseEntity> filter = null;
        CaseIs(Entity());
        _collection.CountAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>()).Returns(0L);
        _collection.UpdateOneAsync(Arg.Do<FilterDefinition<SupportCaseEntity>>(x => filter = x), Arg.Any<UpdateDefinition<SupportCaseEntity>>(), Arg.Any<OneOption<SupportCaseEntity>>(), Arg.Any<IClientSessionHandle>())
            .Returns(Changed(Entity()));

        var held = await Store().TrySetTrackingTokenAsync("acme", "case-1", "tracking-bbbbbbbbbbbb", TestContext.Current.CancellationToken);

        Assert.Equal("tracking-bbbbbbbbbbbb", held);
        var rendered = filter.Render(new RenderArgs<SupportCaseEntity>(global::MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<SupportCaseEntity>(), global::MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry)).ToString();
        Assert.Contains("\"CaseId\" : \"case-1\"", rendered);
        Assert.Contains("\"TrackingToken\" : null", rendered);
    }

    [Fact]
    public async Task AnAnswerThatLosesTheRace_ReturnsTheWinnersToken()
    {
        // The case had none when read; by the time this write ran another answer had set one.
        CaseIs(Entity(), Entity("tracking-aaaaaaaaaaaa"));
        _collection.CountAsync(Arg.Any<FilterDefinition<SupportCaseEntity>>()).Returns(0L);
        UpdateReturns(null);

        var held = await Store().TrySetTrackingTokenAsync("acme", "case-1", "tracking-bbbbbbbbbbbb", TestContext.Current.CancellationToken);

        Assert.Equal("tracking-aaaaaaaaaaaa", held);
    }

    [Fact]
    public async Task ACaseIsFoundByItsToken_WithTheTokenOnIt()
    {
        CaseIs(Entity("tracking-aaaaaaaaaaaa"));

        var found = await Store().GetCaseByTrackingTokenAsync("tracking-aaaaaaaaaaaa", TestContext.Current.CancellationToken);

        Assert.Equal(("case-1", "tracking-aaaaaaaaaaaa"), (found.Id, found.TrackingToken));
        Assert.Null(await Store().GetCaseByTrackingTokenAsync(" ", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheTokenIsStoredOnlyWhenSet_SoACaseWrittenBeforeCarriesNoField()
    {
        Assert.False(Entity().ToBsonDocument().Contains(nameof(SupportCaseEntity.TrackingToken)));
        Assert.Equal("tracking-aaaaaaaaaaaa", Entity("tracking-aaaaaaaaaaaa").ToBsonDocument()[nameof(SupportCaseEntity.TrackingToken)].AsString);
    }

    [Fact]
    public void TheTokenIndex_IsUniqueAndHoldsOnlyCasesThatHaveOne()
    {
        var collection = (SupportCaseRepositoryCollection)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SupportCaseRepositoryCollection));

        var index = collection.Indices.Single(x => x.Options.Name == "TrackingToken");

        Assert.True(index.Options.Unique);
        Assert.NotNull(index.Options.PartialFilterExpression);
    }
}
