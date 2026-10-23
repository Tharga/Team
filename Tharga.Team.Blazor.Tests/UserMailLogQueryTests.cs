using Tharga.Team.Blazor.Features.User;

namespace Tharga.Team.Blazor.Tests;

/// <summary>What was typed in the mail log's form becomes its filter: blanks left out, an address told from a key, and the days the reader's own.</summary>
public class UserMailLogQueryTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly TimeZoneInfo TwoHoursAhead = TimeZoneInfo.CreateCustomTimeZone("ahead", TimeSpan.FromHours(2), "ahead", "ahead");

    [Fact]
    public void AnEmptyForm_IsAnEmptyFilter()
    {
        Assert.Equal(new UserMailFilter(), UserMailLogQuery.Build(null, null, null, null, null, null, null, Utc));
        Assert.Equal(new UserMailFilter(), UserMailLogQuery.Build(null, null, " ", null, null, "  ", "", Utc));
    }

    [Fact]
    public void EveryValueGiven_IsKept_Trimmed()
    {
        var filter = UserMailLogQuery.Build(UserMailKind.Notice, UserMailStatus.Failed, " news ", null, null, " user-1 ", " acme ", Utc);

        Assert.Equal(new UserMailFilter { Kind = UserMailKind.Notice, Status = UserMailStatus.Failed, Category = "news", RecipientUserKey = "user-1", TeamKey = "acme" }, filter);
    }

    [Theory]
    [InlineData("anna@example.org", null, "anna@example.org")]
    [InlineData(" Anna@Example.org ", null, "Anna@Example.org")]
    [InlineData("user-1", "user-1", null)]
    public void ARecipientWithAnAt_IsAnAddress_AndAnyOtherIsAUserKey(string typed, string key, string address)
    {
        var filter = UserMailLogQuery.Build(null, null, null, null, null, typed, null, Utc);

        Assert.Equal((key, address), (filter.RecipientUserKey, filter.RecipientAddress));
    }

    [Fact]
    public void TheFromDay_IsIncluded_FromItsStart_AndTheToDayIsIncludedUpToTheStartOfTheNext()
    {
        var filter = UserMailLogQuery.Build(null, null, null, new DateTime(2026, 10, 5, 15, 30, 0), new DateTime(2026, 10, 7, 9, 0, 0), null, null, Utc);

        Assert.Equal(new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc), filter.From);
        Assert.Equal(new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc), filter.To);
        Assert.Equal(DateTimeKind.Utc, filter.From!.Value.Kind);
    }

    [Fact]
    public void TheDays_AreTheReadersOwn_SoTheyAreGivenInUtc()
    {
        var filter = UserMailLogQuery.Build(null, null, null, new DateTime(2026, 10, 5), new DateTime(2026, 10, 5), null, null, TwoHoursAhead);

        Assert.Equal(new DateTime(2026, 10, 4, 22, 0, 0, DateTimeKind.Utc), filter.From);
        Assert.Equal(new DateTime(2026, 10, 5, 22, 0, 0, DateTimeKind.Utc), filter.To);
    }

    [Fact]
    public void ADayThatStartsWhenTheClocksMoveForward_StartsWhenTheyHave()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 15);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 15);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        var zone = TimeZoneInfo.CreateCustomTimeZone("gap", TimeSpan.Zero, "gap", "gap-standard", "gap-daylight", [rule]);
        Assert.True(zone.IsInvalidTime(new DateTime(2026, 3, 15, 0, 30, 0, DateTimeKind.Unspecified)));

        var filter = UserMailLogQuery.Build(null, null, null, new DateTime(2026, 3, 15), null, null, null, zone);

        Assert.Equal(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), filter.From);
    }
}
