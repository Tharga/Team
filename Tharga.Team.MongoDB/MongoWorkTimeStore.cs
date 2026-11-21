using System.Globalization;
using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.Team.WorkTime;

namespace Tharga.Team.MongoDB;

/// <summary>MongoDB adapter for <see cref="IWorkTimeStore"/>: one document per person, team and day, written by updates that merge (Tharga/Team#399).</summary>
/// <remarks>
/// <b>Every write is an update that merges, never a read and replace.</b> A slot's minutes are or'd in with <c>$bit</c>, and first and last widen with
/// <c>$min</c> and <c>$max</c>, so several tabs and several instances reporting the same person converge on the union, whatever order they land in.
/// <para>
/// The first write of a day finds no document to update and creates it; if another instance created it in the meantime, the unique index refuses the second and
/// the write goes round again as an update. It is one extra round trip, once a day per person and team.
/// </para>
/// </remarks>
internal sealed class MongoWorkTimeStore(IWorkDayCollection collection) : IWorkTimeStore
{
    private static readonly DateTime Epoch = DateTime.UnixEpoch;

    private static FilterDefinitionBuilder<WorkDayEntity> Filter => Builders<WorkDayEntity>.Filter;

    public async Task RecordAsync(string userKey, string teamKey, string day, DateTime slotStart, int minutes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userKey) || string.IsNullOrEmpty(teamKey) || string.IsNullOrEmpty(day)) return;

        // Bits above the slot's fifteen minutes are not minutes.
        minutes &= (1 << WorkTimeDay.MinutesPerSlot) - 1;
        if (minutes == 0) return;

        var active = WorkTimeDay.MinutesIn(slotStart, minutes).ToList();
        var first = active[0];
        var last = active[^1];
        var key = SlotKey(slotStart);

        var matching = Filter.Eq(x => x.UserKey, userKey) & Filter.Eq(x => x.TeamKey, teamKey) & Filter.Eq(x => x.Day, day);
        var merge = Builders<WorkDayEntity>.Update
            .BitwiseOr(new StringFieldDefinition<WorkDayEntity, int>($"{nameof(WorkDayEntity.Slots)}.{key}"), minutes)
            .Min(x => x.First, first)
            .Max(x => x.Last, last);

        if ((await collection.UpdateOneAsync(matching, merge, OneOption<WorkDayEntity>.FirstOrDefault))?.Before != null) return;

        try
        {
            await collection.AddAsync(new WorkDayEntity
            {
                Id = ObjectId.GenerateNewId(),
                UserKey = userKey,
                TeamKey = teamKey,
                Day = day,
                First = first,
                Last = last,
                Slots = new Dictionary<string, int> { [key] = minutes }
            });
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Another instance created the day first: this is an ordinary merge into theirs.
            await collection.UpdateOneAsync(matching, merge, OneOption<WorkDayEntity>.FirstOrDefault);
        }
    }

    public async IAsyncEnumerable<WorkDay> GetAsync(string userKey, string teamKey, string fromDay, string toDay, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userKey) || string.IsNullOrEmpty(teamKey)) yield break;

        var filter = Filter.Eq(x => x.UserKey, userKey) & Filter.Eq(x => x.TeamKey, teamKey)
                     & Filter.Gte(x => x.Day, fromDay) & Filter.Lte(x => x.Day, toDay);

        var days = new List<WorkDayEntity>();
        await foreach (var entity in collection.GetAsync(filter, cancellationToken: cancellationToken))
        {
            days.Add(entity);
        }

        foreach (var entity in days.OrderBy(x => x.Day, StringComparer.Ordinal)) yield return ToWorkDay(entity);
    }

    /// <summary>The number of the slot since the Unix epoch, which is the key the minutes are kept under.</summary>
    internal static string SlotKey(DateTime slotStart)
        => ((DateTime.SpecifyKind(slotStart, DateTimeKind.Utc) - Epoch).Ticks / WorkTimeDay.Slot.Ticks).ToString(CultureInfo.InvariantCulture);

    internal static DateTime SlotStartOf(string key)
        => Epoch.AddTicks(long.Parse(key, CultureInfo.InvariantCulture) * WorkTimeDay.Slot.Ticks);

    internal static WorkDay ToWorkDay(WorkDayEntity entity) => new(
        entity.UserKey,
        entity.TeamKey,
        entity.Day,
        DateTime.SpecifyKind(entity.First, DateTimeKind.Utc),
        DateTime.SpecifyKind(entity.Last, DateTimeKind.Utc),
        entity.Slots.ToDictionary(x => SlotStartOf(x.Key), x => x.Value));
}
