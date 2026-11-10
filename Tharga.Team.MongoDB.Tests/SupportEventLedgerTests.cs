using MongoDB.Driver;
using NSubstitute;

namespace Tharga.Team.MongoDB.Tests;

public class SupportEventLedgerTests
{
    private readonly ISupportEventLedgerCollection _collection = Substitute.For<ISupportEventLedgerCollection>();

    [Fact]
    public async Task AnEventAlreadyRecorded_IsAnsweredWithoutAnInsert_SoNoDuplicateErrorIsLogged()
    {
        _collection.CountAsync(Arg.Any<FilterDefinition<SupportEventLedgerEntity>>()).Returns(1L);

        var recorded = await new MongoSupportEventLedger(_collection, TimeProvider.System).TryRecordAsync("email", "<m1@example.org>", TestContext.Current.CancellationToken);

        Assert.False(recorded);
        await _collection.DidNotReceive().TryAddAsync(Arg.Any<SupportEventLedgerEntity>(), Arg.Any<IClientSessionHandle>());
    }

    [Fact]
    public async Task ANewEvent_IsRecorded()
    {
        _collection.CountAsync(Arg.Any<FilterDefinition<SupportEventLedgerEntity>>()).Returns(0L);
        _collection.TryAddAsync(Arg.Any<SupportEventLedgerEntity>(), Arg.Any<IClientSessionHandle>()).Returns(true);

        var recorded = await new MongoSupportEventLedger(_collection, TimeProvider.System).TryRecordAsync("email", "<m1@example.org>", TestContext.Current.CancellationToken);

        Assert.True(recorded);
        await _collection.Received(1).TryAddAsync(Arg.Is<SupportEventLedgerEntity>(x => x.Source == "email" && x.EventId == "<m1@example.org>"), Arg.Any<IClientSessionHandle>());
    }

    [Fact]
    public async Task AnEventAnotherInstanceRecordedFirst_IsSeenAsHandled_WithNoErrorLogged()
    {
        // Not seen at the first look, then the insert answers false because the unique index holds it, and a second look finds it.
        _collection.CountAsync(Arg.Any<FilterDefinition<SupportEventLedgerEntity>>()).Returns(0L, 1L);
        _collection.TryAddAsync(Arg.Any<SupportEventLedgerEntity>(), Arg.Any<IClientSessionHandle>()).Returns(false);

        Assert.False(await new MongoSupportEventLedger(_collection, TimeProvider.System).TryRecordAsync("email", "<m1@example.org>", TestContext.Current.CancellationToken));
        await _collection.DidNotReceive().AddAsync(Arg.Any<SupportEventLedgerEntity>());
    }

    [Fact]
    public async Task AnInsertThatFailedForAnotherReason_IsNotTakenForAnEventAlreadyHandled()
    {
        _collection.CountAsync(Arg.Any<FilterDefinition<SupportEventLedgerEntity>>()).Returns(0L);
        _collection.TryAddAsync(Arg.Any<SupportEventLedgerEntity>(), Arg.Any<IClientSessionHandle>()).Returns(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new MongoSupportEventLedger(_collection, TimeProvider.System).TryRecordAsync("email", "<m1@example.org>", TestContext.Current.CancellationToken));
    }
}
