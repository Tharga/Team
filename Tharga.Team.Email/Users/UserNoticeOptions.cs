namespace Tharga.Team.Email.Users;

/// <summary>Automatic notices: the catalogue, when the daily summary goes out, how long a burst is collected, and who sets a team's defaults.</summary>
/// <remarks>
/// Notices are mail to users, so they go out through the queue, sending address and categories of
/// <see cref="UserMailOptions"/>, and their links are built on its <see cref="UserMailOptions.BaseUrl"/>.
/// </remarks>
public class UserNoticeOptions
{
    private readonly List<UserNoticeType> _notices = [];

    /// <summary>The local time of day the daily summary is sent, in <see cref="TimeZoneId"/>, default 07:00.</summary>
    public TimeOnly DigestTime { get; set; } = new(7, 0);

    /// <summary>The time zone <see cref="DigestTime"/> is read in, as a system time zone id such as <c>Europe/Stockholm</c>, default <c>UTC</c>.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>How long a notice delivered at once waits for others of the same notice to the same user, which then go in the same mail, default two minutes.</summary>
    public TimeSpan BurstWindow { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The most notices one mail lists; any more wait for the next, default 100.</summary>
    public int MaxItemsPerMail { get; set; } = 100;

    /// <summary>How often raised notices and due mail are looked for when none were found, default 15 seconds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How long an instance holds what it claimed before another may take it over, default five minutes.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The most occurrences and mails one pass handles of each before looking again at once, default 50.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>How many times finding an occurrence's recipients is attempted before it is given up, default 5.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>The least privileged access level granted <see cref="NoticeScopes.Manage"/> on its team, default administrator; null makes it grant-only.</summary>
    public AccessLevel? ManageAccessLevel { get; set; } = AccessLevel.Administrator;

    /// <summary>The catalogue, in the order the notices were added.</summary>
    public IReadOnlyList<UserNoticeType> Notices => _notices;

    /// <summary>Adds a notice to the catalogue; it is checked when notices are registered.</summary>
    public UserNoticeOptions Add(UserNoticeType notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        _notices.Add(notice);
        return this;
    }
}
