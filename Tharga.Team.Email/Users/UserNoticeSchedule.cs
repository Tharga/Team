using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Users;

/// <summary>When the daily summary goes out: the configured local time of day in the configured time zone.</summary>
internal sealed class UserNoticeSchedule(IOptions<UserNoticeOptions> options)
{
    private readonly TimeZoneInfo _zone = Zone(options.Value.TimeZoneId);
    private readonly TimeOnly _time = options.Value.DigestTime;

    /// <summary>The first summary time after <paramref name="utcNow"/>, in UTC.</summary>
    public DateTime NextDigest(DateTime utcNow)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), _zone);
        var date = DateOnly.FromDateTime(local);

        for (var day = 0; day < 3; day++)
        {
            var candidate = ToUtc(date.AddDays(day).ToDateTime(_time));
            if (candidate > utcNow) return candidate;
        }

        return utcNow.AddDays(1);
    }

    /// <summary>The local date at <paramref name="utcNow"/> in the summary's time zone.</summary>
    public DateOnly LocalDate(DateTime utcNow)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), _zone));

    /// <summary>The time zone with this id, refusing one the system does not know.</summary>
    /// <exception cref="InvalidOperationException">The id is not a known time zone.</exception>
    public static TimeZoneInfo Zone(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"{nameof(UserNoticeOptions)}.{nameof(UserNoticeOptions.TimeZoneId)} '{id}' is not a time zone this system knows.", e);
        }
    }

    /// <remarks>A local time skipped by a clock change is taken an hour later; one that happens twice, the first time.</remarks>
    private DateTime ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (_zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
        if (_zone.IsAmbiguousTime(unspecified)) return DateTime.SpecifyKind(unspecified - _zone.GetAmbiguousTimeOffsets(unspecified).Max(), DateTimeKind.Utc);

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, _zone);
    }
}
