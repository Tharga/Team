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
        await _collection.DidNotReceive().AddAsync(Arg.Any<SupportEventLedgerEntity>());
    }

    [Fact]
    public async Task ANewEvent_IsRecorded()
    {
        _collection.CountAsync(Arg.Any<FilterDefinition<SupportEventLedgerEntity>>()).Returns(0L);

        var recorded = await new MongoSupportEventLedger(_collection, TimeProvider.System).TryRecordAsync("email", "<m1@example.org>", TestContext.Current.CancellationToken);

        Assert.True(recorded);
        await _collection.Received(1).AddAsync(Arg.Is<SupportEventLedgerEntity>(x => x.Source == "email" && x.EventId == "<m1@example.org>"));
    }
}
