namespace Tharga.Team;

/// <summary>How often one category of mail was sent, per user who received it, over the period the outbox can still count.</summary>
/// <remarks>
/// <b>Sent means it left</b>: a mail that bounced was sent and counts; one queued, skipped or given up does not. Only mail
/// to users counts — mail to an address alone, such as an invitation, goes to people who may not be users. The figure is
/// the average for those who received at least one, so a category sent to a few users often reads as often.
/// </remarks>
public sealed record UserMailCategoryFrequency
{
    private const double DaysPerMonth = 365.25 / 12;

    /// <summary>The category's key.</summary>
    public required string Category { get; init; }

    /// <summary>How many mails in the category were sent to users in the period.</summary>
    public int Sent { get; init; }

    /// <summary>How many users received at least one of them.</summary>
    public int Recipients { get; init; }

    /// <summary>When the period counted starts, in UTC: a year back, or less when the outbox keeps done mail for less long.</summary>
    public required DateTime Since { get; init; }

    /// <summary>When the period counted ends, in UTC.</summary>
    public required DateTime Until { get; init; }

    /// <summary>Whether the period counted is a whole year.</summary>
    public bool CoversAYear => Until - Since >= TimeSpan.FromDays(365);

    /// <summary>The mails one user who received the category got, on average, per month; zero when none was sent.</summary>
    public double PerMonth => Sent <= 0 || Recipients <= 0 || Until <= Since ? 0 : Sent / (double)Recipients / ((Until - Since).TotalDays / DaysPerMonth);

    /// <summary>The words for it, such as "About 2 a month".</summary>
    public UserMailFrequencyPhrase Phrase => UserMailFrequencyPhrase.Of(this);
}
