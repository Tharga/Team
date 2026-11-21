using Tharga.Team.WorkTime;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The record of when people worked (Tharga/Team#399, phase 3): one document per person, team and day, written by updates that merge, so tabs and instances
/// reporting the same person converge on the union whatever order they land in.
/// </summary>
public class MongoWorkTimeStoreTests
{
    private static readonly DateTime Nine = new(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);
    private const string Day = "2026-11-21";

    private readonly InMemoryWorkDayCollection _memory = new();
    private readonly MongoWorkTimeStore _sut;

    public MongoWorkTimeStoreTests() => _sut = new MongoWorkTimeStore(_memory.Collection);

    private static int Bits(params int[] minutes) => minutes.Aggregate(0, (mask, minute) => mask | (1 << minute));

    private async Task<WorkDay> Single(string user = "u1", string team = "t1", string from = Day, string to = Day)
        => Assert.Single(await _sut.GetAsync(user, team, from, to).ToListAsync());

    [Fact]
    public async Task TheFirstWriteOfADay_CreatesIt()
    {
        await _sut.RecordAsync("u1", "t1", Day, Nine, Bits(2, 3, 4));

        var day = await Single();
        Assert.Equal(Nine.AddMinutes(2), day.First);
        Assert.Equal(Nine.AddMinutes(4), day.Last);
        Assert.Equal(Bits(2, 3, 4), Assert.Single(day.Slots).Value);
        Assert.Equal(Nine, Assert.Single(day.Slots).Key);
    }

    /// <summary>Two tabs, or two instances, report the same slot: the minutes of both are kept, neither replaces the other.</summary>
    [Fact]
    public async Task TheSameSlotWrittenTwice_KeepsTheUnionOfTheMinutes()
    {
        await _sut.RecordAsync("u1", "t1", Day, Nine, Bits(0, 1));
        await _sut.RecordAsync("u1", "t1", Day, Nine, Bits(1, 5));

        Assert.Equal(Bits(0, 1, 5), (await Single()).Slots[Nine]);
        Assert.Single(_memory.Documents);
    }

    [Fact]
    public async Task AnotherSlot_IsAddedToTheDay_AndFirstAndLastWiden()
    {
        await _sut.RecordAsync("u1", "t1", Day, Nine.AddMinutes(30), Bits(3));
        await _sut.RecordAsync("u1", "t1", Day, Nine, Bits(7));
        await _sut.RecordAsync("u1", "t1", Day, Nine.AddMinutes(60), Bits(1));

        var day = await Single();
        Assert.Equal(3, day.Slots.Count);
        Assert.Equal(Nine.AddMinutes(7), day.First);
        Assert.Equal(Nine.AddMinutes(61), day.Last);
    }

    /// <summary>Instances do not agree on an order, so the result must not depend on one.</summary>
    [Fact]
    public async Task TheOrderOfTheWrites_DoesNotMatter()
    {
        var writes = new[] { (Nine, Bits(1, 2)), (Nine.AddMinutes(15), Bits(4)), (Nine, Bits(2, 9)), (Nine.AddMinutes(45), Bits(0)) };

        await Write(writes);
        var forward = await Single();

        var other = new InMemoryWorkDayCollection();
        var reversed = new MongoWorkTimeStore(other.Collection);
        foreach (var (slot, minutes) in writes.Reverse()) await reversed.RecordAsync("u1", "t1", Day, slot, minutes);
        var backward = Assert.Single(await reversed.GetAsync("u1", "t1", Day, Day).ToListAsync());

        Assert.Equal(forward.First, backward.First);
        Assert.Equal(forward.Last, backward.Last);
        Assert.Equal(forward.Slots.OrderBy(x => x.Key), backward.Slots.OrderBy(x => x.Key));
    }

    private async Task Write((DateTime Slot, int Minutes)[] writes)
    {
        foreach (var (slot, minutes) in writes) await _sut.RecordAsync("u1", "t1", Day, slot, minutes);
    }

    [Fact]
    public async Task EachPersonTeamAndDay_IsADocumentOfItsOwn()
    {
        await _sut.RecordAsync("u1", "t1", Day, Nine, Bits(1));
        await _sut.RecordAsync("u2", "t1", Day, Nine, Bits(1));
        await _sut.RecordAsync("u1", "t2", Day, Nine, Bits(1));
        await _sut.RecordAsync("u1", "t1", "2026-11-22", Nine.AddDays(1), Bits(1));

        Assert.Equal(4, _memory.Documents.Count);
    }

    /// <summary>Only fifteen minutes fit in a slot, so a bit above them is not a minute and is not kept.</summary>
    [Fact]
    public async Task BitsBeyondTheSlot_AreNotKept()
    {
        await _sut.RecordAsync("u1", "t1", Day, Nine, Bits(3) | (1 << 20));

        Assert.Equal(Bits(3), (await Single()).Slots[Nine]);
    }

    [Theory]
    [InlineData(null, "t1", Day, 1)]
    [InlineData("u1", null, Day, 1)]
    [InlineData("u1", "t1", null, 1)]
    [InlineData("u1", "t1", Day, 0)]
    public async Task WithoutWhoWhereWhenOrMinutes_NothingIsWritten(string user, string team, string day, int minutes)
    {
        await _sut.RecordAsync(user, team, day, Nine, minutes);

        Assert.Empty(_memory.Documents);
    }

    [Fact]
    public async Task ARange_ReturnsOnlyThatPersonAndTeam_OldestFirst()
    {
        await _sut.RecordAsync("u1", "t1", "2026-11-23", Nine.AddDays(2), Bits(1));
        await _sut.RecordAsync("u1", "t1", "2026-11-21", Nine, Bits(1));
        await _sut.RecordAsync("u1", "t1", "2026-11-22", Nine.AddDays(1), Bits(1));
        await _sut.RecordAsync("u2", "t1", "2026-11-22", Nine.AddDays(1), Bits(1));
        await _sut.RecordAsync("u1", "t2", "2026-11-22", Nine.AddDays(1), Bits(1));

        var days = await _sut.GetAsync("u1", "t1", "2026-11-21", "2026-11-22").ToListAsync();

        Assert.Equal(["2026-11-21", "2026-11-22"], days.Select(x => x.Day));
    }

    /// <summary>What comes back is what the report reads: the day as it was written, in UTC.</summary>
    [Fact]
    public async Task WhatIsRead_FeedsTheReport()
    {
        await _sut.RecordAsync("u1", "t1", Day, Nine, Bits(0, 1, 2));
        await _sut.RecordAsync("u1", "t1", Day, Nine.AddHours(4), Bits(5));

        var summary = WorkTimeReport.Summarize(await Single(), TimeSpan.FromMinutes(30));

        Assert.Equal(DateTimeKind.Utc, summary.Start!.Value.Kind);
        Assert.Equal(Nine, summary.Start);
        Assert.Equal(2, summary.Sessions.Count);
        Assert.Single(summary.Breaks);
    }

    [Fact]
    public void ASlotsKey_IsItsNumberSinceTheEpoch_AndBack()
    {
        var key = MongoWorkTimeStore.SlotKey(Nine);

        Assert.Equal(((Nine - DateTime.UnixEpoch).Ticks / TimeSpan.FromMinutes(15).Ticks).ToString(), key);
        Assert.Equal(Nine, MongoWorkTimeStore.SlotStartOf(key));
    }

    // --- the collection ---

    private static IEnumerable<global::MongoDB.Driver.CreateIndexModel<WorkDayEntity>> Indices(TimeSpan? retention) => WorkDayCollection.IndicesFor(retention);

    [Fact]
    public void ADayIsUniquePerPersonTeamAndDay()
    {
        var index = Assert.Single(Indices(null), x => x.Options.Name == "UserKey_TeamKey_Day");

        Assert.True(index.Options.Unique);
    }

    /// <summary>Kept for good unless the host says otherwise, so there is nothing that expires by default.</summary>
    [Fact]
    public void WithNoRetention_NothingExpires()
    {
        Assert.DoesNotContain(Indices(null), x => x.Options.ExpireAfter != null);
    }

    [Fact]
    public void WithARetention_ADayExpiresThatLongAfterItsLastActivity()
    {
        var index = Assert.Single(Indices(TimeSpan.FromDays(90)), x => x.Options.ExpireAfter != null);

        Assert.Equal(TimeSpan.FromDays(90), index.Options.ExpireAfter);
    }

    [Fact]
    public void ARetentionOfNothing_IsNoRetention()
    {
        Assert.DoesNotContain(Indices(TimeSpan.Zero), x => x.Options.ExpireAfter != null);
    }
}
