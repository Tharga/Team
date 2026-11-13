namespace Tharga.Team.Blazor.Features.User;

/// <summary>
/// How an absent timestamp reads on the admin grids.
/// </summary>
/// <remarks>
/// A blank cell is ambiguous — it reads as "we do not know" when the truth is "this has never happened".
/// On a surface where the question is whether an account or a team is still in use, that distinction is
/// the whole point of the column.
/// </remarks>
public static class LastSeenText
{
    /// <summary>Rendered in place of a date when there is none.</summary>
    public const string Never = "Never";

    /// <summary>True when the value should render as <see cref="Never"/> rather than as a date.</summary>
    public static bool IsNever(DateTime? value) => !value.HasValue;

    /// <summary>A timestamp as text, to the minute, for a place that takes a string rather than a date component.</summary>
    public static string Format(DateTime value) => value.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
