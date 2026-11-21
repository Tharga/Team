namespace Tharga.Team.WorkTime;

/// <summary>
/// Where a person's active minutes are kept, one record per person, team and day (Tharga/Team#399, phase 3).
/// </summary>
/// <remarks>
/// <b>Nothing derived is stored.</b> A day holds when the first and last active minute were and, per 15-minute slot, which minutes were active; start, end,
/// breaks and worked time are computed from that by <see cref="WorkTimeReport"/>, so a different <c>BreakAfter</c> or a better rule applies to history too.
/// <para>
/// <b>An unchecked store.</b> It names the user it writes for, so it is the circuit's own tracker that records through it, for the caller, and a person reads
/// their own days through <see cref="IWorkTimeService"/>. Do not hand it to code that takes a user key from outside.
/// </para>
/// <para>
/// Writes must merge, since tabs and instances report the same person: a slot's minutes are or'd into what is there, and first and last only ever widen.
/// </para>
/// </remarks>
public interface IWorkTimeStore
{
    /// <summary>
    /// Records <paramref name="minutes"/>, a bitmask of the active minutes of the slot starting at <paramref name="slotStart"/> (bit 0 is the slot's first
    /// minute), on <paramref name="day"/>. Merges with what is already recorded for the slot.
    /// </summary>
    /// <param name="userKey">The person.</param>
    /// <param name="teamKey">The team they were active in.</param>
    /// <param name="day">The day the slot falls on in the configured time zone, as <c>yyyy-MM-dd</c> (<see cref="WorkTimeDay"/>).</param>
    /// <param name="slotStart">The start of the slot, in UTC, a multiple of 15 minutes.</param>
    /// <param name="minutes">The active minutes of the slot, bit 0 to bit 14.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task RecordAsync(string userKey, string teamKey, string day, DateTime slotStart, int minutes, CancellationToken cancellationToken = default);

    /// <summary>The days of <paramref name="userKey"/> in <paramref name="teamKey"/> from <paramref name="fromDay"/> to <paramref name="toDay"/>, both included, oldest first.</summary>
    IAsyncEnumerable<WorkDay> GetAsync(string userKey, string teamKey, string fromDay, string toDay, CancellationToken cancellationToken = default);
}

/// <summary>One person's recorded activity in one team on one day.</summary>
/// <param name="UserKey">The person.</param>
/// <param name="TeamKey">The team.</param>
/// <param name="Day">The day in the configured time zone, as <c>yyyy-MM-dd</c>.</param>
/// <param name="First">The start of the first active minute, in UTC.</param>
/// <param name="Last">The start of the last active minute, in UTC.</param>
/// <param name="Slots">The active minutes per slot: the slot's start, in UTC, and a bitmask with bit 0 as the slot's first minute.</param>
public sealed record WorkDay(string UserKey, string TeamKey, string Day, DateTime First, DateTime Last, IReadOnlyDictionary<DateTime, int> Slots);
