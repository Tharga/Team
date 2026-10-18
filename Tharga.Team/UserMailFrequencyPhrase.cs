using System.Globalization;

namespace Tharga.Team;

/// <summary>The words for how often a category is sent: which text, by its key and English default, and the number it holds.</summary>
/// <remarks>
/// <b>The one place the rounding is decided.</b> None sent reads as none in the past year, or none recently when the
/// period counted is shorter; under one a year as fewer than one a year; under about twelve a year as about N a year;
/// under four and a half a month as about N a month; anything more as about N a week. N is rounded to the nearest whole
/// number and is never below one.
/// </remarks>
/// <param name="Key">The text key, one of the <c>UserMailTexts.Frequency…Key</c> constants.</param>
/// <param name="English">The English default, holding <c>{0}</c> where the number goes when it has one.</param>
/// <param name="Count">The number, or zero for a phrase without one.</param>
public sealed record UserMailFrequencyPhrase(string Key, string English, int Count)
{
    private const double WeeksPerMonth = 365.25 / 7 / 12;

    /// <summary>The words for a category's frequency.</summary>
    public static UserMailFrequencyPhrase Of(UserMailCategoryFrequency frequency)
    {
        ArgumentNullException.ThrowIfNull(frequency);

        return frequency.Sent <= 0 ? None(frequency.CoversAYear) : Of(frequency.PerMonth);
    }

    /// <summary>The words for mail received on average <paramref name="perMonth"/> times a month.</summary>
    public static UserMailFrequencyPhrase Of(double perMonth)
    {
        if (perMonth <= 0) return None(true);

        var perYear = perMonth * 12;

        if (perYear < 1) return new UserMailFrequencyPhrase(UserMailTexts.FrequencyUnderOneAYearKey, UserMailTexts.FrequencyUnderOneAYear, 0);
        if (perYear < 11.5) return new UserMailFrequencyPhrase(UserMailTexts.FrequencyPerYearKey, UserMailTexts.FrequencyPerYear, Rounded(perYear));
        if (perMonth < 4.5) return new UserMailFrequencyPhrase(UserMailTexts.FrequencyPerMonthKey, UserMailTexts.FrequencyPerMonth, Rounded(perMonth));

        return new UserMailFrequencyPhrase(UserMailTexts.FrequencyPerWeekKey, UserMailTexts.FrequencyPerWeek, Rounded(perMonth / WeeksPerMonth));
    }

    /// <summary>The words for none sent: in the past year when a whole year was counted, recently otherwise.</summary>
    public static UserMailFrequencyPhrase None(bool coversAYear)
        => coversAYear
            ? new UserMailFrequencyPhrase(UserMailTexts.FrequencyNoneKey, UserMailTexts.FrequencyNone, 0)
            : new UserMailFrequencyPhrase(UserMailTexts.FrequencyNoneRecentlyKey, UserMailTexts.FrequencyNoneRecently, 0);

    /// <summary>The phrase in <paramref name="template"/>, its translation, with the number in place; the English when the translation does not format.</summary>
    public string Format(string template)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, string.IsNullOrWhiteSpace(template) ? English : template, Count);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.CurrentCulture, English, Count);
        }
    }

    private static int Rounded(double value) => Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
}
