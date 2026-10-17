using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>The daily summary goes out at the host's time of day in the host's time zone, through clock changes.</summary>
public class UserNoticeScheduleTests
{
    private static UserNoticeSchedule Schedule(string zone, int hour = 7, int minute = 0)
        => new(Microsoft.Extensions.Options.Options.Create(new UserNoticeOptions { TimeZoneId = zone, DigestTime = new TimeOnly(hour, minute) }));

    private static DateTime Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(6, 59, 17)]
    [InlineData(7, 0, 18)]
    [InlineData(12, 0, 18)]
    public void InUtc_TheNextSummary_IsTheNextSevenOClock(int hour, int minute, int expectedDay)
    {
        Assert.Equal(Utc(10, expectedDay, 7), Schedule("UTC").NextDigest(Utc(10, 17, hour, minute)));
    }

    [Fact]
    public void InAZoneWithSummerTime_TheLocalTimeOfDayIsKept()
    {
        var stockholm = Schedule("Europe/Stockholm");

        Assert.Equal(Utc(7, 1, 5), stockholm.NextDigest(Utc(7, 1, 3)));
        Assert.Equal(Utc(12, 2, 6), stockholm.NextDigest(Utc(12, 1, 6, 30)));
    }

    [Fact]
    public void ATimeOfDayTheClockChangeSkips_IsTakenAnHourLater()
    {
        Assert.Equal(Utc(3, 29, 1, 30), Schedule("Europe/Stockholm", 2, 30).NextDigest(Utc(3, 28, 12)));
    }

    [Fact]
    public void TheLocalDate_IsTheZones()
    {
        Assert.Equal(new DateOnly(2026, 10, 18), Schedule("Europe/Stockholm").LocalDate(Utc(10, 17, 23, 30)));
        Assert.Equal(new DateOnly(2026, 10, 17), Schedule("UTC").LocalDate(Utc(10, 17, 23, 30)));
    }
}
