namespace Tharga.Team.Blazor.Features.User;

/// <summary>
/// Turns what a person typed into the mail log's filter. Separated from the component so the rules are testable without
/// rendering, as <see cref="UserAdminGate"/> is.
/// </summary>
internal static class UserMailLogQuery
{
    /// <summary>
    /// The filter for the values of the form, each blank one left out.
    /// </summary>
    /// <remarks>
    /// <b>The days are the reader's own.</b> "To" is a day and includes all of it, so it becomes the start of the next day,
    /// which the filter excludes; both are read in <paramref name="zone"/> and given in UTC. A recipient with an
    /// <c>@</c> in it is an address and any other is a user key, since a key never holds one.
    /// </remarks>
    public static UserMailFilter Build(UserMailKind? kind, UserMailStatus? status, string category, DateTime? fromDay, DateTime? toDay, string recipient, string team, TimeZoneInfo zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var who = string.IsNullOrWhiteSpace(recipient) ? null : recipient.Trim();

        return new UserMailFilter
        {
            Kind = kind,
            Status = status,
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            From = fromDay is { } from ? StartOf(from, zone) : null,
            To = toDay is { } to ? StartOf(to.AddDays(1), zone) : null,
            RecipientUserKey = who != null && !who.Contains('@') ? who : null,
            RecipientAddress = who != null && who.Contains('@') ? who : null,
            TeamKey = string.IsNullOrWhiteSpace(team) ? null : team.Trim()
        };
    }

    /// <remarks>In a zone that moves its clocks forward at midnight, the start of that day does not exist; the day then starts when the clocks have moved.</remarks>
    private static DateTime StartOf(DateTime day, TimeZoneInfo zone)
    {
        var start = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(start)) start = start.AddHours(1);

        return TimeZoneInfo.ConvertTimeToUtc(start, zone);
    }
}
