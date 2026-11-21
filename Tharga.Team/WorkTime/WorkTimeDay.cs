using System.Globalization;

namespace Tharga.Team.WorkTime;

/// <summary>Which day a moment belongs to, and the slots a day is made of (Tharga/Team#399).</summary>
public static class WorkTimeDay
{
    /// <summary>How long a slot is. The tracker writes once per person per slot.</summary>
    public static readonly TimeSpan Slot = TimeSpan.FromMinutes(15);

    /// <summary>The minutes in a slot, which is how many bits of a slot's bitmask are used.</summary>
    public const int MinutesPerSlot = 15;

    private const string Format = "yyyy-MM-dd";

    /// <summary>
    /// The day <paramref name="utc"/> falls on in <paramref name="zone"/>, as <c>yyyy-MM-dd</c>. Every offset in use is a multiple of 15 minutes, so a slot
    /// never straddles two days.
    /// </summary>
    public static string Of(DateTime utc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var instant = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(instant, zone).ToString(Format, CultureInfo.InvariantCulture);
    }

    /// <summary>The day as a date, or <c>null</c> for text that is not <c>yyyy-MM-dd</c>.</summary>
    public static DateOnly? Parse(string day)
        => DateOnly.TryParseExact(day, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;

    /// <summary>The text of a date, as <see cref="Of"/> gives it.</summary>
    public static string Text(DateOnly day) => day.ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>The start of the slot <paramref name="utc"/> falls in.</summary>
    public static DateTime SlotStartOf(DateTime utc)
        => new(utc.Ticks - utc.Ticks % Slot.Ticks, DateTimeKind.Utc);

    /// <summary>Which minute of its slot <paramref name="utc"/> is, 0 to 14.</summary>
    public static int MinuteOf(DateTime utc)
        => (int)(utc.Ticks % Slot.Ticks / TimeSpan.TicksPerMinute);

    /// <summary>The start of each active minute in a slot, earliest first.</summary>
    public static IEnumerable<DateTime> MinutesIn(DateTime slotStart, int minutes)
    {
        for (var bit = 0; bit < MinutesPerSlot; bit++)
        {
            if ((minutes & (1 << bit)) != 0) yield return slotStart.AddMinutes(bit);
        }
    }
}
